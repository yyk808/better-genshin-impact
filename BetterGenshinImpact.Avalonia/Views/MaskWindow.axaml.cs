using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>遮罩状态栏的一项（对应 WPF MaskWindowViewModel.StatusList：图标 + 名称，按开关着色）</summary>
public sealed record MaskStatusItem(string Text, bool IsEnabled);

/// <summary>遮罩窗口：透明、置顶、不抢焦点；点击穿透由 <see cref="Services.MaskWindowController"/> 用 X11 SHAPE 设置</summary>
public partial class MaskWindow : Window
{
    /// <summary>WPF 遮罩识别文字用的 TextThemeFontFamily（MiSans）</summary>
    public static FontFamily TextFont { get; } = new("avares://bgi-avalonia/Assets/Fonts/MiSans-Regular.ttf#MiSans");

    private long _logVersion = -1;
    private string _logStyleKey = "";
    private string _statusKey = "";

    public MaskWindow()
    {
        InitializeComponent();
    }

    public MaskDrawingLayer DrawingLayer => Drawing;

    /// <summary>日志框（用于验证）</summary>
    public MaskLogView LogLines => LogView;

    /// <summary>状态栏、日志框的屏幕区域（窗口 DIP，用于验证）</summary>
    public Rect StatusBounds => new(Canvas.GetLeft(StatusBox), Canvas.GetTop(StatusBox), StatusBox.Width, StatusBox.Height);
    public Rect LogBounds => new(Canvas.GetLeft(LogBox), Canvas.GetTop(LogBox), LogBox.Width, LogBox.Height);

    /// <summary>
    /// 按 MaskWindowConfig 摆放状态栏和日志框（与 WPF MaskWindow.xaml 相同）：
    /// 位置与大小 = 比例 × 窗口大小（OverlayRelativeOrAbsoluteConverter）；字号按 ComputeEffectiveSize（OverlayScaledSizeConverter）；
    /// 面板背景 / 边框 / 阴影、文字颜色与透明度都取自配置
    /// </summary>
    /// <param name="width">遮罩宽度（DIP）</param>
    /// <param name="height">遮罩高度（DIP）</param>
    /// <param name="scaleTo1080P">游戏画面相对 1080P 的比例（宽高比例取小）</param>
    /// <param name="dpiScale">显示缩放（物理像素 / DIP）</param>
    public void ApplyOverlay(MaskWindowConfig c, double width, double height, double scaleTo1080P, double dpiScale,
        IReadOnlyList<MaskStatusItem> status, IReadOnlyList<IReadOnlyList<MaskLogSegment>>? log, long logVersion)
    {
        // 状态栏
        StatusBox.IsVisible = c.ShowStatus;
        PlaceUnlessDragging(StatusBox, c.StatusListLeftRatio, c.StatusListTopRatio, c.StatusListWidthRatio, c.StatusListHeightRatio, width, height);
        StatusBox.Background = Brush(c.StatusPanelBackgroundColor, Colors.Transparent);
        StatusBox.BorderBrush = Brush(c.StatusPanelBorderColor, Color.Parse("#33000000"));
        StatusBox.BorderThickness = new Thickness(Math.Max(0, c.StatusPanelBorderThickness));
        StatusBox.Effect = Shadow(c.StatusShadowEnabled, c.StatusShadowOpacity, c.StatusShadowBlurRadius, c.StatusShadowColor, Colors.LightGray);
        var statusSize = MaskWindowConfig.ComputeEffectiveSize(c.StatusFontSize, c.LogFontScale, scaleTo1080P, dpiScale, c.OverlayScalingEnabled);
        var statusKey = $"{statusSize}|{c.TextOpacity}|{c.StatusEnabledTextColor}|{c.StatusDisabledTextColor}|" + string.Join(",", status.Select(s => s.Text + s.IsEnabled));
        if (statusKey != _statusKey)
        {
            _statusKey = statusKey;
            StatusPanel.Children.Clear();
            foreach (var item in status)
            {
                StatusPanel.Children.Add(new TextBlock
                {
                    Text = item.Text,
                    FontFamily = FgiSymbols.Font,
                    FontSize = statusSize,
                    Opacity = c.TextOpacity,
                    // WPF：TextBlock Margin 0,0,8,0，外层 ListViewItem 另有左右内边距
                    Margin = new Thickness(0, 0, 8, 0),
                    Padding = new Thickness(4, 0),
                    Foreground = item.IsEnabled ? Brush(c.StatusEnabledTextColor, Colors.LightGreen) : Brush(c.StatusDisabledTextColor, Colors.LightGray),
                });
            }
        }

        // 日志框
        LogBox.IsVisible = c.ShowLogBox && log is not null;
        PlaceUnlessDragging(LogBox, c.LogTextBoxLeftRatio, c.LogTextBoxTopRatio, c.LogTextBoxWidthRatio, c.LogTextBoxHeightRatio, width, height);
        LogBox.Background = Brush(c.LogPanelBackgroundColor, Colors.Transparent);
        LogBox.BorderBrush = Brush(c.LogPanelBorderColor, Color.Parse("#33000000"));
        LogBox.BorderThickness = new Thickness(Math.Max(0, c.LogPanelBorderThickness));
        LogBox.Effect = Shadow(c.LogShadowEnabled, c.LogShadowOpacity, c.LogShadowBlurRadius, c.LogShadowColor, Colors.Black);
        ApplyEditState();
        if (!LogBox.IsVisible || log is null)
        {
            return;
        }

        var logSize = MaskWindowConfig.ComputeEffectiveSize(c.LogFontSize, c.LogFontScale, scaleTo1080P, dpiScale, c.OverlayScalingEnabled);
        var styleKey = $"{logSize}|{c.LogFontFamily}|{c.LogTextColor}|{c.TextOpacity}|{c.LogPanelBackgroundColor}|{LogBox.Width}|{LogBox.Height}";
        if (logVersion == _logVersion && styleKey == _logStyleKey)
        {
            return;
        }

        _logVersion = logVersion;
        _logStyleKey = styleKey;
        // WPF：日志框外层与内部 RichTextBox 都用 LogPanelBackgroundColor 作背景（半透明时叠两层）
        LogView.Background = Brush(c.LogPanelBackgroundColor, Colors.Transparent);
        LogView.TextOpacity = c.TextOpacity;
        LogView.SetContent(log, LogFontFamily(c.LogFontFamily), logSize, Brush(c.LogTextColor, Colors.LightGray));
        LogView.InvalidateVisual();
    }

    /// <summary>小地图 / 大地图点位层（用于验证与输入区域）</summary>
    public MapPointsLayer MiniMapLayer => MiniMapPoints;
    public MapPointsLayer BigMapLayer => BigMapPoints;
    public bool MapLoadingVisible => MapLoadingBanner.IsVisible;

    /// <summary>
    /// 地图点位（同 WPF MaskWindow.xaml 的绑定）：小地图层在 MiniMapMaskEnabled 时显示，位置为 MapAssets 小地图区域按比例换算
    /// （左 = 比例 × 宽，上与边长 = 比例 × 高）；大地图层在大地图界面时显示并铺满遮罩；加载点位时顶部居中显示提示
    /// </summary>
    public void ApplyMapPoints(BetterGenshinImpact.ViewModel.MaskWindowViewModel? map, double width, double height)
    {
        if (map is null)
        {
            MiniMapPoints.IsVisible = BigMapPoints.IsVisible = MapLoadingBanner.IsVisible = false;
            return;
        }

        MiniMapPoints.IsVisible = map.Config.MapMaskConfig.MiniMapMaskEnabled;
        Canvas.SetLeft(MiniMapPoints, map.MiniMapOverlayLeftRatio * width);
        Canvas.SetTop(MiniMapPoints, map.MiniMapOverlayTopRatio * height);
        MiniMapPoints.Width = MiniMapPoints.Height = map.MiniMapOverlaySizeRatio * height;
        MiniMapPoints.Update(map.MapPoints, map.MapPointLabels, map.MiniMapViewport);

        BigMapPoints.IsVisible = map.IsInBigMapUi;
        BigMapPoints.Update(map.MapPoints, map.MapPointLabels, map.BigMapViewport);

        MapLoadingBanner.IsVisible = map.IsMapPointsLoading;
        MapLoadingText.Text = map.MapPointsLoadingText;
    }

    private static void Place(Control control, double left, double top, double w, double h, double width, double height)
    {
        Canvas.SetLeft(control, left * width);
        Canvas.SetTop(control, top * height);
        control.Width = Math.Max(control.MinWidth, w * width);
        control.Height = Math.Max(control.MinHeight, h * height);
    }

    /// <summary>WPF DropShadowEffect（ShadowDepth 0）：关闭阴影时不透明度为 0</summary>
    private static DropShadowEffect? Shadow(bool enabled, double opacity, double blur, string? color, Color fallback) =>
        enabled && opacity > 0
            ? new DropShadowEffect { OffsetX = 0, OffsetY = 0, BlurRadius = Math.Max(0, blur), Color = ParseColor(color, fallback), Opacity = Math.Clamp(opacity, 0, 1) }
            : null;

    /// <summary>
    /// WPF 的字体回退链（如“Cascadia Mono, Consolas, Courier New, monospace, /Resources/Fonts/MiSans-Regular.ttf#MiSans”）：
    /// 去掉 WPF 包内路径，其余按名字交给 Avalonia（没装的字体跳过，中文按系统字体回退）
    /// </summary>
    public static FontFamily LogFontFamily(string? families)
    {
        var installed = FontManager.Current.SystemFonts.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>();
        foreach (var name in (families ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (name.StartsWith('/') || name.Contains('#')) continue;
            // 通用名 monospace：Avalonia 不按 fontconfig 解析，换成常见的已安装等宽字体
            if (name.Equals("monospace", StringComparison.OrdinalIgnoreCase))
                names.AddRange(MonospaceCandidates.Where(installed.Contains));
            else if (installed.Contains(name))
                names.Add(name);
        }

        if (FontInfo.CjkFamily is { } cjk) names.Add(cjk);
        return names.Count == 0 ? FontFamily.Default : new FontFamily(string.Join(", ", names.Distinct()));
    }

    /// <summary>字体族里第一个字体的行距（em 的倍数），取不到时 1.2</summary>
    public static double PrimaryLineSpacing(FontFamily family)
    {
        var first = family.FamilyNames.FirstOrDefault();
        if (first is not null && FontManager.Current.TryGetGlyphTypeface(new Typeface(first), out var glyph) && glyph.Metrics.DesignEmHeight > 0)
        {
            var m = glyph.Metrics;
            var spacing = (-m.Ascent + m.Descent + m.LineGap) / (double)m.DesignEmHeight;
            if (spacing is > 0.8 and < 2) return spacing;
        }

        return 1.2;
    }

    private static readonly string[] MonospaceCandidates =
        ["DejaVu Sans Mono", "Noto Sans Mono", "Liberation Mono", "Ubuntu Mono", "Cascadia Mono", "JetBrains Mono", "Source Code Pro", "Hack", "Inconsolata"];

    private static readonly Dictionary<string, IBrush> Brushes = new();

    private static IBrush Brush(string? text, Color fallback)
    {
        var key = (text ?? "") + "|" + fallback;
        if (!Brushes.TryGetValue(key, out var brush))
        {
            if (Brushes.Count > 128) Brushes.Clear();
            brush = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(ParseColor(text, fallback));
            Brushes[key] = brush;
        }

        return brush;
    }

    /// <summary>WPF 颜色字符串（#AARRGGBB / #RRGGBB / 颜色名），无法解析时用默认值（同 WPF OverlayStyleHelper.ParseColorOrDefault）</summary>
    public static Color ParseColor(string? text, Color fallback) =>
        !string.IsNullOrWhiteSpace(text) && Color.TryParse(text.Trim(), out var color) ? color : fallback;
}
