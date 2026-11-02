using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.Model.MaskMap;
using BetterGenshinImpact.ViewModel;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>
/// 大地图上的交互（WPF PointsCanvas 的鼠标事件与 MaskWindow.xaml 的弹窗）：
/// 左键点位 → 点位信息弹窗（PointClickCommand，锚点为点位中心）；右键点位 → 隐藏 / 显示（PointRightClickCommand）；悬停在点位或按钮上显示手形光标；
/// 弹窗与面板里的按钮由这里的命中测试分发（X11 与 Wayland 相同）。
/// 接收鼠标的范围（输入区域）= 画出来的点位 + 弹窗 / 面板，其余部分点击穿透（同 WPF：大地图时窗口不穿透，但透明像素不接收鼠标）
/// </summary>
public partial class MaskWindow
{
    private MaskWindowViewModel? _mapVm;
    private MaskRect _lastBigViewport;
    private string _pointInfoKey = "";
    private MaskButton? _hoverButton;
    private MaskButton? _pressedButton;

    /// <summary>点位信息弹窗（用于验证）</summary>
    public MapPointInfoPanel PointInfoPanel => PointInfo;

    /// <summary>遮罩内容的根（Wayland 下内容脱离窗口离屏渲染，仍以它为坐标原点）</summary>
    public Visual ContentRootVisual => ContentRoot;

    /// <summary>弹窗在遮罩上的范围（DIP；不显示时为空）</summary>
    public Rect? PointInfoBounds => PointInfo.IsVisible ? new Rect(Canvas.GetLeft(PointInfo), Canvas.GetTop(PointInfo), PointInfo.Bounds.Width > 0 ? PointInfo.Bounds.Width : PointInfo.Width, PointInfo.DesiredSize.Height) : null;

    private void ApplyMapPopups(MaskWindowViewModel map, double width, double height)
    {
        var pop = map.PointInfoPopup;
        var key = pop.IsOpen
            ? string.Join("|", pop.Title, pop.IsCurrentPointHidden, pop.IsTextLoading, pop.TextError, pop.Text, pop.HasUrlList, pop.UrlList.Count,
                pop.HasImage, pop.Image?.GetHashCode() ?? 0, pop.IsImageLoading, pop.ImageError, pop.IsGuideWindowAvailable, pop.IsGuideWindowPreferred)
            : "";
        if (!map.IsInBigMapUi && pop.IsOpen) key = "";
        if (key != _pointInfoKey)
        {
            _pointInfoKey = key;
            _hoverButton = _pressedButton = null;
            PointInfo.Rebuild(pop);
            PointInfo.IsVisible = key.Length > 0;
        }

        if (!PointInfo.IsVisible) return;
        // WPF Popup Placement=Top、PlacementRectangle=点位范围、HorizontalOffset -164、VerticalOffset -8：弹窗底边在点位上方 8，
        // 水平居中于点位（360 宽）；上方放不下时翻到点位下方，左右不超出遮罩
        PointInfo.Measure(new Size(PointInfo.Width, double.PositiveInfinity));
        var h = PointInfo.DesiredSize.Height;
        var r = pop.PlacementRect;
        var left = Math.Clamp(r.X - 164, 0, Math.Max(0, width - PointInfo.Width));
        var top = r.Y - 8 - h;
        if (top < 0) top = r.Bottom + 8;
        top = Math.Max(0, Math.Min(top, height - h));
        Canvas.SetLeft(PointInfo, left);
        Canvas.SetTop(PointInfo, top);
        // Wayland 下内容脱离窗口（没有布局管理器），子元素变化不会让上层重新布局，所以这里直接排好（X11 下与画布的排法相同）
        PointInfo.Arrange(new Rect(left, top, PointInfo.Width, h));
    }

    /// <summary>大地图时接收鼠标的范围（遮罩 DIP）：点位 + 弹窗</summary>
    public IReadOnlyList<Rect> MapRegions
    {
        get
        {
            if (_mapVm is not { IsInBigMapUi: true } || !BigMapPoints.IsVisible) return [];
            var list = BigMapPoints.PointRects().ToList();
            if (PointInfoBounds is { } b) list.Add(b);
            return list;
        }
    }

    /// <summary>(x, y) 处可点击的按钮（只找大地图弹窗 / 面板里的 <see cref="MaskButton"/>）</summary>
    private MaskButton? ButtonAt(Point p)
    {
        if (_mapVm is not { IsInBigMapUi: true }) return null;
        return Find(MapCanvas);

        MaskButton? Find(Visual v)
        {
            if (!v.IsVisible) return null;
            var children = v.GetVisualChildren().ToList();
            for (var i = children.Count - 1; i >= 0; i--)
            {
                if (Find(children[i]) is { } hit) return hit;
            }

            if (v is MaskButton { Enabled: true } button && button.TransformToVisual(ContentRoot) is { } m
                && new Rect(button.Bounds.Size).TransformToAABB(m).Contains(p))
            {
                return button;
            }

            return null;
        }
    }

    /// <summary>(x, y) 在弹窗或面板上（不算点位）</summary>
    private bool OverPanels(Point p) => PointInfoBounds is { } b && b.Contains(p);

    /// <summary>当前指针处的光标：按钮或点位上为手形（同 WPF Cursors.Hand），其余为箭头；不在大地图时为 null</summary>
    public StandardCursorType? MapCursorAt(Point p)
    {
        if (_mapVm is not { IsInBigMapUi: true }) return null;
        if (ButtonAt(p) is not null) return StandardCursorType.Hand;
        if (!OverPanels(p) && BigMapPoints.HitTestPoint(p) is not null) return StandardCursorType.Hand;
        return StandardCursorType.Arrow;
    }

    /// <summary>按下：按钮记下（松开时点击）；点位左键打开信息弹窗、右键隐藏 / 显示（同 WPF PointsCanvas 在按下时执行）。返回是否需要重画</summary>
    public bool MapPointerPressed(Point p, bool right)
    {
        if (_mapVm is not { IsInBigMapUi: true } map) return false;
        if (!right && ButtonAt(p) is { } button)
        {
            _pressedButton = button;
            return false;
        }

        if (OverPanels(p) || BigMapPoints.HitTestPoint(p) is not { } point) return false;
        if (right)
        {
            _ = map.PointRightClickCommand.ExecuteAsync(point);
        }
        else
        {
            var anchor = BigMapPoints.TryGetPointCenter(point, out var c) ? c : p;
            _ = map.PointClickCommand.ExecuteAsync(new MaskMapPointClickArgs(point, new System.Drawing.PointF((float)anchor.X, (float)anchor.Y)));
        }

        return true;
    }

    /// <summary>移动：更新悬停的按钮；返回是否需要重画</summary>
    public bool MapPointerMoved(Point p)
    {
        var hover = ButtonAt(p);
        if (ReferenceEquals(hover, _hoverButton)) return false;
        if (_hoverButton is not null) _hoverButton.IsHovered = false;
        _hoverButton = hover;
        if (hover is not null) hover.IsHovered = true;
        return true;
    }

    /// <summary>松开：在按下的同一个按钮上松开时点击</summary>
    public bool MapPointerReleased(Point p)
    {
        var pressed = _pressedButton;
        _pressedButton = null;
        if (pressed is null || !ReferenceEquals(ButtonAt(p), pressed)) return false;
        pressed.Click?.Invoke();
        return true;
    }
}
