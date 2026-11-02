using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>布局编辑中一个控件的新位置与大小（遮罩 DIP）；LayoutKey 与 WPF 相同（StatusList / LogTextBox）</summary>
public readonly record struct MaskLayoutCommit(string LayoutKey, Rect Bounds);

/// <summary>
/// 遮罩布局编辑（WPF AdjustableOverlayItem + MaskWindowViewModel.OnOverlayLayoutCommitted 的对应实现）：
/// 编辑模式下状态栏与日志框显示半透明白底和虚线框，拖动内部移动、拖动边缘 6 DIP 以内改变大小，松开后提交新位置；
/// 右键控件退出编辑模式。指针输入由宿主送进来——X11 遮罩窗口来自 Avalonia 指针事件，
/// Wayland 覆盖层（离屏渲染，没有 Avalonia 输入）来自 layer-shell 的 wl_pointer 事件，两边走同一套逻辑
/// </summary>
public partial class MaskWindow
{
    /// <summary>边缘多宽以内算改变大小（同 WPF 模板里 Thumb 的宽度）</summary>
    private const double ResizeEdge = 6;

    private static readonly IBrush EditBackground = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Colors.White, 0.55);

    private Border? _dragBox;
    private Edge _dragEdge;
    private Point _dragStart;
    private Rect _dragOrigin;

    [Flags]
    public enum Edge
    {
        None = 0,
        Left = 1,
        Top = 2,
        Right = 4,
        Bottom = 8,
    }

    /// <summary>是否处于布局编辑模式</summary>
    public bool IsEditing { get; private set; }

    /// <summary>正在拖动的控件（LayoutKey，没有拖动时为 null；用于验证）</summary>
    public string? DraggingKey => _dragBox is null ? null : KeyOf(_dragBox);

    /// <summary>拖动结束，提交新位置</summary>
    public event Action<MaskLayoutCommit>? LayoutCommitted;

    /// <summary>右键控件：退出编辑模式</summary>
    public event Action? ExitEditRequested;

    /// <summary>编辑状态下可编辑控件的区域（遮罩 DIP），宿主据此设置输入区域（其余部分仍然点击穿透）</summary>
    public IReadOnlyList<Rect> EditableRegions =>
        IsEditing ? new[] { StatusBox, LogBox }.Where(b => b.IsVisible).Select(BoundsOf).ToList() : [];

    public void SetEditing(bool editing)
    {
        if (IsEditing == editing) return;
        IsEditing = editing;
        if (!editing) _dragBox = null;
        ApplyEditState();
    }

    private void ApplyEditState()
    {
        EditBanner.IsVisible = IsEditing;
        StatusOutline.IsVisible = IsEditing && StatusBox.IsVisible;
        LogOutline.IsVisible = IsEditing && LogBox.IsVisible;
        if (IsEditing)
        {
            StatusBox.Background = EditBackground;
            LogBox.Background = EditBackground;
            Outline(StatusOutline, StatusBox);
            Outline(LogOutline, LogBox);
        }
    }

    private static void Outline(Rectangle outline, Control box)
    {
        Canvas.SetLeft(outline, Canvas.GetLeft(box));
        Canvas.SetTop(outline, Canvas.GetTop(box));
        outline.Width = box.Width;
        outline.Height = box.Height;
    }

    private void PlaceUnlessDragging(Control control, double left, double top, double w, double h, double width, double height)
    {
        if (!ReferenceEquals(control, _dragBox)) Place(control, left, top, w, h, width, height);
    }

    private static Rect BoundsOf(Control c)
    {
        var left = Canvas.GetLeft(c);
        var top = Canvas.GetTop(c);
        return new Rect(double.IsNaN(left) ? 0 : left, double.IsNaN(top) ? 0 : top,
            double.IsNaN(c.Width) ? c.Bounds.Width : c.Width, double.IsNaN(c.Height) ? c.Bounds.Height : c.Height);
    }

    private string KeyOf(Control c) => ReferenceEquals(c, StatusBox) ? "StatusList" : "LogTextBox";

    /// <summary>(x, y)（遮罩 DIP）处的可编辑控件；日志框在状态栏之后加入画布，重叠时取日志框（在上层）</summary>
    private Border? HitBox(Point p) =>
        new[] { LogBox, StatusBox }.FirstOrDefault(b => b.IsVisible && BoundsOf(b).Contains(p));

    /// <summary>点在控件的哪条边上（6 DIP 以内）；内部为 None（移动）</summary>
    public static Edge EdgeAt(Rect r, Point p)
    {
        var e = Edge.None;
        if (p.X - r.Left <= ResizeEdge) e |= Edge.Left;
        else if (r.Right - p.X <= ResizeEdge) e |= Edge.Right;
        if (p.Y - r.Top <= ResizeEdge) e |= Edge.Top;
        else if (r.Bottom - p.Y <= ResizeEdge) e |= Edge.Bottom;
        return e;
    }

    /// <summary>当前指针处应显示的光标（编辑模式外为 null）</summary>
    public StandardCursorType? CursorAt(Point p)
    {
        if (!IsEditing) return null;
        if (_dragBox is not null) return CursorFor(_dragEdge);
        return HitBox(p) is { } box ? CursorFor(EdgeAt(BoundsOf(box), p)) : StandardCursorType.Arrow;
    }

    private static StandardCursorType CursorFor(Edge e) => e switch
    {
        Edge.None => StandardCursorType.SizeAll,
        Edge.Left or Edge.Right => StandardCursorType.SizeWestEast,
        Edge.Top or Edge.Bottom => StandardCursorType.SizeNorthSouth,
        Edge.Top | Edge.Left or Edge.Bottom | Edge.Right => StandardCursorType.TopLeftCorner,
        _ => StandardCursorType.TopRightCorner,
    };

    /// <summary>按下：右键控件退出编辑；左键开始移动或改变大小。返回是否处理（需要重画）</summary>
    public bool EditPointerPressed(Point p, bool right)
    {
        if (!IsEditing || HitBox(p) is not { } box) return false;
        if (right)
        {
            _dragBox = null;
            ExitEditRequested?.Invoke();
            return true;
        }

        _dragBox = box;
        _dragOrigin = BoundsOf(box);
        _dragEdge = EdgeAt(_dragOrigin, p);
        _dragStart = p;
        return true;
    }

    /// <summary>拖动中：移动或改变大小（最小尺寸取控件的 MinWidth / MinHeight，同 WPF EnsureMinSize）</summary>
    public bool EditPointerMoved(Point p)
    {
        if (_dragBox is not { } box) return false;
        var dx = p.X - _dragStart.X;
        var dy = p.Y - _dragStart.Y;
        double l = _dragOrigin.Left, t = _dragOrigin.Top, r = _dragOrigin.Right, b = _dragOrigin.Bottom;
        if (_dragEdge == Edge.None)
        {
            l += dx; r += dx; t += dy; b += dy;
        }
        else
        {
            if (_dragEdge.HasFlag(Edge.Left)) l += dx;
            if (_dragEdge.HasFlag(Edge.Right)) r += dx;
            if (_dragEdge.HasFlag(Edge.Top)) t += dy;
            if (_dragEdge.HasFlag(Edge.Bottom)) b += dy;
            var minW = Math.Max(1, box.MinWidth);
            var minH = Math.Max(1, box.MinHeight);
            if (r - l < minW)
            {
                if (_dragEdge.HasFlag(Edge.Left)) l = r - minW; else r = l + minW;
            }

            if (b - t < minH)
            {
                if (_dragEdge.HasFlag(Edge.Top)) t = b - minH; else b = t + minH;
            }
        }

        Canvas.SetLeft(box, l);
        Canvas.SetTop(box, t);
        box.Width = r - l;
        box.Height = b - t;
        Outline(ReferenceEquals(box, StatusBox) ? StatusOutline : LogOutline, box);
        return true;
    }

    /// <summary>松开：提交新位置（WPF LayoutCommitted），宿主换算成比例写回 MaskWindowConfig</summary>
    public bool EditPointerReleased(Point p)
    {
        if (_dragBox is not { } box) return false;
        EditPointerMoved(p);
        var bounds = BoundsOf(box);
        var key = KeyOf(box);
        _dragBox = null;
        if (bounds.Width > 0 && bounds.Height > 0) LayoutCommitted?.Invoke(new MaskLayoutCommit(key, bounds));
        return true;
    }

    // X11：真实窗口，直接用 Avalonia 的指针事件（Wayland 覆盖层由 MaskWindowController 转发 wl_pointer 事件）
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(Root);
        if (EditPointerPressed(point.Position, point.Properties.IsRightButtonPressed))
        {
            if (_dragBox is not null) e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(Root);
        EditPointerMoved(p);
        if (CursorAt(p) is { } cursor) Cursor = new Cursor(cursor);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (EditPointerReleased(e.GetPosition(Root)))
        {
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }
}
