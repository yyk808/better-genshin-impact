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

    /// <param name="display">X 显示名，为 null 时使用 $DISPLAY</param>
    public X11PlatformServices(string? display = null)
    {
        _display = display;
        _wm = new(() => new X11WindowManager(new X11Connection(display)));
        _hotkeys = new(() => new X11HotkeyService(display));
    }

    public string Name => "X11";

    public IWindowManager Windows => _wm.Value;

    public IHotkeyService Hotkeys => _hotkeys.Value;

    /// <summary>按进程名 / WM_CLASS 在 _NET_CLIENT_LIST 中查找游戏窗口</summary>
    public IGameWindowFinder GameWindows => new WindowManagerGameWindowFinder(Windows);

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
        if (_wm.IsValueCreated) _wm.Value.Dispose();
    }
}
