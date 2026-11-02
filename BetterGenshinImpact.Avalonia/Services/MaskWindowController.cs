using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Avalonia.Views;
using BetterGenshinImpact.Host.Common;
using BetterGenshinImpact.Platform.Linux.Wayland;
using BetterGenshinImpact.Platform.Linux.X11;
using Serilog;

namespace BetterGenshinImpact.Avalonia.Services;

/// <summary>
/// 遮罩窗口的显示与跟随：截图器运行且启用遮罩时，把遮罩窗口盖在游戏画面上（每 500 ms 跟随位置和大小），
/// 停止后隐藏。点击穿透：窗口打开后把 X11 SHAPE 输入区域设为空。
/// Wayland 会话（niri）下遮罩内容离屏渲染后提交到 layer-shell 覆盖层（<see cref="WaylandMaskSurface"/>），不显示 X 窗口。
/// </summary>
public sealed class MaskWindowController : IDisposable
{
    private readonly GameHost _host;
    private readonly string? _display;
    private readonly ILogger _log;
    private readonly DispatcherTimer _timer;
    private MaskWindow? _window;
    private PixelRect _lastRect;
    private readonly WaylandPlatformServices? _wayland;
    private WaylandMaskSurface? _surface;
    private bool _surfaceFailed;
    private bool _placementWarned;
    private readonly DispatcherTimer _focusTimer;
    private readonly MaskFocusPolicy _focus = new();
    private string _focusReason = "";

    public MaskWindowController(GameHost host, string? display, ILogger log)
    {
        _host = host;
        _display = display;
        _log = log;
        _wayland = host.Platform as WaylandPlatformServices;
        Current = this;
        // 遮罩日志框：同 WPF，只有遮罩启用且显示日志框时才接收日志
        MaskLogSink.Instance.IsEnabled = () => _host.LinuxConfig.MaskWindowEnabled && _host.Config.MaskWindowConfig is { MaskEnabled: true, ShowLogBox: true };
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => Update());
        _timer.Start();
        // 焦点：每 100 ms 判断一次（X11 读 _NET_ACTIVE_WINDOW / 输入焦点），niri 的 WindowFocusChanged 事件立即判断；防抖见 MaskFocusPolicy
        _focusTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => EvaluateFocus());
        _focusTimer.Start();
        if (_wayland?.Niri is { } niri)
        {
            try
            {
                niri.Events.Received += e =>
                {
                    if (e.Kind == "WindowFocusChanged") Dispatcher.UIThread.Post(EvaluateFocus, DispatcherPriority.Background);
                };
            }
            catch (Exception e) when (e is IOException or InvalidOperationException)
            {
                _log.Debug(e, "niri 事件流不可用，遮罩焦点改为轮询");
            }
        }

        // 配置里的颜色是 WPF 颜色文本（颜色名或 #AARRGGBB），用 Avalonia 的解析（语义相同）
        MaskColorParser.ColorTextConverter ??= text =>
        {
            var c = Color.Parse(text);
            return MaskColor.FromArgb(c.A, c.R, c.G, c.B);
        };

        // 绘制板可在任意线程变化，合并到 UI 线程上刷新
        host.MaskDrawing.Changed += OnDrawingChanged;

        // 布局编辑模式：启动时关闭（同 WPF MainWindowViewModel），之后跟随设置页开关
        if (host.Config.MaskWindowConfig.OverlayLayoutEditEnabled)
        {
            host.Config.MaskWindowConfig.OverlayLayoutEditEnabled = false;
        }

        host.Config.MaskWindowConfig.PropertyChanged += OnMaskConfigChanged;
        InitMapPoints();
        // Wayland 覆盖层没有 Avalonia 输入：编辑模式下定时读取 wl_pointer 事件
        _pumpTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Input, (_, _) => PumpOverlay());
    }

    private readonly DispatcherTimer _pumpTimer;
    private string _inputRegionKey = "";

    /// <summary>最近一次设置的输入区域（窗口像素 / Wayland 逻辑坐标；空 = 完全点击穿透），用于验证</summary>
    public IReadOnlyList<System.Drawing.Rectangle> InputRegion { get; private set; } = [];

    private BetterGenshinImpact.ViewModel.MaskWindowViewModel? _map;
    private IMaskWindowSnapshotSource<MaskWindowMapSnapshot>? _mapSource;
    private int _mapQueued;
    private int _mapRefreshQueued;

    /// <summary>
    /// 地图点位：界面模型（与 WPF 共用）来自宿主；大地图识别（MapMaskTrigger → MaskWindowMapState）的快照在 UI 线程上合并后交给模型（同 WPF UiCoalescer），
    /// 模型或图标变化后重绘点位层。图标 / 详情图片由 <see cref="MapImageCache"/> 加载
    /// </summary>
    private void InitMapPoints()
    {
        MapImageCache.Configure(_host.Services.GetService(typeof(BetterGenshinImpact.Infrastructure.Cache.IFileCache)) as BetterGenshinImpact.Infrastructure.Cache.IFileCache,
            _host.Services.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory, _log);
        BetterGenshinImpact.ViewModel.MapLabelItemVm.IconLoader = async (url, ct) => await MapImageCache.GetAsync(url, ct);
        BetterGenshinImpact.ViewModel.MaskMapPointInfoPopupViewModel.ImageLoader = async (url, ct) => await MapImageCache.LoadPointImageAsync(url, ct);
        MapImageCache.ImageUpdated += OnMapImageUpdated;
        _mapSource = _host.Services.GetService(typeof(IMaskWindowMapState)) as IMaskWindowSnapshotSource<MaskWindowMapSnapshot>;
        if (_mapSource is not null) _mapSource.Changed += OnMapSnapshotChanged;
        UseMapViewModel(_host.Services.GetService(typeof(BetterGenshinImpact.ViewModel.MaskWindowViewModel)) as BetterGenshinImpact.ViewModel.MaskWindowViewModel);
    }

    /// <summary>当前的地图点位界面模型（自测可换成用假点位服务的模型）</summary>
    public BetterGenshinImpact.ViewModel.MaskWindowViewModel? MapViewModel => _map;

    public void UseMapViewModel(BetterGenshinImpact.ViewModel.MaskWindowViewModel? map)
    {
        if (_map is not null)
        {
            _map.PropertyChanged -= OnMapPropertyChanged;
            _map.PointInfoPopup.PropertyChanged -= OnMapPropertyChanged;
        }

        _map = map;
        if (_map is not null)
        {
            _map.PropertyChanged += OnMapPropertyChanged;
            _map.PointInfoPopup.PropertyChanged += OnMapPropertyChanged;
            if (_mapSource is not null) _map.ApplyMapSnapshot(_mapSource.Current);
        }

        WatchMapCollections();
        QueueMapRefresh();
    }

    private void OnMapSnapshotChanged()
    {
        if (Interlocked.Exchange(ref _mapQueued, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            Volatile.Write(ref _mapQueued, 0);
            if (_mapSource is not null) _map?.ApplyMapSnapshot(_mapSource.Current);
        }, DispatcherPriority.Background);
    }

    private void OnMapPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        WatchMapCollections();
        QueueMapRefresh();
    }

    private readonly List<System.Collections.Specialized.INotifyCollectionChanged> _watchedMapCollections = [];

    /// <summary>选择面板里的列表（分类、标签、已选标签）是集合，增删不触发模型的属性变化，所以单独监听（集合整个换掉时重新监听）</summary>
    private void WatchMapCollections()
    {
        System.Collections.Specialized.INotifyCollectionChanged[] now = _map is { } m ? [m.MapLabelCategories, m.MapLabelItems, m.SelectedMapLabelItems] : [];
        if (now.SequenceEqual(_watchedMapCollections)) return;
        foreach (var c in _watchedMapCollections) c.CollectionChanged -= OnMapCollectionChanged;
        _watchedMapCollections.Clear();
        foreach (var c in now)
        {
            c.CollectionChanged += OnMapCollectionChanged;
            _watchedMapCollections.Add(c);
        }
    }

    private void OnMapCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => QueueMapRefresh();

    private void OnMapImageUpdated(object? sender, string url) => QueueMapRefresh();

    private void QueueMapRefresh()
    {
        if (Interlocked.Exchange(ref _mapRefreshQueued, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            Volatile.Write(ref _mapRefreshQueued, 0);
            RefreshMapPoints();
        }, DispatcherPriority.Background);
    }

    private void RefreshMapPoints()
    {
        // 同 WPF：选择面板关闭（或遮罩隐藏）时隐藏搜索窗口
        if (_searchWindow is { IsVisible: true } && (!IsShown || _map is not { IsInBigMapUi: true, IsMapPointPickerOpen: true })) _searchWindow.Hide();
        if (_window is not { } window || !IsShown) return;
        var (width, height, _, _) = OverlayLayout;
        if (width <= 0 || height <= 0) return;
        window.ApplyMapPoints(_map, width, height);
        ApplyInputRegion(_wayland is not null ? 1 : window.RenderScaling > 0 ? window.RenderScaling : 1);
        UpdatePump();
        _surface?.Render();
    }

    private Views.MapLabelSearchWindow? _searchWindow;

    /// <summary>点位标签搜索窗口（用于验证；未打开过时为 null）</summary>
    public Views.MapLabelSearchWindow? SearchWindow => _searchWindow;

    /// <summary>
    /// 点了选择面板的搜索框：弹出可获得焦点的搜索窗口，左边与搜索框对齐、底边在搜索框上方 4（同 WPF MapLabelSearchTextBox_OnPreviewMouseLeftButtonDown）。
    /// Wayland 下搜索窗口是 XWayland 窗口，位置由合成器决定
    /// </summary>
    private void ShowSearch(Rect box)
    {
        if (_map is null || _window is not { } window) return;
        _searchWindow ??= new Views.MapLabelSearchWindow();
        _searchWindow.AttachViewModel(_map);
        if (OverlayPlacement is { } o)
        {
            _searchWindow.Position = new PixelPoint(o.X + (int)Math.Round(box.X), o.Y + (int)Math.Round(box.Y - 4 - _searchWindow.Height));
        }
        else
        {
            var s = window.RenderScaling > 0 ? window.RenderScaling : 1;
            _searchWindow.Position = new PixelPoint(window.Position.X + (int)Math.Round(box.X * s), window.Position.Y + (int)Math.Round((box.Y - 4 - _searchWindow.Height) * s));
        }

        if (!_searchWindow.IsVisible) _searchWindow.Show();
        _searchWindow.Topmost = true;
        _searchWindow.FocusSearch();
        // 窗口刚映射时 X 服务器可能还不接受设置焦点，稍后再聚焦一次
        var sw = _searchWindow;
        DispatcherTimer.RunOnce(() =>
        {
            if (sw.IsVisible && !sw.IsActive) sw.FocusSearch();
        }, TimeSpan.FromMilliseconds(150));
        _log.Information("遮罩：打开点位标签搜索窗口 {Position}", _searchWindow.Position);
    }

    /// <summary>Wayland：编辑模式或大地图时定时读取覆盖层的指针事件</summary>
    private void UpdatePump()
    {
        var want = _wayland is not null && (Editing || _map?.IsInBigMapUi == true);
        if (want && !_pumpTimer.IsEnabled) _pumpTimer.Start();
        else if (!want && _pumpTimer.IsEnabled) _pumpTimer.Stop();
    }

    /// <summary>布局编辑模式（MaskWindowConfig.OverlayLayoutEditEnabled）：遮罩强制显示，可编辑控件接收鼠标</summary>
    public bool Editing => _host.Config.MaskWindowConfig.OverlayLayoutEditEnabled;

    private void OnMaskConfigChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Core.Config.MaskWindowConfig.OverlayLayoutEditEnabled))
        {
            Dispatcher.UIThread.Post(OnEditingChanged);
        }
    }

    private void OnEditingChanged()
    {
        var editing = Editing;
        _log.Information(editing ? "遮罩：进入布局编辑模式" : "遮罩：退出布局编辑模式");
        _window?.SetEditing(editing);
        UpdatePump();
        Update();
    }

    private MaskWindow CreateWindow()
    {
        var window = new MaskWindow();
        window.SetEditing(Editing);
        window.LayoutCommitted += CommitLayout;
        window.MapLayoutChanged += QueueMapRefresh;
        window.MapSearchRequested += ShowSearch;
        window.ExitEditRequested += ExitEdit;
        return window;
    }

    /// <summary>右键控件退出编辑模式并把焦点还给游戏（同 WPF OnExitOverlayLayoutEditMode）</summary>
    private void ExitEdit()
    {
        _host.Config.MaskWindowConfig.OverlayLayoutEditEnabled = false;
        try
        {
            _host.RuntimeService.Current?.Window.Activate();
        }
        catch (Exception e)
        {
            _log.Debug(e, "激活游戏窗口失败");
        }
    }

    /// <summary>拖动结束：位置与大小换算成相对遮罩大小的比例写回配置（同 WPF MaskWindowViewModel.OnOverlayLayoutCommitted）</summary>
    private void CommitLayout(MaskLayoutCommit commit)
    {
        var (width, height, _, _) = OverlayLayout;
        if (width <= 0 || height <= 0) return;
        var c = _host.Config.MaskWindowConfig;
        var b = commit.Bounds;
        double Ratio(double v, double size) => Math.Clamp(double.IsNaN(v) ? 0 : v / size, 0, 1);
        switch (commit.LayoutKey)
        {
            case "LogTextBox":
                c.LogTextBoxLeftRatio = Ratio(b.X, width);
                c.LogTextBoxTopRatio = Ratio(b.Y, height);
                c.LogTextBoxWidthRatio = Ratio(b.Width, width);
                c.LogTextBoxHeightRatio = Ratio(b.Height, height);
                break;
            case "StatusList":
                c.StatusListLeftRatio = Ratio(b.X, width);
                c.StatusListTopRatio = Ratio(b.Y, height);
                c.StatusListWidthRatio = Ratio(b.Width, width);
                c.StatusListHeightRatio = Ratio(b.Height, height);
                break;
        }

        _log.Information("遮罩布局：{Key} 调整为 ({X:0},{Y:0}) {W:0}x{H:0}", commit.LayoutKey, b.X, b.Y, b.Width, b.Height);
        Update();
    }

    /// <summary>
    /// 输入区域：平时为空（完全点击穿透）；编辑模式下为状态栏与日志框的区域，大地图时为点位与弹窗 / 面板
    /// （同 WPF：只有画出来的可交互部分接收鼠标，透明部分仍然穿透）
    /// </summary>
    private void ApplyInputRegion(double scale)
    {
        var regions = _window is { } w ? w.EditableRegions.Concat(w.MapRegions).ToList() : [];
        var rects = regions.Select(r => new System.Drawing.Rectangle((int)Math.Floor(r.X * scale), (int)Math.Floor(r.Y * scale),
            (int)Math.Ceiling(r.Width * scale), (int)Math.Ceiling(r.Height * scale))).ToList();
        var key = string.Join(";", rects);
        if (key == _inputRegionKey) return;
        if (_wayland is not null)
        {
            if (_surface is null) return;
            _surface.SetInputRegion(rects);
        }
        else
        {
            if (Handle == 0) return;
            var error = X11Overlay.SetInputRegion(_display, Handle, rects);
            if (error is not null)
            {
                _log.Warning("遮罩窗口无法设置输入区域：{Error}", error);
                return;
            }
        }

        _inputRegionKey = key;
        InputRegion = rects;
        if (rects.Count > 0) _log.Information("遮罩输入区域：{Rects}", key);
    }

    /// <summary>Wayland 编辑模式：读取覆盖层上的指针事件</summary>
    private void PumpOverlay()
    {
        try
        {
            _surface?.Pump();
        }
        catch (Exception e)
        {
            _log.Debug(e, "读取 Wayland 覆盖层事件失败");
        }
    }

    private void OnOverlayPointer(OverlayPointerEvent e)
    {
        if (_window is not { } window) return;
        var p = new Point(e.X, e.Y);
        var right = e.Button == 0x111;
        var changed = e.Kind switch
        {
            OverlayPointerKind.Press => window.EditPointerPressed(p, right) || window.MapPointerPressed(p, right),
            OverlayPointerKind.Move => window.EditPointerMoved(p) | window.MapPointerMoved(p),
            OverlayPointerKind.Release => window.EditPointerReleased(p) || window.MapPointerReleased(p),
            OverlayPointerKind.Leave => window.MapPointerMoved(new Point(-1, -1)),
            // wl_pointer.axis：滚轮一格约 10（niri / mutter / KWin），换算成 WPF ScrollViewer 的一格 48
            OverlayPointerKind.Scroll => window.MapPointerWheel(p, e.Delta * 4.8),
            _ => false,
        };
        _surface?.SetCursorShape((window.CursorAt(p) ?? window.MapCursorAt(p)) switch
        {
            StandardCursorType.Hand => 4u,
            StandardCursorType.SizeAll => 13u,
            StandardCursorType.SizeWestEast => 26u,
            StandardCursorType.SizeNorthSouth => 27u,
            StandardCursorType.TopLeftCorner => 29u,
            StandardCursorType.TopRightCorner => 28u,
            _ => 1u,
        });
        if (changed) _surface?.Render();
    }

    private int _drawingQueued;
    private bool _drawingLogged;

    private void OnDrawingChanged()
    {
        if (Interlocked.Exchange(ref _drawingQueued, 1) == 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Volatile.Write(ref _drawingQueued, 0);
                RefreshDrawing();
            }, DispatcherPriority.Render);
        }
    }

    /// <summary>最近一次绘制的图形数（用于验证）</summary>
    public int DrawnShapes => _window?.DrawingLayer.LastShapeCount ?? 0;

    /// <summary>当前绘制快照中的分组名（用于验证）</summary>
    public IReadOnlyList<string> DrawingGroups => _host.MaskDrawing.Current.Entries.Select(e => e.Group.Name).ToList();

    private void RefreshDrawing()
    {
        if (_window is not { } window || !IsShown || _lastRect.Width <= 0)
        {
            return;
        }

        var config = _host.Config.MaskWindowConfig;
        window.DrawingLayer.Update(_host.MaskDrawing.Current, CreateStyle(config, ScaleTo1080P(_lastRect)),
            config.DisplayRecognitionResultsOnMask, _lastRect.Size);
        _surface?.Render();
    }

    /// <summary>由 MaskWindowConfig 的 Recognition* 配置合成绘制样式（同 WPF MaskWindowViewModel.CreateDrawingStyle）</summary>
    private static MaskWindowDrawingStyle CreateStyle(Core.Config.MaskWindowConfig config, double scaleTo1080P) =>
        new(config.RecognitionUseDrawableStyle,
            new MaskWindowDrawingStroke(ParseColor(config.RecognitionRectStrokeColor, MaskColor.Red), Math.Max(0, config.RecognitionRectStrokeThickness)),
            new MaskWindowDrawingStroke(ParseColor(config.RecognitionLineStrokeColor, MaskColor.Red), Math.Max(0, config.RecognitionLineStrokeThickness)),
            ParseColor(config.RecognitionTextColor, MaskColor.Black),
            Math.Max(1, config.RecognitionTextFontSize) * scaleTo1080P);

    private static MaskColor ParseColor(string? text, MaskColor fallback)
    {
        try
        {
            return string.IsNullOrWhiteSpace(text) ? fallback : MaskColorParser.ConvertFromString(text);
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    /// <summary>遮罩窗口的 X11 窗口号（未显示时为 0），用于验证</summary>
    public nint Handle { get; private set; }

    public bool IsShown => _surface?.IsShown ?? _window?.IsVisible == true;

    /// <summary>当前的遮罩控制器（宿主初始化失败时为 null），用于自测</summary>
    public static MaskWindowController? Current { get; private set; }

    /// <summary>Wayland 覆盖层最近一次提交的位图中 (x, y)（缓冲区像素）的 ARGB（预乘）；未显示时为 null，用于验证</summary>
    public uint? OverlayPixel(int x, int y) => _surface?.Pixel(x, y);

    /// <summary>Wayland 覆盖层的位置（输出名、相对输出的逻辑坐标；非 Wayland 或未显示时为 null），用于验证</summary>
    public WaylandOverlayPlacement? OverlayPlacement => _surface?.Placement;

    /// <summary>防抖后遮罩是否应随游戏焦点显示（用于验证）</summary>
    public bool FocusVisible => _focus.Visible;

    /// <summary>遮罩窗口（X11 为真实窗口，Wayland 为渲染到覆盖层的内容；用于验证）</summary>
    public MaskWindow? Window => _window;

    /// <summary>上次摆放状态栏与日志框用的遮罩大小（DIP）、1080P 比例与显示缩放（用于验证）</summary>
    public (double Width, double Height, double ScaleTo1080P, double DpiScale) OverlayLayout { get; private set; }

    /// <summary>遮罩因焦点显示 / 隐藏的次数（用于验证防抖）</summary>
    public int FocusHideCount { get; private set; }
    public int FocusShowCount { get; private set; }

    /// <summary>
    /// 按 WPF 规则判断游戏焦点（MaskFocusPolicy），可见状态变化时立即隐藏或显示遮罩
    /// </summary>
    private void EvaluateFocus()
    {
        try
        {
            if (!Wanted)
            {
                _focus.Reset();
                return;
            }

            var runtime = _host.RuntimeService.Current!;
            var window = runtime.Window;
            var active = window.IsForeground;
            var owned = !active && IsForegroundOwnedByBetterGiOrGame(window, out _focusReason);
            var decision = MaskFocusPolicy.Decide(runtime.Capture.IsCapturing, window.IsMinimized, active, owned);
            var before = _focus.Visible;
            var visible = _focus.Update(decision, Environment.TickCount64, immediate: !runtime.Capture.IsCapturing);
            if (visible == before) return;
            if (visible)
            {
                FocusShowCount++;
                _log.Information("遮罩：游戏窗口重新获得焦点，显示遮罩");
                Update();
            }
            else
            {
                FocusHideCount++;
                _log.Information("遮罩：游戏窗口失去焦点（前台为{Foreground}），隐藏遮罩", _focusReason);
                if (!Editing) Hide();
            }
        }
        catch (Exception e)
        {
            _log.Debug(e, "判断遮罩焦点失败");
        }
    }

    /// <summary>前台属于 BetterGI 自身或游戏进程（或者没有前台窗口），按进程 ID 判断（同 WPF TaskTriggerDispatcher）</summary>
    private static bool IsForegroundOwnedByBetterGiOrGame(BetterGenshinImpact.Platform.IPlatformWindow window, out string foreground)
    {
        if (BetterGenshinImpact.Platform.PlatformHost.ForegroundProcessId() is not { } pid)
        {
            foreground = "无";
            return true;
        }

        foreground = pid == (uint)Environment.ProcessId ? "BetterGI" : pid == (uint)window.ProcessId ? "游戏进程" : $"进程 {pid}";
        return pid == 0 || pid == (uint)Environment.ProcessId || pid == (uint)window.ProcessId;
    }

    private bool Wanted => _host.LinuxConfig.MaskWindowEnabled && _host.Config.MaskWindowConfig.MaskEnabled && _host.RuntimeService.Current is not null;

    private void Update()
    {
        try
        {
            // 调试：遮罩上显示全部识别结果
            RecognitionObject.ForceDrawOnWindow = Wanted && _host.LinuxConfig.MaskDrawAllRecognition;
            if (!Wanted)
            {
                Hide();
                return;
            }

            var window = _host.RuntimeService.Current!.Window;
            var r = window.Viewport.ScreenRect;
            var rect = new PixelRect(r.X, r.Y, r.Width, r.Height);
            if (rect.Width <= 0 || rect.Height <= 0 || window.IsMinimized)
            {
                Hide();
                return;
            }

            // 游戏失去焦点（其他进程在前台）时不显示，规则与 WPF 相同；布局编辑模式强制显示（WPF MaskWindowHost.IsForcedVisibleLocked）
            if (!_focus.Visible && !Editing)
            {
                Hide();
                return;
            }

            if (_wayland is not null)
            {
                UpdateWayland(window, rect);
                return;
            }

            if (_window is null)
            {
                _window = CreateWindow();
                _window.Opened += (_, _) => OnOpened();
            }

            if (rect != _lastRect || !_window.IsVisible)
            {
                var scale = _window.RenderScaling > 0 ? _window.RenderScaling : 1;
                _window.Position = rect.Position;
                _window.Width = rect.Width / scale;
                _window.Height = rect.Height / scale;
                _lastRect = rect;
            }

            ApplyOverlay(_window, _window.Width, _window.Height, rect, _window.RenderScaling > 0 ? _window.RenderScaling : 1);
            _window.ApplyMapPoints(_map, _window.Width, _window.Height);
            ApplyInputRegion(_window.RenderScaling > 0 ? _window.RenderScaling : 1);
            RefreshDrawing();
            if (!_drawingLogged && DrawnShapes > 0)
            {
                _drawingLogged = true;
                _log.Information("遮罩绘制：{Count} 个图形，分组 {Groups}", DrawnShapes, string.Join("、", DrawingGroups));
            }

            if (!_window.IsVisible)
            {
                _window.Show();
                _log.Information("遮罩窗口已显示：画面 {Rect}", rect);
            }

            PublishState(true, rect);
        }
        catch (Exception e)
        {
            _log.Debug(e, "更新遮罩窗口失败");
        }
    }

    private void UpdateWayland(BetterGenshinImpact.Platform.IPlatformWindow window, PixelRect rect)
    {
        if (_surfaceFailed) return;
        if (_surface is { Closed: true })
        {
            _surface.Dispose();
            _surface = null;
        }

        if (_surface is null)
        {
            _window ??= CreateWindow();
            try
            {
                _surface = WaylandMaskSurface.Create(_window);
                _surface.Pointer += OnOverlayPointer;
                _inputRegionKey = "";
                _log.Information("Wayland 遮罩：使用 layer-shell 覆盖层（输出 {Outputs}）", string.Join("、", _surface.OutputNames));
            }
            catch (Exception e) when (e is NotSupportedException or IOException or InvalidOperationException)
            {
                _surfaceFailed = true;
                _log.Warning("Wayland 遮罩不可用（合成器不支持 wlr-layer-shell 或无法连接 WAYLAND_DISPLAY）：{Error}；遮罩不会显示", e.Message);
                return;
            }
        }

        if (_wayland!.LocateOverlay(window) is not { } placement)
        {
            if (!_placementWarned)
            {
                _placementWarned = true;
                _log.Warning("Wayland 遮罩：拿不到游戏窗口在屏幕上的位置（niri 只报告浮动和全屏窗口的位置），遮罩暂不显示；请把游戏窗口设为浮动（Mod+Shift+V 或 window-rule open-floating）或全屏");
            }

            HideWayland();
            return;
        }

        _placementWarned = false;
        var wasShown = _surface.IsShown;
        _surface.Place(placement);
        _lastRect = rect;
        ApplyOverlay(_window!, placement.Width, placement.Height, rect, Math.Max(1, placement.Scale));
        _window!.ApplyMapPoints(_map, placement.Width, placement.Height);
        ApplyInputRegion(1); // 覆盖层的输入区域用逻辑坐标
        RefreshDrawing();
        if (!_drawingLogged && DrawnShapes > 0)
        {
            _drawingLogged = true;
            _log.Information("遮罩绘制：{Count} 个图形，分组 {Groups}", DrawnShapes, string.Join("、", DrawingGroups));
        }

        if (!wasShown)
        {
            _log.Information("遮罩已显示（Wayland 覆盖层）：输出 {Output} 位置 ({X},{Y}) 大小 {W}x{H} 缩放 {Scale}",
                placement.Output ?? "(合成器选择)", placement.X, placement.Y, placement.Width, placement.Height, placement.Scale);
        }
    }

    private void HideWayland()
    {
        if (_surface?.IsShown == true)
        {
            _surface.Hide();
            _log.Information("遮罩已隐藏（Wayland 覆盖层）");
        }
    }

    /// <summary>游戏画面相对 1080P 的比例：宽高比例取小（同 WPF MaskWindowViewModel.RecalculateScaleTo1080PRatio）</summary>
    private static double ScaleTo1080P(PixelRect rect) => rect.Width > 0 && rect.Height > 0 ? Math.Min(rect.Width / 1920d, rect.Height / 1080d) : 1;

    /// <summary>状态栏与日志框（WPF 遮罩的 StatusList 与日志 RichTextBox）</summary>
    private void ApplyOverlay(MaskWindow window, double width, double height, PixelRect rect, double dpiScale)
    {
        var all = _host.Config;
        IReadOnlyList<MaskStatusItem> status =
        [
            new("\uf256 拾取", all.AutoPickConfig.Enabled),
            new("\uf075 剧情", all.AutoSkipConfig.Enabled),
            new("\ue5c8 邀约", all.AutoSkipConfig.AutoHangoutEventEnabled),
            new("\uf578 钓鱼", all.AutoFishingConfig.Enabled),
            new("\uf3c5 传送", all.QuickTeleportConfig.Enabled),
        ];
        var logs = MaskLogSink.Instance;
        OverlayLayout = (width, height, ScaleTo1080P(rect), dpiScale);
        window.ApplyOverlay(all.MaskWindowConfig, width, height, ScaleTo1080P(rect), dpiScale, status,
            all.MaskWindowConfig.ShowLogBox ? logs.Snapshot() : null, logs.Version);
    }

    private void OnOpened()
    {
        if (_window?.TryGetPlatformHandle() is not { } handle)
        {
            return;
        }

        Handle = handle.Handle;
        if (!X11Overlay.HasCompositor(_display))
        {
            _log.Warning("没有检测到合成器（_NET_WM_CM_S0），遮罩的透明部分会显示为黑色并挡住游戏画面（Composite 窗口截图不受影响，屏幕区域截图会截到黑色）；请启用桌面合成（如 picom）或关闭遮罩");
        }

        _inputRegionKey = "";
        var error = X11Overlay.SetClickThrough(_display, Handle);
        if (error is null)
        {
            _log.Information("遮罩窗口 0x{Handle:x}：已设置点击穿透（SHAPE 输入区域为空）", Handle);
        }
        else
        {
            _log.Warning("遮罩窗口无法设置点击穿透：{Error}", error);
        }
    }

    private void Hide()
    {
        if (_wayland is not null)
        {
            HideWayland();
            return;
        }

        if (_window?.IsVisible == true)
        {
            _window.Hide();
            _log.Information("遮罩窗口已隐藏");
        }

        PublishState(false, _lastRect);
    }

    private BetterGenshinImpact.Core.Mask.MaskWindowState? _publishedState;

    /// <summary>
    /// 遮罩显示、隐藏或移动后通知订阅者（与 WPF 的 IMaskWindowHost.StateChanged 相同，悬浮小窗据此重新置顶到遮罩之上）
    /// </summary>
    private void PublishState(bool visible, PixelRect rect)
    {
        var state = new BetterGenshinImpact.Core.Mask.MaskWindowState(visible, new System.Drawing.Rectangle(rect.X, rect.Y, rect.Width, rect.Height));
        if (state == _publishedState)
        {
            return;
        }

        _publishedState = state;
        (_host.Services.GetService(typeof(BetterGenshinImpact.Core.Mask.MaskWindowStateSource)) as BetterGenshinImpact.Core.Mask.MaskWindowStateSource)?.Publish(state);
    }

    public void Dispose()
    {
        _host.MaskDrawing.Changed -= OnDrawingChanged;
        _host.Config.MaskWindowConfig.PropertyChanged -= OnMaskConfigChanged;
        MapImageCache.ImageUpdated -= OnMapImageUpdated;
        if (_mapSource is not null) _mapSource.Changed -= OnMapSnapshotChanged;
        if (_map is not null)
        {
            _map.PropertyChanged -= OnMapPropertyChanged;
            _map.PointInfoPopup.PropertyChanged -= OnMapPropertyChanged;
        }

        foreach (var c in _watchedMapCollections) c.CollectionChanged -= OnMapCollectionChanged;
        _watchedMapCollections.Clear();
        _pumpTimer.Stop();
        RecognitionObject.ForceDrawOnWindow = false;
        _timer.Stop();
        _focusTimer.Stop();
        _searchWindow?.CloseForGood();
        _searchWindow = null;
        _surface?.Dispose();
        _surface = null;
        _window?.Close();
        _window = null;
    }
}
