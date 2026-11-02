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
    private string _pickerKey = "";
    private Border? _pickerToggle;
    private MaskButton? _pickerToggleButton;
    private double _pickerToggleSide;

    /// <summary>遮罩自己的界面状态变了（下拉框开关、滚动），需要重新排版、更新输入区域并重画</summary>
    public event Action? MapLayoutChanged;

    /// <summary>点位选择面板（用于验证）</summary>
    public MapPointPicker PickerPanel => Picker;

    /// <summary>左下角打开选择面板的按钮（用于验证；不在大地图时为 null 或不可见）</summary>
    public MaskButton? PickerToggleButton => _pickerToggle is { IsVisible: true } ? _pickerToggleButton : null;

    /// <summary>点位信息弹窗（用于验证）</summary>
    public MapPointInfoPanel PointInfoPanel => PointInfo;

    /// <summary>遮罩内容的根（Wayland 下内容脱离窗口离屏渲染，仍以它为坐标原点）</summary>
    public Visual ContentRootVisual => ContentRoot;

    /// <summary>弹窗在遮罩上的范围（DIP；不显示时为空）</summary>
    public Rect? PointInfoBounds => PointInfo.IsVisible ? new Rect(Canvas.GetLeft(PointInfo), Canvas.GetTop(PointInfo), PointInfo.Bounds.Width > 0 ? PointInfo.Bounds.Width : PointInfo.Width, PointInfo.DesiredSize.Height) : null;

    private void ApplyMapPopups(MaskWindowViewModel map, double width, double height)
    {
        ApplyPicker(map, width, height);
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

    /// <summary>
    /// 地球按钮与选择面板的位置（同 WPF MapPointPickerRoot 的网格）：外层列 108* / 1802* / 10*、行 10* / 1038* / 32*，
    /// 内层列 58* / 12* / 640* / 1092*、行 328* / 640* / 12* / 58*，按钮在内层第 4 行第 1 列（Viewbox 等比缩放 58×58 的按钮并居中）；
    /// 面板为 Popup Placement=Top、VerticalOffset -12：底边在这个格子上方 12，左边与格子对齐，高为遮罩高的 0.62
    /// </summary>
    private void ApplyPicker(MaskWindowViewModel map, double width, double height)
    {
        var cellX = 108 / 1920d * width;
        var cellY = 990 / 1080d * height;
        var cellW = 58 / 1920d * width;
        var cellH = 58 / 1080d * height;
        var side = Math.Min(cellW, cellH);
        if (_pickerToggle is null || Math.Abs(_pickerToggleSide - side) > 0.01)
        {
            if (_pickerToggle is not null) MapCanvas.Children.Remove(_pickerToggle);
            _pickerToggleSide = side;
            var s = side / 58;
            _pickerToggleButton = new MaskButton
            {
                Width = 46 * s, Height = 46 * s, CornerRadius = new CornerRadius(23 * s), Padding = new Thickness(8 * s, 0, 0, 0),
                Background = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(global::Avalonia.Media.Color.Parse("#ECE5D8")),
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = Services.FluentSymbols.Glyph("GlobeLocation24"), FontFamily = Services.FluentSymbols.Font, FontSize = 30 * s,
                    Foreground = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(global::Avalonia.Media.Color.Parse("#3B4255")),
                    HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                },
                Click = () => _ = _mapVm?.ToggleMapPointPickerCommand.ExecuteAsync(null),
            };
            _pickerToggle = new Border
            {
                Width = 58 * s, Height = 58 * s, CornerRadius = new CornerRadius(28.5 * s), BorderThickness = new Thickness(6 * s),
                BorderBrush = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(global::Avalonia.Media.Color.Parse("#77FDF5E4")),
                BoxShadow = global::Avalonia.Media.BoxShadows.Parse($"0 0 {2 * s:0.##} 0 #408C8C8C"),
                Child = _pickerToggleButton,
            };
            MapCanvas.Children.Insert(0, _pickerToggle);
        }

        _pickerToggle.IsVisible = map.IsInBigMapUi;
        var tl = cellX + (cellW - side) / 2;
        var tt = cellY + (cellH - side) / 2;
        Canvas.SetLeft(_pickerToggle, tl);
        Canvas.SetTop(_pickerToggle, tt);
        _pickerToggle.Measure(new Size(side, side));
        _pickerToggle.Arrange(new Rect(tl, tt, side, side));

        var open = map.IsInBigMapUi && map.IsMapPointPickerOpen;
        var ph = Math.Max(0, 0.62 * height);
        var key = open
            ? string.Join("|", ph, map.SelectedMapPointApiProviderOption?.DisplayName, map.IsHoYoLabProviderSelected, map.SelectedHoYoLabLanguageOption?.DisplayName,
                map.MapLabelSearchText, map.MapPointVisibilitySummary, map.HasMapPoints, map.HasHiddenMapPoints, map.IsMapLabelTreeLoading, map.SelectedMapLabelCategory?.Id,
                string.Join(",", map.MapLabelCategories.Select(x => x.Id)),
                string.Join(",", map.MapLabelItems.Select(x => x.Id + (Services.MapImageCache.TryGet(x.IconUrl) is null ? "" : "+"))),
                string.Join(",", map.SelectedMapLabelItems.Select(x => x.Id + (Services.MapImageCache.TryGet(x.IconUrl) is null ? "" : "+"))),
                Picker.StateKey)
            : "";
        var rebuilt = false;
        if (key != _pickerKey)
        {
            _pickerKey = key;
            _hoverButton = _pressedButton = null;
            if (open) Picker.Rebuild(map, ph);
            else Picker.Children.Clear();
            Picker.IsVisible = open;
            rebuilt = true;
        }

        if (!open) return;
        var left = cellX;
        var top = Math.Max(0, cellY - 12 - ph);
        Canvas.SetLeft(Picker, left);
        Canvas.SetTop(Picker, top);
        // 同点位弹窗：Wayland 下没有布局管理器，直接排好
        Picker.Measure(new Size(double.PositiveInfinity, ph));
        Picker.Arrange(new Rect(left, top, Picker.DesiredSize.Width, ph));
        if (rebuilt) Picker.RestoreScroll();
    }

    /// <summary>控件在遮罩上的范围（DIP）</summary>
    private Rect? MapBoundsOf(Control? c) =>
        c is { IsVisible: true } && c.TransformToVisual(ContentRoot) is { } m ? new Rect(c.Bounds.Size).TransformToAABB(m) : null;

    /// <summary>选择面板的各块（面板、已选列）与地球按钮在遮罩上的范围</summary>
    private IEnumerable<Rect> PickerRects()
    {
        if (_mapVm is not { IsInBigMapUi: true }) yield break;
        if (MapBoundsOf(_pickerToggle) is { } t) yield return t;
        if (!Picker.IsVisible) yield break;
        if (MapBoundsOf(Picker.PanelBorderControl) is { } p) yield return p;
        if (MapBoundsOf(Picker.SelectedColumn) is { } s) yield return s;
    }

    /// <summary>大地图时接收鼠标的范围（遮罩 DIP）：点位 + 弹窗</summary>
    public IReadOnlyList<Rect> MapRegions
    {
        get
        {
            if (_mapVm is not { IsInBigMapUi: true } || !BigMapPoints.IsVisible) return [];
            var list = BigMapPoints.PointRects().ToList();
            list.AddRange(PickerRects());
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
            // 滚动区外（被裁掉）的部分不能点
            if (v is MaskScroller sc && (MapBoundsOf(sc) is not { } sr || !sr.Contains(p))) return null;
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
    private bool OverPanels(Point p) => PointInfoBounds is { } b && b.Contains(p) || PickerRects().Any(r => r.Contains(p));

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
        var hit = right ? null : ButtonAt(p);
        if (Picker.OpenDropDown is not null && (hit is null || (!Picker.DropDownOptions.Contains(hit) && !ReferenceEquals(hit, Picker.ProviderButton) && !ReferenceEquals(hit, Picker.LanguageButton))))
        {
            // 同 WPF ComboBox：在下拉框外按下时收起（这次按下只用来收起）
            Picker.ToggleDropDown(null);
            MapLayoutChanged?.Invoke();
            return true;
        }

        if (hit is { } button)
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
        MapLayoutChanged?.Invoke();
        return true;
    }

    /// <summary>滚轮：滚动指针下的列表（dy 正数向下，DIP）；返回是否需要重画</summary>
    public bool MapPointerWheel(Point p, double dy)
    {
        if (_mapVm is not { IsInBigMapUi: true }) return false;
        var scroller = FindScroller(MapCanvas);
        return scroller is not null && scroller.ScrollBy(dy);

        MaskScroller? FindScroller(Visual v)
        {
            if (!v.IsVisible) return null;
            var children = v.GetVisualChildren().ToList();
            for (var i = children.Count - 1; i >= 0; i--)
            {
                if (FindScroller(children[i]) is { } hit) return hit;
            }

            return v is MaskScroller s && s.MaxOffset > 0 && MapBoundsOf(s) is { } r && r.Contains(p) ? s : null;
        }
    }
}
