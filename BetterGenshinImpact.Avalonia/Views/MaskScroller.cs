using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>
/// 遮罩上的竖直滚动区（代替 WPF ScrollViewer：Wayland 下遮罩内容离屏渲染，模板控件没有样式）：内容按不限高度排，
/// 超出部分裁掉，滚轮由遮罩自己分发（X11 窗口的滚轮事件 / Wayland wl_pointer.axis），内容超出时右侧画一条细滚动条（同 WPF UI 的细滚动条）
/// </summary>
public sealed class MaskScroller : Control
{
    private static readonly IBrush Thumb = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#66FFFFFF"));
    private Control? _content;

    public MaskScroller()
    {
        ClipToBounds = true;
    }

    public Control? Content
    {
        get => _content;
        set
        {
            if (_content is not null)
            {
                LogicalChildren.Remove(_content);
                VisualChildren.Remove(_content);
            }

            _content = value;
            if (value is not null)
            {
                LogicalChildren.Add(value);
                VisualChildren.Add(value);
            }

            InvalidateMeasure();
        }
    }

    /// <summary>当前滚动位置（内容向上移的距离）</summary>
    public double Offset { get; private set; }

    /// <summary>内容总高</summary>
    public double Extent => _content?.DesiredSize.Height ?? 0;

    /// <summary>最大滚动位置</summary>
    public double MaxOffset => Math.Max(0, Extent - Bounds.Height);

    /// <summary>滚动 dy（正数向下）；返回是否变化。直接重排（Wayland 下内容没有布局管理器）</summary>
    public bool ScrollBy(double dy) => ScrollTo(Offset + dy);

    public bool ScrollTo(double offset)
    {
        var n = Math.Clamp(offset, 0, MaxOffset);
        if (Math.Abs(n - Offset) < 0.01) return false;
        Offset = n;
        InvalidateArrange();
        if (Bounds.Width > 0) Arrange(Bounds);
        InvalidateVisual();
        return true;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_content is null) return default;
        _content.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        var d = _content.DesiredSize;
        return new Size(double.IsInfinity(availableSize.Width) ? d.Width : availableSize.Width, Math.Min(d.Height, availableSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_content is null) return finalSize;
        Offset = Math.Clamp(Offset, 0, Math.Max(0, _content.DesiredSize.Height - finalSize.Height));
        _content.Arrange(new Rect(0, -Offset, finalSize.Width, Math.Max(_content.DesiredSize.Height, finalSize.Height)));
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var extent = Extent;
        var h = Bounds.Height;
        if (extent <= h + 0.5 || h <= 0) return;
        var thumb = Math.Max(16, h * h / extent);
        var top = (h - thumb) * (Offset / Math.Max(1, extent - h));
        context.DrawRectangle(Thumb, null, new RoundedRect(new Rect(Bounds.Width - 3, top, 2, thumb), 1));
    }
}
