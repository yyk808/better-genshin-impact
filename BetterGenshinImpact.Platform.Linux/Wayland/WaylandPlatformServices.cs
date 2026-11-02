using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Platform.Linux.X11;

namespace BetterGenshinImpact.Platform.Linux.Wayland;

/// <summary>
/// Wayland 会话（首要目标 niri）的平台服务。
/// <list type="bullet">
/// <item>游戏（Wine）是 X 程序，经 xwayland-satellite / XWayland 运行：找窗、截图（GetImageWindow / CompositeWindow，无根 XWayland 的根窗口没有画面）、
/// XTest 输入都复用 X11 实现，在 X 服务器内部完成，不经过合成器；</item>
/// <item>窗口置前、前台判断、屏幕位置用 niri IPC（$NIRI_SOCKET）；</item>
/// <item>全局热键与系统键状态读 evdev（/dev/input，需要 input 组），不依赖合成器；</item>
/// <item>原生 Wayland 窗口：截图走 xdg-desktop-portal ScreenCast + PipeWire（method = "Portal"），输入走 uinput。</item>
/// </list>
/// </summary>
public sealed class WaylandPlatformServices : IPlatformServices
{
    private readonly string? _display;
    private readonly X11PlatformServices? _x11;
    private readonly string? _x11Problem;
    private readonly Lazy<WaylandWindowManager> _wm;
    private readonly Lazy<(EvdevGlobalInputHook? Hook, string? Problem)> _evdev;
    private readonly Lazy<IHotkeyService> _hotkeys;

    public WaylandPlatformServices(string? display = null, string? winePrefix = null)
    {
        _display = display;
        Niri = NiriIpc.FromEnvironment();
        if (!string.IsNullOrEmpty(display ?? Environment.GetEnvironmentVariable("DISPLAY")))
        {
            try
            {
                // 先试连一次：xwayland-satellite 没启动时 DISPLAY 可能指向不存在的显示
                using (new X11Connection(display))
                {
                }

                _x11 = new X11PlatformServices(display, winePrefix);
            }
            catch (InvalidOperationException e)
            {
                _x11Problem = e.Message;
            }
        }
        else
        {
            _x11Problem = "没有 DISPLAY：未运行 xwayland-satellite / XWayland，Wine 游戏窗口无法操作";
        }

        UserRegistry = _x11?.UserRegistry ?? new WinePrefixUserRegistry(winePrefix);
        _wm = new(() => new WaylandWindowManager(_x11?.Windows as X11WindowManager, Niri));
        _evdev = new(() =>
        {
            try
            {
                return (new EvdevGlobalInputHook(CursorPosition), null);
            }
            catch (InvalidOperationException e)
            {
                return (null, e.Message);
            }
        });
        _hotkeys = new(() => _evdev.Value.Hook is { } hook
            ? new GlobalInputHotkeyService(hook)
            : _x11?.Hotkeys ?? new GlobalInputHotkeyService(new NullGlobalInputHook()));
    }

    /// <summary>niri IPC；不是 niri 时为 null</summary>
    public NiriIpc? Niri { get; }

    /// <summary>X 显示（xwayland-satellite / XWayland）上的 X11 服务；没有 X 显示时为 null</summary>
    public X11PlatformServices? X11 => _x11;

    /// <summary>evdev 不可用的原因（热键退回 X11，只在 X 窗口有焦点时生效）；可用时为 null</summary>
    public string? HotkeyProblem => _evdev.Value.Problem;

    public string Name => Niri is not null ? "Wayland (niri)" : "Wayland";

    public IWindowManager Windows => _wm.Value;

    /// <summary>evdev 全局热键；没有 input 组权限时退回 X11 XGrabKey（只在 X 窗口有焦点时收到）</summary>
    public IHotkeyService Hotkeys => _hotkeys.Value;

    public IGameWindowFinder GameWindows => new WindowManagerGameWindowFinder(Windows);

    public IUserRegistry UserRegistry { get; }

    public IKeyStateProvider KeyState => (IKeyStateProvider?)_evdev.Value.Hook ?? _x11?.KeyState ?? NullKeyState.Instance;

    /// <summary>X 显示的大小（游戏坐标系）；没有 X 时取 niri 第一个输出的物理大小</summary>
    public IScreenInfo Screen => _x11?.Screen ?? new NiriScreenInfo(Niri);

    private PixelPoint? CursorPosition() => _x11?.KeyState.GetCursorPosition();

    /// <summary>全部输出的外接矩形（合成器全局物理像素），uinput 绝对定位用</summary>
    public PixelRect LayoutBounds()
    {
        try
        {
            var outputs = Niri?.Outputs() ?? [];
            if (outputs.Count == 0) return default;
            var x1 = outputs.Min(o => (int)Math.Round(o.X * o.Scale));
            var y1 = outputs.Min(o => (int)Math.Round(o.Y * o.Scale));
            var x2 = outputs.Max(o => (int)Math.Round((o.X + o.Width) * o.Scale));
            var y2 = outputs.Max(o => (int)Math.Round((o.Y + o.Height) * o.Scale));
            return new PixelRect(x1, y1, x2 - x1, y2 - y1);
        }
        catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or InvalidOperationException or System.Text.Json.JsonException)
        {
            return default;
        }
    }

    public IGlobalInputHook CreateGlobalInputHook()
    {
        try
        {
            return new EvdevGlobalInputHook(CursorPosition);
        }
        catch (InvalidOperationException)
        {
            return _x11?.CreateGlobalInputHook() ?? new NullGlobalInputHook();
        }
    }

    /// <summary>
    /// "Portal"：ScreenCast portal + PipeWire（原生 Wayland 窗口或整个输出，首次需要用户在对话框里授权）；
    /// 其它：X 窗口截图。无根 XWayland 的根窗口没有画面，ShmRoot / GetImageRoot 改为 GetImageWindow
    /// </summary>
    public IGameCapture CreateCapture(string? method = null)
    {
        if (string.Equals(method, PortalScreenCastCapture.MethodName, StringComparison.OrdinalIgnoreCase) || _x11 is null)
        {
            return new PortalScreenCastCapture(Niri);
        }

        return new XWaylandCapture(_x11, MapCaptureMode(method));
    }

    /// <summary>X11 截图方式在 Wayland 会话下的实际方式</summary>
    public static string MapCaptureMode(string? method) =>
        method is null || method.Equals("ShmRoot", StringComparison.OrdinalIgnoreCase) || method.Equals("GetImageRoot", StringComparison.OrdinalIgnoreCase)
            ? nameof(X11CaptureMode.GetImageWindow)
            : method;

    /// <summary>X 窗口用 XTest（在 X 服务器内注入，不需要 uinput 权限）；原生 Wayland 窗口用 uinput</summary>
    public IInputBackend CreateInput(IPlatformWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window is XWaylandWindow xw && _x11 is not null) return _x11.CreateInput(xw.Inner);
        if (window is X11Window x && _x11 is not null) return _x11.CreateInput(x);
        return new UinputInputBackend(LayoutBounds);
    }

    public PlatformProbeResult Probe()
    {
        var d = new Dictionary<string, string>
        {
            ["WAYLAND_DISPLAY"] = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "",
            ["XDG_SESSION_TYPE"] = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "",
            ["XDG_CURRENT_DESKTOP"] = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "",
            ["compositor"] = Niri is not null ? "niri" : "(未知，无 niri IPC)",
        };
        if (Niri is not null)
        {
            try
            {
                d["niri.outputs"] = string.Join(";", Niri.Outputs().Select(o => $"{o.Name} {o.Width}x{o.Height}+{o.X}+{o.Y}@{o.Scale}"));
                d["niri.windows"] = Niri.Windows().Count.ToString();
            }
            catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or InvalidOperationException or System.Text.Json.JsonException)
            {
                d["niri.error"] = e.Message;
            }
        }

        if (_x11 is not null)
        {
            foreach (var (k, v) in _x11.Probe().Items) d["x11." + k] = v;
        }
        else
        {
            d["x11"] = _x11Problem ?? "无";
        }

        var devices = Evdev.Enumerate(out var evProblem);
        d["evdev"] = evProblem ?? $"{devices.Count(x => x.IsKeyboard)} 键盘 / {devices.Count(x => x.IsMouse)} 鼠标";
        d["uinput"] = UinputDevice.IsAvailable(out var uProblem) ? "可写" : "不可用";
        if (uProblem is not null) d["uinput.hint"] = uProblem;
        d["portal.screencast"] = PortalScreenCastCapture.ProbeAvailability();
        return new PlatformProbeResult(PlatformKind.LinuxWayland, d);
    }

    public void Dispose()
    {
        if (_hotkeys.IsValueCreated && !ReferenceEquals(_hotkeys.Value, _x11?.Hotkeys)) _hotkeys.Value.Dispose();
        if (_evdev.IsValueCreated) _evdev.Value.Hook?.Dispose();
        if (_wm.IsValueCreated) _wm.Value.Dispose();
        _x11?.Dispose();
    }
}

/// <summary>niri 第一个输出的物理大小</summary>
internal sealed class NiriScreenInfo(NiriIpc? niri) : IScreenInfo
{
    public PixelSize PrimaryScreenSize
    {
        get
        {
            try
            {
                var o = niri?.Outputs().FirstOrDefault();
                return o is null ? default : new PixelSize((int)Math.Round(o.Width * o.Scale), (int)Math.Round(o.Height * o.Scale));
            }
            catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or InvalidOperationException or System.Text.Json.JsonException)
            {
                return default;
            }
        }
    }
}

/// <summary>没有任何输入监听手段时的占位（不触发事件）</summary>
internal sealed class NullGlobalInputHook : IGlobalInputHook
{
#pragma warning disable CS0067
    public event EventHandler<GlobalKeyEventArgs>? KeyDown;
    public event EventHandler<GlobalKeyEventArgs>? KeyUp;
    public event EventHandler<GlobalMouseEventArgs>? MouseDown;
    public event EventHandler<GlobalMouseEventArgs>? MouseUp;
    public event EventHandler<GlobalMouseEventArgs>? MouseMove;
    public event EventHandler<GlobalMouseEventArgs>? MouseWheel;
#pragma warning restore CS0067
    public bool SupportsWheel => false;

    public void Dispose()
    {
    }
}

internal sealed class NullKeyState : IKeyStateProvider
{
    public static NullKeyState Instance { get; } = new();
    public bool IsKeyDown(GameKey key) => false;
    public IReadOnlySet<GameKey> GetPressedKeys() => new HashSet<GameKey>();
    public PixelPoint? GetCursorPosition() => null;
}

/// <summary>Wayland 会话里的 X 窗口截图：把根窗口方式换成窗口方式，并接受 <see cref="XWaylandWindow"/></summary>
internal sealed class XWaylandCapture(X11PlatformServices x11, string mode) : IGameCapture
{
    private readonly IGameCapture _inner = x11.CreateCapture(mode);

    public bool IsCapturing => _inner.IsCapturing;
    public CaptureCapabilities Capabilities => _inner.Capabilities;

    public void Start(IPlatformWindow window, Dictionary<string, object>? settings = null)
    {
        if (settings is not null)
        {
            settings = new Dictionary<string, object>(settings);
            foreach (var key in new[] { "x11CaptureMode", "mode" })
            {
                if (settings.TryGetValue(key, out var v)) settings[key] = WaylandPlatformServices.MapCaptureMode(v?.ToString());
            }
        }

        _inner.Start(window is XWaylandWindow xw ? xw.Inner : window, settings);
    }

    public GameCaptureFrame? Capture() => _inner.Capture();

    public void Stop() => _inner.Stop();

    public void Dispose() => _inner.Dispose();
}
