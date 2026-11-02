using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.Model.MaskMap;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>
/// 遮罩上的地图点位（WPF View/Controls/PointsCanvas 与 MiniMapPointsCanvas 的画法）：
/// 每个点位 = 右下偏移 2 的淡阴影 + #323947 底、#D3BC8E 2 像素边的圆 + 圆形裁剪的标签图标（图标未加载时画标签颜色的圆），隐藏的点位 35% 不透明度。
/// 大地图：点位 32×32，可视区域（地图图片坐标）铺满整个控件；小地图：圆形裁剪（取宽高中的小值为直径），点位大小 = 直径 / 12（8 到 16 之间）
/// </summary>
public sealed class MapPointsLayer : Control
{
    private static readonly IBrush FillBrush = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#323947"));
    private static readonly IPen BorderPen = new global::Avalonia.Media.Immutable.ImmutablePen(
        new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromRgb(0xD3, 0xBC, 0x8E)), 2);
    private static readonly IBrush ShadowBrush = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb(30, 0, 0, 0));

    private static readonly IBrush HiddenMask = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb((byte)Math.Round(0.35 * 255), 0, 0, 0));

    private readonly Dictionary<string, IBrush> _colorBrushCache = new(StringComparer.Ordinal);
    private IReadOnlyList<MaskMapPoint> _points = [];
    private Dictionary<string, MaskMapPointLabel> _labelMap = new(StringComparer.Ordinal);
    private MaskRect _viewport;

    /// <summary>小地图（圆形裁剪、点位按直径缩放）</summary>
    public bool IsMiniMap { get; init; }

    /// <summary>最近一次绘制的点位数（用于验证）</summary>
    public int LastDrawnCount { get; private set; }

    /// <summary>最近一次绘制时用图标画的点位数（用于验证）</summary>
    public int LastIconCount { get; private set; }

    public MapPointsLayer()
    {
        IsHitTestVisible = false;
    }

    /// <summary>更新点位、标签与可视区域；有变化时重绘</summary>
    public void Update(IReadOnlyList<MaskMapPoint> points, IEnumerable<MaskMapPointLabel>? labels, MaskRect viewport)
    {
        var labelMap = new Dictionary<string, MaskMapPointLabel>(StringComparer.Ordinal);
        foreach (var label in labels ?? []) labelMap[label.LabelId] = label;
        if (!labelMap.Keys.SequenceEqual(_labelMap.Keys)) _colorBrushCache.Clear();
        _points = points;
        _labelMap = labelMap;
        _viewport = viewport;
        InvalidateVisual();
    }

    private bool HasViewport => _viewport.Width > 0 && _viewport.Height > 0;

    public override void Render(DrawingContext dc)
    {
        LastDrawnCount = 0;
        LastIconCount = 0;
        var aw = Bounds.Width;
        var ah = Bounds.Height;
        if (_points.Count == 0 || !HasViewport || aw <= 0 || ah <= 0) return;

        // 扩展可视区域，避免边缘闪烁（同 WPF：各边外扩一个点位大小）
        var ex = new Rect(_viewport.X - MaskMapPointStatic.Width, _viewport.Y - MaskMapPointStatic.Height,
            _viewport.Width + 2 * MaskMapPointStatic.Width, _viewport.Height + 2 * MaskMapPointStatic.Height);
        if (IsMiniMap)
        {
            var side = Math.Min(aw, ah);
            var clipRect = new Rect((aw - side) / 2.0, (ah - side) / 2.0, side, side);
            using var clip = dc.PushGeometryClip(new EllipseGeometry(clipRect));
            var scaleX = side / _viewport.Width;
            var scaleY = side / _viewport.Height;
            var pointSide = Math.Max(8, Math.Min(16, side / 12.0));
            foreach (var point in _points)
            {
                if (!Contains(ex, point.ImageX, point.ImageY)) continue;
                DrawPoint(dc, point, clipRect.X + (point.ImageX - _viewport.X) * scaleX, clipRect.Y + (point.ImageY - _viewport.Y) * scaleY, pointSide);
            }
        }
        else
        {
            var scaleX = aw / _viewport.Width;
            var scaleY = ah / _viewport.Height;
            foreach (var point in _points)
            {
                if (!Contains(ex, point.ImageX, point.ImageY)) continue;
                DrawPoint(dc, point, (point.ImageX - _viewport.X) * scaleX, (point.ImageY - _viewport.Y) * scaleY, MaskMapPointStatic.Width);
            }
        }
    }

    /// <summary>WPF Rect.Contains：含边界</summary>
    private static bool Contains(Rect r, double x, double y) => x >= r.X && x <= r.Right && y >= r.Y && y <= r.Bottom;

    private void DrawPoint(DrawingContext dc, MaskMapPoint point, double cx, double cy, double size)
    {
        LastDrawnCount++;
        var radius = size / 2.0;
        // WPF DrawingContext.PushOpacity 作用于整组（先合成再整体透明）；Avalonia 的 PushOpacity 逐个图形相乘，叠在一起会更不透明，
        // 所以用整块 35% 的不透明度蒙版（Skia 里是单独一层）
        using var opacity = point.IsHidden
            ? dc.PushOpacityMask(HiddenMask, new Rect(cx - radius - 2, cy - radius - 2, size + 6, size + 6))
            : default(DrawingContext.PushedState?);
        var center = new Point(cx, cy);
        dc.DrawEllipse(ShadowBrush, null, new Point(cx + 2, cy + 2), radius, radius);
        dc.DrawEllipse(FillBrush, BorderPen, center, radius, radius);
        if (_labelMap.TryGetValue(point.LabelId, out var label))
        {
            if (MapImageCache.TryGet(label.IconUrl) is { } image)
            {
                LastIconCount++;
                var rect = new Rect(cx - radius, cy - radius, size, size);
                using var clip = dc.PushGeometryClip(new EllipseGeometry(rect));
                dc.DrawImage(image, new Rect(image.Size), rect);
            }
            else
            {
                _ = MapImageCache.GetAsync(label.IconUrl, CancellationToken.None);
                dc.DrawEllipse(GetColorBrush(label), null, center, radius, radius);
            }
        }
        else
        {
            dc.DrawEllipse(new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(GenerateColor(point.Id)), null, center, radius, radius);
        }
    }

    private IBrush GetColorBrush(MaskMapPointLabel label)
    {
        if (_colorBrushCache.TryGetValue(label.LabelId, out var brush)) return brush;
        var color = label.Color is { } c ? Color.FromArgb(c.A, c.R, c.G, c.B) : GenerateColor(label.LabelId);
        brush = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(color);
        _colorBrushCache[label.LabelId] = brush;
        return brush;
    }

    /// <summary>按字符串生成稳定的颜色（同 WPF：以 GetHashCode 为种子取 80–255）</summary>
    private static Color GenerateColor(string? seed)
    {
        var random = new Random(seed?.GetHashCode() ?? 0);
        return Color.FromRgb((byte)random.Next(80, 256), (byte)random.Next(80, 256), (byte)random.Next(80, 256));
    }

    /// <summary>点位中心在控件里的位置（大地图；同 WPF TryGetPointCenterPosition）</summary>
    public bool TryGetPointCenter(MaskMapPoint point, out Point center)
    {
        center = default;
        if (!HasViewport || Bounds.Width <= 0 || Bounds.Height <= 0) return false;
        center = new Point((point.ImageX - _viewport.X) * Bounds.Width / _viewport.Width, (point.ImageY - _viewport.Y) * Bounds.Height / _viewport.Height);
        return true;
    }

    /// <summary>命中测试：从后往前找包含该位置的点位（同 WPF PointsCanvas.HitTest，只认可视区域内的点位）</summary>
    public MaskMapPoint? HitTestPoint(Point position)
    {
        if (!HasViewport || Bounds.Width <= 0 || Bounds.Height <= 0) return null;
        var scaleX = Bounds.Width / _viewport.Width;
        var scaleY = Bounds.Height / _viewport.Height;
        var vr = new Rect(_viewport.X, _viewport.Y, _viewport.Width, _viewport.Height);
        for (var i = _points.Count - 1; i >= 0; i--)
        {
            var point = _points[i];
            if (!Contains(vr, point.ImageX, point.ImageY)) continue;
            var w = MaskMapPointStatic.Width * scaleX;
            var h = MaskMapPointStatic.Height * scaleY;
            var x = (point.ImageX - _viewport.X) * scaleX;
            var y = (point.ImageY - _viewport.Y) * scaleY;
            if (Contains(new Rect(x - w / 2, y - h / 2, w, h), position.X, position.Y)) return point;
        }

        return null;
    }

    /// <summary>
    /// 可视区域内各点位画出来的范围（控件坐标，含阴影；用于输入区域）。
    /// WPF 的遮罩在大地图时不穿透，但分层窗口的透明像素不接收鼠标，实际接收鼠标的就是画出来的点位
    /// </summary>
    public IEnumerable<Rect> PointRects()
    {
        if (!HasViewport || Bounds.Width <= 0 || Bounds.Height <= 0) yield break;
        var scaleX = Bounds.Width / _viewport.Width;
        var scaleY = Bounds.Height / _viewport.Height;
        var ex = new Rect(_viewport.X - MaskMapPointStatic.Width, _viewport.Y - MaskMapPointStatic.Height,
            _viewport.Width + 2 * MaskMapPointStatic.Width, _viewport.Height + 2 * MaskMapPointStatic.Height);
        var r = MaskMapPointStatic.Width / 2.0;
        foreach (var point in _points)
        {
            if (!Contains(ex, point.ImageX, point.ImageY)) continue;
            var x = (point.ImageX - _viewport.X) * scaleX;
            var y = (point.ImageY - _viewport.Y) * scaleY;
            var rect = new Rect(x - r - 1, y - r - 1, 2 * r + 4, 2 * r + 4).Intersect(new Rect(Bounds.Size));
            if (rect.Width > 0 && rect.Height > 0) yield return rect;
        }
    }
}
