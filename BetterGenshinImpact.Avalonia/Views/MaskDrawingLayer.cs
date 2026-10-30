using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Core.Mask;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>
/// 遮罩绘制层（对应 WPF 的 MaskWindowDrawingLayer）：渲染 <see cref="IMaskWindowDrawingBoard"/> 的快照（识别框、线、文字）。
/// 快照坐标是游戏捕获区域的物理像素，按 本层宽度 / 捕获宽度 换算成 DIP。
/// </summary>
public sealed class MaskDrawingLayer : Control
{
    private const double BadgePaddingXRatio = 6d / 26d;
    private const double BadgePaddingYRatio = 2d / 26d;
    private const double BadgeRadiusRatio = 5d / 26d;

    private readonly Dictionary<(MaskColor, double), IPen> _pens = new();
    private readonly Dictionary<MaskColor, IBrush> _brushes = new();

    public MaskDrawingLayer()
    {
        IsHitTestVisible = false;
    }

    public MaskWindowDrawingSnapshot Snapshot { get; private set; } = MaskWindowDrawingSnapshot.Empty;

    public MaskWindowDrawingStyle DrawingStyle { get; private set; } = MaskWindowDrawingStyle.Default;

    /// <summary>对应配置“在遮罩上显示识别结果”，只影响 Recognition 分组</summary>
    public bool ShowRecognition { get; private set; } = true;

    /// <summary>捕获区域大小（物理像素）</summary>
    public PixelSize CaptureSize { get; private set; }

    /// <summary>最近一次绘制的图形数（用于验证）</summary>
    public int LastShapeCount { get; private set; }

    public void Update(MaskWindowDrawingSnapshot snapshot, MaskWindowDrawingStyle style, bool showRecognition, PixelSize captureSize)
    {
        if (ReferenceEquals(snapshot, Snapshot) && style == DrawingStyle && showRecognition == ShowRecognition && captureSize == CaptureSize)
        {
            return;
        }

        Snapshot = snapshot;
        DrawingStyle = style;
        ShowRecognition = showRecognition;
        CaptureSize = captureSize;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var count = 0;
        var snapshot = Snapshot;
        if (snapshot.IsEmpty || CaptureSize.Width <= 0 || CaptureSize.Height <= 0)
        {
            LastShapeCount = 0;
            return;
        }

        var sx = Bounds.Width / CaptureSize.Width;
        var sy = Bounds.Height / CaptureSize.Height;
        var style = DrawingStyle;
        foreach (var entry in snapshot.Entries)
        {
            var isRecognition = entry.Group.Kind == MaskWindowDrawingKind.Recognition;
            if (isRecognition && !ShowRecognition)
            {
                continue;
            }

            foreach (var shape in entry.Shapes)
            {
                switch (shape)
                {
                    case MaskWindowDrawingRect rect:
                    {
                        var b = rect.Bounds;
                        var pen = GetPen(ResolveStroke(rect.Stroke, style.RectStroke, isRecognition, style));
                        context.DrawRectangle(null, pen, new Rect(b.X * sx, b.Y * sy, b.Width * sx, b.Height * sy));
                        count++;
                        break;
                    }
                    case MaskWindowDrawingLine line:
                    {
                        var pen = GetPen(ResolveStroke(line.Stroke, style.LineStroke, isRecognition, style));
                        context.DrawLine(pen, new Point(line.From.X * sx, line.From.Y * sy), new Point(line.To.X * sx, line.To.Y * sy));
                        count++;
                        break;
                    }
                    case MaskWindowDrawingText text when !string.IsNullOrEmpty(text.Text):
                        DrawText(context, text, style, sx, sy);
                        count++;
                        break;
                }
            }
        }

        LastShapeCount = count;
    }

    private void DrawText(DrawingContext context, MaskWindowDrawingText text, MaskWindowDrawingStyle style, double sx, double sy)
    {
        var textStyle = text.Style;
        var fontSize = Math.Max(1, textStyle?.FontSize ?? style.TextFontSize) * sy;
        var foreground = GetBrush(textStyle?.Foreground ?? style.TextColor);
        var origin = new Point(text.Origin.X * sx, text.Origin.Y * sy);
        var formatted = new FormattedText(text.Text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
            new Typeface(FontInfo.UiFontFamily), fontSize, foreground);
        if (textStyle?.Background is { } background)
        {
            var px = fontSize * BadgePaddingXRatio;
            var py = fontSize * BadgePaddingYRatio;
            var radius = fontSize * BadgeRadiusRatio;
            context.DrawRectangle(GetBrush(background), null,
                new Rect(origin.X - px, origin.Y - py, formatted.Width + px * 2, formatted.Height + py * 2), radius, radius);
        }

        context.DrawText(formatted, origin);
    }

    private static MaskWindowDrawingStroke ResolveStroke(MaskWindowDrawingStroke? own, MaskWindowDrawingStroke unified, bool isRecognition,
        MaskWindowDrawingStyle style) =>
        isRecognition && style.OverrideRecognitionStroke ? unified : own ?? MaskWindowDrawingStyle.DefaultStroke;

    private IPen GetPen(MaskWindowDrawingStroke stroke)
    {
        var key = (stroke.Color, stroke.Thickness);
        if (!_pens.TryGetValue(key, out var pen))
        {
            if (_pens.Count > 256)
            {
                _pens.Clear();
            }

            pen = new ImmutablePen(new ImmutableSolidColorBrush(ToColor(stroke.Color)), Math.Max(0, stroke.Thickness));
            _pens[key] = pen;
        }

        return pen;
    }

    private IBrush GetBrush(MaskColor color)
    {
        if (!_brushes.TryGetValue(color, out var brush))
        {
            if (_brushes.Count > 256)
            {
                _brushes.Clear();
            }

            brush = new ImmutableSolidColorBrush(ToColor(color));
            _brushes[color] = brush;
        }

        return brush;
    }

    private static Color ToColor(MaskColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);
}
