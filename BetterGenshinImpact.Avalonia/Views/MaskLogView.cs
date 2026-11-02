using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using BetterGenshinImpact.Avalonia.Services;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>
/// 遮罩日志框的文字（对应 WPF 遮罩的日志 RichTextBox）：逐条着色、按宽度换行、固定行距（首选字体的行距，中文回退字形不撑高行，同 WPF），
/// 内容超出时最新一行贴底、较早的行从顶部裁掉（同 RichTextBox.ScrollToEnd），不足一框时从顶部 5 DIP 开始
/// </summary>
public sealed class MaskLogView : Control
{
    private IReadOnlyList<IReadOnlyList<MaskLogSegment>> _lines = [];
    private FontFamily _family = FontFamily.Default;
    private double _fontSize = 12;
    private IBrush _foreground = Brushes.LightGray;
    private readonly List<TextLayout> _layouts = [];
    private double _layoutWidth = -1;

    public MaskLogView()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    /// <summary>内层背景（WPF 日志 RichTextBox 自身的 Background）</summary>
    public IBrush? Background { get; set; }

    /// <summary>文字不透明度（MaskWindowConfig.TextOpacity，只作用于文字）</summary>
    public double TextOpacity { get; set; } = 1;

    /// <summary>当前能看到的整行数（用于验证）</summary>
    public int VisibleLines { get; private set; }

    /// <summary>换行后的总行数（用于验证）</summary>
    public int TotalLines { get; private set; }

    /// <summary>行距（DIP）</summary>
    public double Pitch { get; private set; }

    public void SetContent(IReadOnlyList<IReadOnlyList<MaskLogSegment>> lines, FontFamily family, double fontSize, IBrush foreground)
    {
        _lines = lines;
        _family = family;
        _fontSize = fontSize;
        _foreground = foreground;
        _layoutWidth = -1;
        InvalidateVisual();
    }

    private void BuildLayouts(double width)
    {
        foreach (var layout in _layouts) layout.Dispose();
        _layouts.Clear();
        var typeface = new Typeface(_family);
        foreach (var line in _lines)
        {
            var text = string.Concat(line.Select(s => s.Text));
            if (text.Length == 0) text = " ";
            var spans = new List<ValueSpan<TextRunProperties>>();
            var start = 0;
            foreach (var segment in line)
            {
                if (segment.Text.Length == 0) continue;
                var brush = new ImmutableSolidColorBrush(MaskWindow.ParseColor(segment.Color, Colors.White));
                IBrush? back = segment.Background is { } b ? new ImmutableSolidColorBrush(MaskWindow.ParseColor(b, Colors.Transparent)) : null;
                spans.Add(new ValueSpan<TextRunProperties>(start, segment.Text.Length,
                    new GenericTextRunProperties(typeface, _fontSize, null, brush, back)));
                start += segment.Text.Length;
            }

            _layouts.Add(new TextLayout(text, typeface, _fontSize, _foreground, TextAlignment.Left, TextWrapping.Wrap,
                maxWidth: Math.Max(1, width), textStyleOverrides: spans));
        }

        _layoutWidth = width;
    }

    public override void Render(DrawingContext context)
    {
        // WPF RichTextBox：上内边距 5，文档左右内边距 5
        if (Background is { } bg) context.FillRectangle(bg, new Rect(Bounds.Size));
        var width = Bounds.Width - 10;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0) return;
        if (Math.Abs(width - _layoutWidth) > 0.5) BuildLayouts(width);

        var spacing = MaskWindow.PrimaryLineSpacing(_family);
        Pitch = spacing * _fontSize;
        var ascent = PrimaryAscent(_family) * _fontSize;
        using var _ = context.PushOpacity(Math.Clamp(TextOpacity, 0, 1));
        var all = _layouts.SelectMany(l => l.TextLines).ToList();
        TotalLines = all.Count;
        var total = all.Count * Pitch;
        var top = Math.Min(5, height - total);
        VisibleLines = 0;
        for (var i = 0; i < all.Count; i++)
        {
            var y = top + i * Pitch;
            if (y + Pitch < 0) continue;
            if (y > height) break;
            if (y >= 0 && y + Pitch <= height + 0.5) VisibleLines++;
            var line = all[i];
            // 基线对齐到首选字体的上升高度，各行行距一致
            line.Draw(context, new Point(5, y + ascent - line.Baseline));
        }
    }

    /// <summary>首选字体的上升高度（em 的倍数）</summary>
    private static double PrimaryAscent(FontFamily family)
    {
        var first = family.FamilyNames.FirstOrDefault();
        if (first is not null && FontManager.Current.TryGetGlyphTypeface(new Typeface(first), out var glyph) && glyph.Metrics.DesignEmHeight > 0)
        {
            return -glyph.Metrics.Ascent / (double)glyph.Metrics.DesignEmHeight;
        }

        return 0.95;
    }
}
