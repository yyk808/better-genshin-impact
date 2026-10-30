using BetterGenshinImpact.Core.Input;
using static BetterGenshinImpact.Platform.Linux.X11.Native.Xlib;

namespace BetterGenshinImpact.Platform.Linux.X11;

/// <summary>
/// X11 / XWayland 平台服务集合。Xlib 连接不保证跨线程安全，窗口管理、每个截图器、每个输入后端、热键线程各自持有独立连接
/// </summary>
public sealed class X11PlatformServices : IPlatformServices
{
    private readonly string? _display;
    private readonly Lazy<X11WindowManager> _wm;
    private readonly Lazy<X11HotkeyService> _hotkeys;
    private readonly Lazy<X11KeyStateProvider> _keyState;

    /// <param name="display">X 显示名，为 null 时使用 $DISPLAY</param>
    /// <param name="winePrefix">游戏所在的 Wine 前缀，为 null 时取 $WINEPREFIX，再退回 ~/.wine</param>
    public X11PlatformServices(string? display = null, string? winePrefix = null)
    {
        _display = display;
        UserRegistry = new WinePrefixUserRegistry(winePrefix);
        _wm = new(() => new X11WindowManager(new X11Connection(display)));
        _hotkeys = new(() => new X11HotkeyService(display));
        _keyState = new(() => new X11KeyStateProvider(new X11Connection(display), ownsConnection: true));
    }

    public string Name => "X11";

    public IWindowManager Windows => _wm.Value;

    public IHotkeyService Hotkeys => _hotkeys.Value;

    /// <summary>按进程名 / WM_CLASS 在 _NET_CLIENT_LIST 中查找游戏窗口</summary>
    public IGameWindowFinder GameWindows => new WindowManagerGameWindowFinder(Windows);

    /// <summary>Wine 前缀 user.reg 中的当前用户注册表（只读）</summary>
    public IUserRegistry UserRegistry { get; }

    /// <summary>XQueryKeymap / XQueryPointer（独立连接，线程安全）</summary>
    public IKeyStateProvider KeyState => _keyState.Value;

    /// <summary>默认屏幕的大小（每次新建连接读取，分辨率变化后立即生效）</summary>
    public IScreenInfo Screen => new X11ScreenInfo(_display);

    /// <summary>
    /// 全局键鼠监听（不抓取，游戏照常收到按键）：XInput2 原始事件（<see cref="X11RawInputHook"/>，忽略本进程的 XTest 注入）；
    /// X 服务器不支持 XInput 2.1 时退回轮询键状态（没有滚轮）
    /// </summary>
    public IGlobalInputHook CreateGlobalInputHook()
    {
        try
        {
            return new X11RawInputHook(_display);
        }
        catch (InvalidOperationException)
        {
            return new PollingGlobalInputHook(new X11KeyStateProvider(new X11Connection(_display), ownsConnection: true), ownsState: true);
        }
    }

    /// <summary>新建 XInput2 原始鼠标移动监听（首个订阅时建立独立连接）</summary>
    public X11RelativeMouseInputMonitor CreateRelativeMouseMonitor() => new(_display);

    /// <summary>新建截图器（独立连接，Dispose 时关闭）</summary>
    /// <param name="method"><see cref="X11CaptureMode"/> 名称，null 或无法识别时为 ShmRoot</param>
    public IGameCapture CreateCapture(string? method = null)
    {
        var mode = method != null && Enum.TryParse<X11CaptureMode>(method, true, out var m) ? m : X11CaptureMode.ShmRoot;
        return new X11Capture(new X11Connection(_display), ownsConnection: true, mode);
    }

    /// <summary>
    /// 新建 XTest 输入后端（独立连接，Dispose 时关闭）。
    /// Wine 窗口（WM_CLASS 含 .exe）的绝对移动会被游戏当成视角转动，据此设置 <see cref="InputCapabilities.AbsoluteMoveAffectsCamera"/>
    /// </summary>
    public IInputBackend CreateInput(IPlatformWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var isWine = window.ClassName.Contains(".exe", StringComparison.OrdinalIgnoreCase);
        return new XTestInputBackend(new X11Connection(_display), ownsConnection: true, absoluteMoveAffectsCamera: isWine);
    }

    public PlatformProbeResult Probe()
    {
        var d = new Dictionary<string, string>();
        using var c = new X11Connection(_display);
        d["DISPLAY"] = c.Name;
        d["WAYLAND_DISPLAY"] = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "";
        d["XDG_SESSION_TYPE"] = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "";
        d["screen"] = $"{XDisplayWidth(c.Display, c.Screen)}x{XDisplayHeight(c.Display, c.Screen)} depth={XDefaultDepth(c.Display, c.Screen)}";
        foreach (var ext in new[] { "MIT-SHM", "XTEST", "XInputExtension", "Composite", "RECORD", "XFIXES", "DAMAGE" })
        {
            d["ext." + ext] = XQueryExtension(c.Display, ext, out _, out _, out _) ? "yes" : "no";
        }

        var wmCheck = c.GetCardinals(c.Root, "_NET_SUPPORTING_WM_CHECK", XA_WINDOW).FirstOrDefault();
        d["wm"] = wmCheck != 0 ? c.GetString((nint)wmCheck, "_NET_WM_NAME", true) ?? "?" : "(none)";
        d["/dev/uinput"] = File.Exists("/dev/uinput") ? "present" : "absent";
        // 走的是 X11 协议：纯 X11 会话或 Wayland 会话里的 XWayland 都按 LinuxX11 处理（只能操作 X11 / XWayland 窗口）
        d["xwayland"] = string.IsNullOrEmpty(d["WAYLAND_DISPLAY"]) ? "no" : "yes";
        return new PlatformProbeResult(PlatformKind.LinuxX11, d);
    }

    public void Dispose()
    {
        if (_hotkeys.IsValueCreated) _hotkeys.Value.Dispose();
        if (_keyState.IsValueCreated) _keyState.Value.Dispose();
        if (_wm.IsValueCreated) _wm.Value.Dispose();
    }
}
