using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using BetterGenshinImpact.Platform.Linux.X11;
using BetterGenshinImpact.Service.Guide;
using BetterGenshinImpact.View.Controls.Guide;
using BetterGenshinImpact.ViewModel.Windows;
using FluentAvalonia.UI.Controls;
using Serilog;

namespace BetterGenshinImpact.Avalonia.Views.Guide;

/// <summary>
/// 悬浮小窗（对应 WPF 的 GuideWindow）：无边框、置顶、显示时不抢焦点，网页为 NativeWebView（WebKitGTK），
/// 窗口栏、热键反馈、进度线与网页异常说明由同一个 <see cref="GuideWindowViewModel"/> 驱动。
/// 与 Windows 的差别：原生网页控件之上不能叠加 Avalonia 控件，窗口栏显示时占用网页上方一行、热键反馈与进度线在网页下方一行；
/// 鼠标穿透用 X SHAPE 输入区域（同遮罩）；拖动、缩放由窗口管理器完成（_NET_WM_MOVERESIZE）
/// </summary>
public sealed class GuideWindow : Window, IGuideWindowView
{
    private readonly GuidePlayerBridge _bridge;
    private readonly NativeWebView _webView;
    private readonly Border _bar;
    private readonly Border _footer;
    private readonly TextBlock _osd;
    private readonly ProgressBar _progress;
    private readonly Border _notice;
    private readonly TextBox _addressBox;
    private readonly Border _titleArea;
    private readonly StackPanel _buttons;
    private readonly string? _display = Environment.GetEnvironmentVariable("DISPLAY");
    private bool _allowClose;
    private bool _clickThrough;

    public GuideWindow(GuideWindowViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        Title = "悬浮小窗";
        Width = 480;
        Height = 270;
        WindowDecorations = WindowDecorations.None;
        ShowActivated = false;
        ShowInTaskbar = false;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Black;
        this.Bind(TopmostProperty, new Binding("State.IsTopmost") { Mode = BindingMode.OneWay });
        this.Bind(OpacityProperty, new Binding("State.Opacity") { Mode = BindingMode.OneWay });

        // 一次性迁移：早期版本的网页数据在 WebKitGTK 默认目录，新目录不存在时先复制过来（失败只记日志）
        BetterGenshinImpact.Host.Common.Storage.LinuxWebDataMigration.MigrateGuide(BetterGenshinImpact.CoreServices.GetLogger<GuideWindow>());
        _webView = new NativeWebView();
        _webView.EnvironmentRequested += (_, e) =>
        {
            if (e is GtkWebViewEnvironmentRequestedEventArgs gtk)
            {
                // 与 Windows 版独立的 Guide 环境对应：保留登录状态，数据放在 Data/WebKit/Guide（存储空间中可清理其缓存）
                gtk.EphemeralDataManager = false;
                gtk.BaseDataDirectory = BetterGenshinImpact.Host.Common.Storage.LinuxWebDataPaths.Data(BetterGenshinImpact.Host.Common.Storage.LinuxWebDataPaths.Guide);
                gtk.BaseCacheDirectory = BetterGenshinImpact.Host.Common.Storage.LinuxWebDataPaths.Cache(BetterGenshinImpact.Host.Common.Storage.LinuxWebDataPaths.Guide);
                gtk.ApplicationNameForUserAgent = "BetterGI";
            }
        };
        _bridge = new GuidePlayerBridge(_webView);

        // 窗口栏
        _addressBox = new TextBox { MinWidth = 120, VerticalContentAlignment = VerticalAlignment.Center, FontSize = 12 };
        _addressBox.Bind(TextBox.TextProperty, new Binding(nameof(GuideWindowViewModel.AddressText)) { Mode = BindingMode.TwoWay });
        _addressBox.Bind(IsVisibleProperty, new Binding(nameof(GuideWindowViewModel.IsEditingAddress)));
        _addressBox.PlaceholderText = "输入网址、BV 号或搜索词，回车打开";
        _addressBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                ViewModel.CommitAddressCommand.Execute(null);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                ViewModel.CancelEditAddressCommand.Execute(null);
            }
        };
        _addressBox.LostFocus += (_, _) =>
        {
            if (ViewModel.IsEditingAddress)
            {
                ViewModel.CancelEditAddressCommand.Execute(null);
            }
        };
        _addressBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && e.NewValue is true)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _addressBox.Focus();
                    _addressBox.SelectAll();
                }, DispatcherPriority.Input);
            }
        };

        var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12, Foreground = Brushes.White };
        title.Bind(TextBlock.TextProperty, new Binding("State.Title"));
        _titleArea = new Border { Child = title, Background = Brushes.Transparent, Padding = new Thickness(6, 0), Cursor = new Cursor(StandardCursorType.Hand) };
        _titleArea.Bind(IsVisibleProperty, new Binding(nameof(GuideWindowViewModel.IsEditingAddress)) { Converter = Not });
        _titleArea.PointerPressed += OnTitlePressed;
        ToolTip.SetTip(_titleArea, "点击输入网址、BV 号或搜索词，按住拖动小窗");

        _buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        _buttons.Bind(IsVisibleProperty, new Binding(nameof(GuideWindowViewModel.IsEditingAddress)) { Converter = Not });
        BuildButtons();

        var drag = new Border { Width = 14, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeAll), Child = new TextBlock { Text = "⋮⋮", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 10 } };
        drag.PointerPressed += (_, e) => DragAndCommit(e);
        var barGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Height = 30 };
        barGrid.Children.Add(drag);
        var middle = new Panel { Children = { _titleArea, _addressBox } };
        Grid.SetColumn(middle, 1);
        barGrid.Children.Add(middle);
        Grid.SetColumn(_buttons, 2);
        barGrid.Children.Add(_buttons);
        _bar = new Border { Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x20, 0x20, 0x20)), Child = barGrid };
        _bar.Bind(IsVisibleProperty, new Binding(nameof(GuideWindowViewModel.IsBarVisible)));
        _bar.PointerEntered += (_, _) => ViewModel.BarEnterCommand.Execute(null);
        _bar.PointerExited += (_, _) => ViewModel.BarLeaveCommand.Execute(null);
        _bar.PointerPressed += (_, e) =>
        {
            if (!e.Handled && e.GetCurrentPoint(_bar).Properties.IsLeftButtonPressed)
            {
                DragAndCommit(e);
            }
        };
        _bar.PointerWheelChanged += (_, e) =>
        {
            e.Handled = true;
            ViewModel.OnBarWheel((int)Math.Round(e.Delta.Y * 120));
        };

        // 网页异常说明：替换网页显示
        var noticeText = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
        noticeText.Bind(TextBlock.TextProperty, new Binding("State.NoticeText"));
        var reload = new Button { Content = "重新加载", HorizontalAlignment = HorizontalAlignment.Center, Command = ViewModel.ReloadAfterCrashCommand };
        reload.Bind(IsVisibleProperty, new Binding("State.CanReloadNotice"));
        _notice = new Border { Background = Brushes.Black, Child = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16), Children = { noticeText, reload } } };
        _notice.Bind(IsVisibleProperty, new Binding("State.HasNotice"));

        // 热键反馈、进度线
        _osd = new TextBlock { Foreground = Brushes.White, FontSize = 12, Margin = new Thickness(8, 2), TextTrimming = TextTrimming.CharacterEllipsis };
        _osd.Bind(TextBlock.TextProperty, new Binding("State.OsdText"));
        _osd.Bind(IsVisibleProperty, new Binding(nameof(GuideWindowViewModel.IsOsdVisible)));
        _progress = new ProgressBar { Minimum = 0, Maximum = 1, Height = 2, MinHeight = 2, Foreground = Brushes.DeepSkyBlue, Background = Brushes.Transparent };
        _progress.Bind(RangeBase.ValueProperty, new Binding("State.Progress"));
        _progress.Bind(IsVisibleProperty, new MultiBinding
        {
            Converter = new global::Avalonia.Data.Converters.FuncMultiValueConverter<object?, bool>(values =>
            {
                var list = values.ToArray();
                return list is [true, true, false];
            }),
            Bindings =
            {
                new Binding("Config.ShowProgressLine"),
                new Binding("State.HasProgress"),
                new Binding(nameof(GuideWindowViewModel.IsBarVisible)),
            },
        });
        _footer = new Border { Background = Brushes.Black, Child = new StackPanel { Children = { _osd, _progress } } };

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        root.Children.Add(_bar);
        var web = new Panel { Children = { _webView, _notice } };
        Grid.SetRow(web, 1);
        root.Children.Add(web);
        Grid.SetRow(_footer, 2);
        root.Children.Add(_footer);
        // 交互模式：强调色边框
        var frame = new Border { Child = root, BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)) };
        ViewModel.State.PropertyChanged += OnStateChanged;
        UpdateFrame(frame);
        Content = frame;

        PointerEntered += (_, _) => ViewModel.PointerEnterCommand.Execute(null);
        PointerExited += (_, _) => ViewModel.PointerLeaveCommand.Execute(null);
        PointerMoved += (_, _) => ViewModel.PointerMoveCommand.Execute(null);
        Activated += (_, _) => ViewModel.OnWindowActivated();
        Deactivated += (_, _) => ViewModel.OnWindowDeactivated();
        SizeChanged += (_, e) => ViewModel.UpdateBarLayout(e.NewSize.Width);
        Opened += (_, _) => ApplyClickThrough(ViewModel.State.IsClickThrough, force: true);
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            ViewModel.State.PropertyChanged -= OnStateChanged;
            ViewModel.Detach();
            _bridge.Dispose();
            ClosedView?.Invoke(this, EventArgs.Empty);
        };
        _frame = frame;
    }

    private readonly Border _frame;

    private static readonly global::Avalonia.Data.Converters.IValueConverter Not =
        new global::Avalonia.Data.Converters.FuncValueConverter<bool, bool>(value => !value);

    public GuideWindowViewModel ViewModel { get; }

    /// <summary>播放桥（自测用）</summary>
    public GuidePlayerBridge Bridge => _bridge;

    #region IGuideWindowView

    public IGuidePlayer Player => _bridge;

    public nint Handle => TryGetPlatformHandle()?.Handle ?? 0;

    public bool IsShown => IsVisible;

    public void ShowWithoutActivation()
    {
        if (!IsVisible)
        {
            Show();
        }
    }

    public void HideWindow()
    {
        if (IsVisible)
        {
            Hide();
        }
    }

    public void CloseWindow()
    {
        _allowClose = true;
        Close();
    }

    public void SetBounds(System.Drawing.Rectangle bounds)
    {
        var scale = DesktopScaling > 0 ? DesktopScaling : 1;
        Position = new PixelPoint(bounds.Left, bounds.Top);
        Width = bounds.Width / scale;
        Height = bounds.Height / scale;
    }

    public bool TryGetBounds(out System.Drawing.Rectangle bounds)
    {
        var scale = DesktopScaling > 0 ? DesktopScaling : 1;
        var size = ClientSize;
        bounds = new System.Drawing.Rectangle(Position.X, Position.Y, (int)Math.Round(size.Width * scale), (int)Math.Round(size.Height * scale));
        return bounds.Width > 0;
    }

    public void BringToTopmost()
    {
        if (!Topmost || !IsVisible)
        {
            return;
        }

        // 重新设置置顶，让窗口管理器把小窗放回置顶层最上面（不激活）
        Topmost = false;
        Topmost = true;
    }

    public void ActivateForInput()
    {
        Activate();
        if (!ViewModel.IsEditingAddress)
        {
            _webView.Focus();
        }
    }

    event EventHandler? IGuideWindowView.Closed
    {
        add => ClosedView += value;
        remove => ClosedView -= value;
    }

    private event EventHandler? ClosedView;

    #endregion

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GuideWindowState.IsInteraction):
                UpdateFrame(_frame);
                break;
            case nameof(GuideWindowState.IsClickThrough):
                ApplyClickThrough(ViewModel.State.IsClickThrough);
                break;
        }
    }

    private void UpdateFrame(Border frame)
    {
        frame.BorderBrush = ViewModel.State.IsInteraction
            ? new SolidColorBrush(Color.FromRgb(0x3A, 0x96, 0xDD))
            : new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
        frame.BorderThickness = new Thickness(ViewModel.State.IsInteraction ? 2 : 1);
    }

    /// <summary>鼠标穿透：X SHAPE 输入区域为空（同遮罩）；关闭时恢复整个窗口</summary>
    private void ApplyClickThrough(bool enabled, bool force = false)
    {
        if (!force && enabled == _clickThrough)
        {
            return;
        }

        var handle = Handle;
        if (handle == 0)
        {
            return;
        }

        _clickThrough = enabled;
        var error = X11Overlay.SetClickThrough(_display, handle, enabled);
        if (error is not null)
        {
            Log.Warning("悬浮小窗无法设置鼠标穿透：{Error}", error);
        }
    }

    private void OnTitlePressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var before = Position;
        BeginMoveDrag(e);
        if (Position == before && e.ClickCount == 1)
        {
            ViewModel.BeginEditAddressCommand.Execute(null);
        }
        else
        {
            ViewModel.CommitBounds();
        }
    }

    private void DragAndCommit(PointerPressedEventArgs e)
    {
        e.Handled = true;
        BeginMoveDrag(e);
        ViewModel.CommitBounds();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            ViewModel.OnCloseRequested();
        }
    }

    #region 窗口栏按钮与菜单

    private Button BarButton(FASymbol symbol, string tip, System.Windows.Input.ICommand? command, string? name = null)
    {
        var button = new Button
        {
            Content = new FASymbolIcon { Symbol = symbol, FontSize = 16 },
            Command = command,
            Padding = new Thickness(5, 3),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White,
            Focusable = false,
            VerticalAlignment = VerticalAlignment.Center,
            Name = name,
        };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private Button MenuButton(FASymbol symbol, string tip, string name, Action? prepare, Func<IEnumerable<object>> items)
    {
        var button = BarButton(symbol, tip, null, name);
        var flyout = new MenuFlyout();
        flyout.Opening += (_, _) =>
        {
            prepare?.Invoke();
            flyout.Items.Clear();
            foreach (var item in items())
            {
                flyout.Items.Add(item);
            }
        };
        flyout.Closed += (_, _) => ViewModel.MenuClosedCommand.Execute(null);
        button.Flyout = flyout;
        return button;
    }

    private static MenuItem Item(string header, System.Windows.Input.ICommand? command, object? parameter = null, bool? isChecked = null, bool enabled = true)
    {
        var item = new MenuItem { Header = header, Command = command, CommandParameter = parameter, IsEnabled = enabled };
        if (isChecked is { } value)
        {
            item.ToggleType = MenuItemToggleType.CheckBox;
            item.IsChecked = value;
        }

        return item;
    }

    private static MenuItem Sub(string header, IEnumerable<object> children)
    {
        var item = new MenuItem { Header = header };
        foreach (var child in children)
        {
            item.Items.Add(child);
        }

        return item;
    }

    private static IEnumerable<object> Options(IEnumerable<GuideMenuOption> options) =>
        options.Select(option => (object)Item(option.DisplayText, option.Command, option.Value is null && option.Url is not null ? option : option.Value ?? option,
            option.Command is null ? null : option.IsChecked, option.IsEnabled));

    private void BuildButtons()
    {
        var vm = ViewModel;
        var back = BarButton(FASymbol.Back, "后退", vm.GoBackCommand, "GuideBack");
        back.Bind(IsEnabledProperty, new Binding("State.CanGoBack"));
        var reload = BarButton(FASymbol.Refresh, "刷新", vm.ReloadCommand, "GuideReload");
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Children = { back, reload } };
        navigation.Bind(IsVisibleProperty, new Binding(nameof(GuideWindowViewModel.ShowNavigationGroup)));

        var play = BarButton(FASymbol.Play, vm.PlayToolTip, vm.TogglePlayCommand, "GuidePlay");
        vm.State.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GuideWindowState.IsPlaying))
            {
                ((FASymbolIcon)play.Content!).Symbol = vm.State.IsPlaying ? FASymbol.Pause : FASymbol.Play;
            }
        };
        var rate = MenuButton(FASymbol.Clock, vm.RateToolTip, "GuideRate", () => vm.PrepareRateMenuCommand.Execute(null),
            () => Options(vm.RateOptions));
        rate.PointerWheelChanged += (_, e) =>
        {
            e.Handled = true;
            vm.OnRateWheel((int)Math.Round(e.Delta.Y * 120));
        };
        var mute = BarButton(FASymbol.Volume, vm.VolumeToolTip, vm.ToggleMuteCommand, "GuideMute");
        mute.PointerWheelChanged += (_, e) =>
        {
            e.Handled = true;
            vm.OnVolumeWheel((int)Math.Round(e.Delta.Y * 120));
        };
        var subtitle = BarButton(FASymbol.ClosedCaption, vm.SubtitleToolTip, vm.ToggleSubtitleCommand, "GuideSubtitle");
        var immersive = BarButton(FASymbol.FullScreenMaximize, vm.ImmersiveToolTip, vm.ToggleImmersiveCommand, "GuideImmersive");
        var extras = new StackPanel { Orientation = Orientation.Horizontal, Children = { rate, mute, subtitle, immersive } };
        extras.Bind(IsVisibleProperty, new Binding(nameof(GuideWindowViewModel.ShowPlayExtras)));

        var favorite = BarButton(FASymbol.Star, "收藏当前视频", vm.ToggleFavoriteCommand, "GuideFavorite");
        var library = MenuButton(FASymbol.List, "收藏与最近观看", "GuideLibrary", () => vm.PrepareLibraryMenuCommand.Execute(null), () =>
        [
            Sub("收藏", Options(vm.FavoriteOptions)),
            Sub("最近观看", Options(vm.HistoryOptions)),
            new Separator(),
            Item("全部设置…", vm.OpenSettingsCommand),
        ]);
        var libraryGroup = new StackPanel { Orientation = Orientation.Horizontal, Children = { favorite, library } };
        libraryGroup.Bind(IsVisibleProperty, new Binding(nameof(GuideWindowViewModel.ShowLibraryGroup)));

        var interaction = BarButton(FASymbol.Keyboard, vm.InteractionToolTip, vm.ToggleInteractionCommand, "GuideInteraction");
        var clickThrough = BarButton(FASymbol.Target, vm.ClickThroughToolTip, vm.ToggleClickThroughCommand, "GuideClickThrough");
        var windowGroup = new StackPanel { Orientation = Orientation.Horizontal, Children = { interaction, clickThrough } };
        windowGroup.Bind(IsVisibleProperty, new Binding(nameof(GuideWindowViewModel.ShowWindowGroup)));

        var links = MenuButton(FASymbol.Link, "链接", "GuideLinks", () => vm.MenuOpenedCommand.Execute(null), () =>
        [
            Item("粘贴链接并打开", vm.PasteAndOpenCommand),
            Item("复制链接", vm.CopyLinkCommand),
            Item("用浏览器打开", vm.OpenInBrowserCommand),
        ]);

        var settings = MenuButton(FASymbol.Settings, "设置", "GuideSettings", () => vm.PrepareSettingsMenuCommand.Execute(null), () =>
        [
            Item("鼠标穿透", vm.ToggleClickThroughCommand, null, vm.Config.IsClickThrough),
            Item("始终置顶", vm.ToggleTopmostCommand, null, vm.Config.Topmost),
            Item("智能透明（穿透时鼠标经过自动淡化）", vm.ToggleSmartFadeCommand, null, vm.Config.SmartFadeEnabled),
            Sub("位置",
            [
                Item("跟随游戏窗口", vm.SetPositionModeCommand, "FollowGame", vm.IsFollowGameMode),
                Item("自由放置", vm.SetPositionModeCommand, "Free", vm.IsFreeMode),
                new Separator(),
                Item("停靠到右上角", vm.DockCommand, "TopRight"),
                Item("停靠到右下角", vm.DockCommand, "BottomRight"),
                Item("停靠到左上角", vm.DockCommand, "TopLeft"),
                Item("停靠到左下角", vm.DockCommand, "BottomLeft"),
            ]),
            Sub("移到显示器", Options(vm.MonitorOptions)),
            new Separator(),
            Item("字幕浮窗", vm.ToggleSubtitleCommand, null, vm.Config.SubtitleWindowEnabled),
            Item("调整字幕位置…", vm.AdjustSubtitleCommand),
            Item("弹幕（B 站）", vm.ToggleDanmakuCommand, null, vm.State.Danmaku ?? false),
            Sub("网页缩放", Options(vm.ZoomOptions)),
            new Separator(),
            Item("上一集", vm.PrevEpisodeCommand, null, null, vm.State.HasPrevEpisode),
            Item("下一集", vm.NextEpisodeCommand, null, null, vm.State.HasNextEpisode),
            new Separator(),
            Item("全部设置…", vm.OpenSettingsCommand),
        ]);

        var hide = BarButton(FASymbol.Remove, vm.HideToolTip, vm.HideWindowCommand, "GuideHide");
        var close = BarButton(FASymbol.Dismiss, "关闭小窗", vm.CloseWindowCommand, "GuideClose");
        _buttons.Children.AddRange([links, navigation, play, extras, libraryGroup, windowGroup, settings, hide, close]);
    }

    #endregion
}
