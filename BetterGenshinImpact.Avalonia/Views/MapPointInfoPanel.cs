using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.ViewModel;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>
/// 点位信息弹窗（WPF MaskWindow.xaml 中 PointInfoPopup 的 Popup）：标题、隐藏 / 显示、关闭，正在加载文本、错误、链接胶囊（左侧按设置打开，
/// 右侧换一种方式打开）、图片（加载中 / 错误）、说明文字（最高 220，超出截断）。宽 360，底色 #C81F232A、圆角 12、阴影同 WPF。
/// 用基本控件拼出（见 <see cref="MaskButton"/>），每次弹窗模型变化时重建
/// </summary>
public sealed class MapPointInfoPanel : Border
{
    private static IBrush B(string c) => new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse(c));
    private static readonly IBrush Secondary = B("#B3FFFFFF");

    public MapPointInfoPanel()
    {
        Width = 360;
        Background = B("#C81F232A");
        BorderBrush = B("#26FFFFFF");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(12);
        BoxShadow = BoxShadows.Parse("0 0 28 0 #B3000000");
        IsVisible = false;
    }

    /// <summary>隐藏 / 显示按钮、关闭按钮、链接按钮（用于验证）</summary>
    public MaskButton? HideButton { get; private set; }
    public MaskButton? CloseButton { get; private set; }
    public IReadOnlyList<MaskButton> LinkButtons { get; private set; } = [];
    public string TitleText { get; private set; } = "";
    public string BodyText { get; private set; } = "";
    public bool ShowsImage { get; private set; }

    public void Rebuild(MaskMapPointInfoPopupViewModel vm)
    {
        IsVisible = vm.IsOpen;
        if (!vm.IsOpen)
        {
            Child = null;
            return;
        }

        var root = new StackPanel { Margin = new Thickness(12) };
        // 标题行
        var head = new DockPanel();
        CloseButton = MaskButton.Text("✕", 13);
        CloseButton.Width = CloseButton.Height = 28;
        CloseButton.CornerRadius = new CornerRadius(4);
        CloseButton.HoverBackground = B("#1FFFFFFF");
        CloseButton.Margin = new Thickness(8, -2, -2, 0);
        CloseButton.Click = () => vm.CloseCommand.Execute(null);
        DockPanel.SetDock(CloseButton, Dock.Right);
        var hidden = vm.IsCurrentPointHidden;
        HideButton = new MaskButton
        {
            MinWidth = 58, Height = 28, Padding = new Thickness(8, 0), Margin = new Thickness(8, -2, 0, 0), CornerRadius = new CornerRadius(4),
            Background = B("#2FFFFFFF"), BorderBrush = B("#26FFFFFF"), BorderThickness = new Thickness(1), HoverBackground = B("#45FFFFFF"),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = FluentSymbols.Glyph(hidden ? "Eye24" : "EyeOff24"), FontFamily = FluentSymbols.Font, FontSize = 14, Foreground = Brushes.White, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = hidden ? "显示" : "隐藏", FontSize = 12, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center },
                },
            },
            Click = () => vm.ToggleHiddenCommand.Execute(null),
        };
        DockPanel.SetDock(HideButton, Dock.Right);
        TitleText = vm.Title;
        head.Children.Add(CloseButton);
        head.Children.Add(HideButton);
        head.Children.Add(new TextBlock { Text = vm.Title, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(head);

        var body = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        if (vm.IsTextLoading)
        {
            body.Children.Add(new TextBlock { Text = "正在加载文本...", Foreground = Secondary });
        }

        if (!string.IsNullOrEmpty(vm.TextError))
        {
            body.Children.Add(new TextBlock { Text = vm.TextError, Foreground = Secondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        }

        var links = new List<MaskButton>();
        if (vm.HasUrlList)
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var link in vm.UrlList)
            {
                var chip = new StackPanel { Orientation = Orientation.Horizontal };
                var open = new MaskButton
                {
                    Padding = new Thickness(10, 4), CornerRadius = new CornerRadius(10), Background = Brushes.Transparent, HoverBackground = B("#1FFFFFFF"),
                    Child = new TextBlock { Text = link.DisplayText, FontSize = 12, Foreground = Brushes.White },
                    Click = () => vm.OpenUrlCommand.Execute(link.Url),
                };
                links.Add(open);
                chip.Children.Add(open);
                if (vm.IsGuideWindowAvailable)
                {
                    chip.Children.Add(new Border { Width = 1, Margin = new Thickness(0, 5), Background = B("#26FFFFFF") });
                    var alt = new MaskButton
                    {
                        Padding = new Thickness(7, 4), CornerRadius = new CornerRadius(10), Background = Brushes.Transparent, HoverBackground = B("#1FFFFFFF"),
                        Child = new TextBlock { Text = FluentSymbols.Glyph(vm.IsGuideWindowPreferred ? "Open24" : "PictureInPicture24"), FontFamily = FluentSymbols.Font, FontSize = 14, Foreground = Brushes.White },
                        Click = () => vm.OpenUrlAlternateCommand.Execute(link.Url),
                    };
                    chip.Children.Add(alt);
                }

                wrap.Children.Add(new Border
                {
                    Margin = new Thickness(0, 0, 8, 8), Background = B("#2FFFFFFF"), BorderBrush = B("#26FFFFFF"), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10), Child = chip,
                });
            }

            body.Children.Add(wrap);
        }

        LinkButtons = links;
        ShowsImage = false;
        if (vm.HasImage)
        {
            var imageGrid = new Grid();
            if (vm.Image is IImage image)
            {
                ShowsImage = true;
                imageGrid.Children.Add(new Image { Source = image, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Stretch });
            }

            if (vm.IsImageLoading)
            {
                imageGrid.Children.Add(new TextBlock { Text = "图片加载中...", Foreground = Secondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 12) });
            }

            if (!string.IsNullOrEmpty(vm.ImageError))
            {
                imageGrid.Children.Add(new TextBlock { Text = vm.ImageError, Foreground = Secondary, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 12) });
            }

            body.Children.Add(new Border { Margin = new Thickness(0, 10, 0, 0), CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = imageGrid });
        }

        BodyText = !vm.IsTextLoading && string.IsNullOrEmpty(vm.TextError) ? vm.Text : "";
        if (BodyText.Length > 0)
        {
            body.Children.Add(new Border
            {
                Margin = new Thickness(0, 8, 0, 0), MaxHeight = 220, ClipToBounds = true,
                Child = new TextBlock { Text = vm.Text, Foreground = B("#E6FFFFFF"), TextWrapping = TextWrapping.Wrap },
            });
        }

        root.Children.Add(body);
        Child = root;
    }
}
