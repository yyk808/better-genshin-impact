using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Platform.Linux.X11;
using BetterGenshinImpact.Service.Guide;
using BetterGenshinImpact.ViewModel.Windows;
using Serilog;

namespace BetterGenshinImpact.Avalonia.Views.Guide;

/// <summary>
/// 字幕浮窗（对应 WPF 的 GuideSubtitleWindow）：透明、置顶、不抢焦点，平时鼠标穿透；
/// 位置按游戏画面比例计算（与 Windows 相同的 SubtitleCenterXRatio / SubtitleBottomRatio / SubtitleWidthRatio），
/// 调整模式下可拖动位置、拖右侧边缘调整宽度、Ctrl+滚轮调整字号。透明背景需要桌面合成器
/// </summary>
public sealed class GuideSubtitleWindow : Window, IGuideSubtitleView
{
    private readonly Grid _root;
    private readonly string? _display = Environment.GetEnvironmentVariable("DISPLAY");
    private System.Drawing.Rectangle _anchor;
    private bool _allowClose;
    private bool? _clickThrough;
    private Point? _widthDragStart;
    private double _widthDragStartWidth;

    public GuideSubtitleWindow(GuideSubtitleWindowViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        Title = "字幕浮窗";
        WindowDecorations = WindowDecorations.None;
        ShowActivated = false;
        ShowInTaskbar = false;
        CanResize = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        var finish = new Button { Content = "完成调整", Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(12, 3), HorizontalAlignment = HorizontalAlignment.Center, Command = viewModel.FinishAdjustCommand };
        finish.Classes.Add("accent");
        finish.Bind(IsVisibleProperty, new Binding(nameof(GuideSubtitleWindowViewModel.IsAdjusting)));

        var text = new TextBlock { Foreground = Brushes.White, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.Wrap, Name = "SubtitleText" };
        text.Bind(TextBlock.TextProperty, new Binding(nameof(GuideSubtitleWindowViewModel.DisplayText)));
        text.Bind(TextBlock.FontSizeProperty, new Binding("Config.SubtitleFontSize"));
        text.Effect = new DropShadowEffect { BlurRadius = 5, OffsetX = 0, OffsetY = 0, Color = Colors.Black, Opacity = 1 };
        var background = new SolidColorBrush(Colors.Black);
        // 画刷不在可视树中，没有 DataContext：直接以配置为绑定源
        background.Bind(Brush.OpacityProperty, new Binding(nameof(GuideWindowConfig.SubtitleBackgroundOpacity)) { Source = viewModel.Config });
        var box = new Border { Padding = new Thickness(16, 6), CornerRadius = new CornerRadius(8), Background = background, Child = text };
        Grid.SetRow(box, 1);

        var outline = new Rectangle { RadiusX = 8, RadiusY = 8, Fill = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), Stroke = new SolidColorBrush(Color.FromRgb(0x3A, 0x96, 0xDD)), StrokeDashArray = [4, 3], StrokeThickness = 2, Cursor = new Cursor(StandardCursorType.SizeAll) };
        outline.Bind(IsVisibleProperty, new Binding(nameof(GuideSubtitleWindowViewModel.IsAdjusting)));
        Grid.SetRow(outline, 1);

        var widthGrip = new Border { Width = 8, HorizontalAlignment = HorizontalAlignment.Right, Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), Cursor = new Cursor(StandardCursorType.SizeWestEast) };
        widthGrip.Bind(IsVisibleProperty, new Binding(nameof(GuideSubtitleWindowViewModel.IsAdjusting)));
        Grid.SetRow(widthGrip, 1);
        widthGrip.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            _widthDragStart = e.GetPosition(this);
            _widthDragStartWidth = _root!.Bounds.Width;
            e.Pointer.Capture(widthGrip);
        };
        widthGrip.PointerMoved += (_, e) =>
        {
            if (_widthDragStart is { } start)
            {
                ViewModel.MaxTextWidth = Math.Max(200, _widthDragStartWidth + e.GetPosition(this).X - start.X);
            }
        };
        widthGrip.PointerReleased += (_, e) =>
        {
            if (_widthDragStart is null)
            {
                return;
            }

            _widthDragStart = null;
            e.Pointer.Capture(null);
            if (_anchor.Width > 0)
            {
                ViewModel.Config.SubtitleWidthRatio = Math.Clamp(ViewModel.MaxTextWidth * Scale / _anchor.Width, 0.2, 1);
            }

            SavePosition();
        };

        _root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto"), Children = { finish, box, outline, widthGrip } };
        _root.Bind(MaxWidthProperty, new Binding(nameof(GuideSubtitleWindowViewModel.MaxTextWidth)));
        _root.PointerPressed += OnRootPressed;
        _root.PointerWheelChanged += OnRootWheel;
        Content = _root;

        SizeChanged += (_, _) => Reposition();
        Opened += (_, _) => ApplyClickThrough();
        viewModel.Config.PropertyChanged += OnConfigChanged;
        viewModel.PropertyChanged += OnViewModelChanged;
        Closing += (_, e) =>
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                HideWindow();
            }
        };
        Closed += (_, _) =>
        {
            ViewModel.Config.PropertyChanged -= OnConfigChanged;
            ViewModel.PropertyChanged -= OnViewModelChanged;
            ViewModel.Detach();
        };
    }

    public GuideSubtitleWindowViewModel ViewModel { get; }

    /// <summary>当前参照区域（自测用）</summary>
    public System.Drawing.Rectangle Anchor => _anchor;

    private double Scale => DesktopScaling > 0 ? DesktopScaling : 1;

    private nint Handle => TryGetPlatformHandle()?.Handle ?? 0;

    #region IGuideSubtitleView

    public bool IsShown => IsVisible;

    public void ShowWithoutActivation()
    {
        if (!IsVisible)
        {
            Show();
            Reposition();
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

    public void UpdateAnchor(System.Drawing.Rectangle anchor)
    {
        if (anchor.Width <= 0 || anchor.Height <= 0)
        {
            return;
        }

        _anchor = anchor;
        var maxWidthPx = anchor.Width * Math.Clamp(ViewModel.Config.SubtitleWidthRatio, 0.2, 1);
        ViewModel.MaxTextWidth = Math.Max(200, maxWidthPx / Scale);
        Reposition();
    }

    public void BringToTopmost()
    {
        if (IsVisible)
        {
            Topmost = false;
            Topmost = true;
        }
    }

    #endregion

    /// <summary>窗口当前的屏幕区域（物理像素）</summary>
    private System.Drawing.Rectangle WindowRect()
    {
        var size = ClientSize;
        return new System.Drawing.Rectangle(Position.X, Position.Y, (int)Math.Round(size.Width * Scale), (int)Math.Round(size.Height * Scale));
    }

    private void Reposition()
    {
        var rect = WindowRect();
        if (_anchor.Width <= 0 || rect.Width <= 0)
        {
            return;
        }

        var config = ViewModel.Config;
        var centerX = _anchor.Left + _anchor.Width * Math.Clamp(config.SubtitleCenterXRatio, 0, 1);
        var bottom = _anchor.Bottom - _anchor.Height * Math.Clamp(config.SubtitleBottomRatio, 0, 1);
        var left = (int)Math.Round(centerX - rect.Width / 2d);
        var top = (int)Math.Round(bottom - rect.Height);
        left = Math.Clamp(left, _anchor.Left, Math.Max(_anchor.Left, _anchor.Right - rect.Width));
        top = Math.Clamp(top, _anchor.Top, Math.Max(_anchor.Top, _anchor.Bottom - rect.Height));
        if (left != rect.Left || top != rect.Top)
        {
            Position = new PixelPoint(left, top);
        }
    }

    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GuideWindowConfig.SubtitleWidthRatio))
        {
            UpdateAnchor(_anchor);
        }
        else if (e.PropertyName is nameof(GuideWindowConfig.SubtitleCenterXRatio) or nameof(GuideWindowConfig.SubtitleBottomRatio))
        {
            Reposition();
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GuideSubtitleWindowViewModel.IsClickThrough))
        {
            ApplyClickThrough();
        }
    }

    /// <summary>平时鼠标穿透（X SHAPE 输入区域为空），调整模式下可点击</summary>
    private void ApplyClickThrough()
    {
        var enabled = ViewModel.IsClickThrough;
        var handle = Handle;
        if (handle == 0 || _clickThrough == enabled)
        {
            return;
        }

        _clickThrough = enabled;
        if (X11Overlay.SetClickThrough(_display, handle, enabled) is { } error)
        {
            Log.Warning("字幕浮窗无法设置鼠标穿透：{Error}", error);
        }
    }

    private void OnRootPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!ViewModel.IsAdjusting || e.Handled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Handled = true;
        BeginMoveDrag(e);
        SavePosition();
    }

    private void OnRootWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!ViewModel.IsAdjusting || (e.KeyModifiers & KeyModifiers.Control) == 0)
        {
            return;
        }

        e.Handled = true;
        var config = ViewModel.Config;
        config.SubtitleFontSize = Math.Clamp(config.SubtitleFontSize + Math.Sign(e.Delta.Y) * 2, 12, 48);
    }

    private void SavePosition()
    {
        var rect = WindowRect();
        if (_anchor.Width <= 0 || _anchor.Height <= 0 || rect.Width <= 0)
        {
            return;
        }

        var config = ViewModel.Config;
        config.SubtitleCenterXRatio = Math.Clamp(((rect.Left + rect.Right) / 2d - _anchor.Left) / _anchor.Width, 0, 1);
        config.SubtitleBottomRatio = Math.Clamp((_anchor.Bottom - rect.Bottom) / (double)_anchor.Height, 0, 1);
    }
}
