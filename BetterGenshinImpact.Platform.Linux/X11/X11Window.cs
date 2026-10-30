using static BetterGenshinImpact.Platform.Linux.X11.Native.Xlib;

namespace BetterGenshinImpact.Platform.Linux.X11;

/// <summary>
/// X11 顶层窗口（_NET_CLIENT_LIST 中的客户端窗口）。<see cref="Handle"/> 为 XID。
/// 所有成员都是实时查询，共享所属 <see cref="X11Connection"/> 的锁，可在任意线程调用。
/// Wine 窗口的 <see cref="ClassName"/> 是 WM_CLASS（小写 exe 名，如 "yuanshen.exe/YuanShen.exe"），不是 Win32 窗口类名
/// </summary>
public sealed unsafe class X11Window : IPlatformWindow
{
    private readonly X11Connection _c;

    public X11Window(X11Connection c, nint xid)
    {
        _c = c;
        Handle = xid;
    }

    public nint Handle { get; }

    public int ProcessId => (int)_c.GetCardinals(Handle, "_NET_WM_PID", XA_CARDINAL).FirstOrDefault();

    public string Title => _c.GetString(Handle, "_NET_WM_NAME", true) ?? _c.GetString(Handle, "WM_NAME", false) ?? "";

    /// <summary>WM_CLASS 的 "instance/class"</summary>
    public string ClassName
    {
        get
        {
            var b = _c.GetProperty(Handle, "WM_CLASS", XA_STRING, out _, out _);
            if (b == null) return "";
            var parts = System.Text.Encoding.UTF8.GetString(b).Split('\0', StringSplitOptions.RemoveEmptyEntries);
            return string.Join("/", parts);
        }
    }

    /// <summary>进程名（/proc/pid/cmdline）。Wine 进程取 cmdline 中的 exe 文件名</summary>
    public string ProcessName
    {
        get
        {
            try
            {
                var pid = ProcessId;
                if (pid <= 0) return "";
                var cmd = File.ReadAllText($"/proc/{pid}/cmdline").Split('\0', StringSplitOptions.RemoveEmptyEntries);
                var first = cmd.FirstOrDefault(a => a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) ?? cmd.FirstOrDefault() ?? "";
                return first.Replace('\\', '/').Split('/').Last();
            }
            catch
            {
                return "";
            }
        }
    }

    /// <summary>客户区在根窗口上的位置与尺寸。X11 没有按窗口的 DPI 缩放，固定为 1</summary>
    public GameViewport Viewport
    {
        get
        {
            lock (_c.Gate)
            {
                LastErrorCode = 0;
                if (XGetGeometry(_c.Display, Handle, out _, out _, out _, out var w, out var h, out _, out _) == 0) return default;
                XTranslateCoordinates(_c.Display, Handle, _c.Root, 0, 0, out var x, out var y, out _);
                return new GameViewport(new PixelRect(x, y, (int)w, (int)h), 1f);
            }
        }
    }

    public bool IsAlive
    {
        get
        {
            lock (_c.Gate)
            {
                LastErrorCode = 0;
                var ok = XGetGeometry(_c.Display, Handle, out _, out _, out _, out _, out _, out _, out _) != 0;
                return ok && LastErrorCode == 0;
            }
        }
    }

    public bool IsForeground => _c.GetCardinals(_c.Root, "_NET_ACTIVE_WINDOW", XA_WINDOW).FirstOrDefault() == Handle;

    public bool IsMinimized
    {
        get
        {
            var hidden = _c.Atom("_NET_WM_STATE_HIDDEN");
            if (_c.GetCardinals(Handle, "_NET_WM_STATE", XA_ATOM).Any(a => a == hidden)) return true;
            var ws = _c.GetCardinals(Handle, "WM_STATE", _c.Atom("WM_STATE"));
            return ws.Length > 0 && ws[0] == 3; // IconicState
        }
    }

    /// <summary>XTest 输入发往焦点窗口，因此总是需要前台</summary>
    public bool RequiresForeground => true;

    public void Activate() => ActivateAndWait();

    /// <summary>
    /// 还原并置前，等待成为前台。EWMH _NET_ACTIVE_WINDOW（source=2，WM 一般无条件执行），超时后回退 XSetInputFocus
    /// </summary>
    /// <returns>超时前是否确认成为前台</returns>
    public bool ActivateAndWait(TimeSpan? timeout = null)
    {
        lock (_c.Gate)
        {
            XMapRaised(_c.Display, Handle);
            SendRootClientMessage("_NET_ACTIVE_WINDOW", 2, 0);
        }

        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
        while (DateTime.UtcNow < deadline)
        {
            if (IsForeground) return true;
            Thread.Sleep(20);
        }

        lock (_c.Gate)
        {
            XSetInputFocus(_c.Display, Handle, 2 /*RevertToParent*/, 0);
            XFlush(_c.Display);
        }

        Thread.Sleep(50);
        return IsForeground;
    }

    /// <summary>请求关闭（EWMH _NET_CLOSE_WINDOW，相当于 WM_CLOSE）</summary>
    public void RequestClose()
    {
        lock (_c.Gate) SendRootClientMessage("_NET_CLOSE_WINDOW", 0, 2);
    }

    /// <summary>向根窗口发送针对本窗口的 EWMH 客户端消息，调用方需持有锁</summary>
    private void SendRootClientMessage(string atom, nint l0, nint l1)
    {
        var ev = stackalloc byte[192];
        new Span<byte>(ev, 192).Clear();
        *(int*)(ev + 0) = ClientMessage;
        *(int*)(ev + 16) = 1; // send_event
        *(nint*)(ev + 24) = _c.Display;
        *(nint*)(ev + 32) = Handle; // window
        *(nint*)(ev + 40) = _c.Atom(atom); // message_type
        *(int*)(ev + 48) = 32; // format
        *(nint*)(ev + 56) = l0; // data.l[0]
        *(nint*)(ev + 64) = l1; // data.l[1]
        XSendEvent(_c.Display, _c.Root, false, (nint)(SubstructureRedirectMask | SubstructureNotifyMask), ev);
        XFlush(_c.Display);
    }

    /// <summary>尚未实现（可订阅 ConfigureNotify）。调用方目前按需轮询 <see cref="Viewport"/></summary>
    public event EventHandler? ViewportChanged
    {
        add { }
        remove { }
    }

    public override string ToString()
    {
        var v = Viewport.ScreenRect;
        return $"0x{Handle:x} pid={ProcessId} proc={ProcessName} class={ClassName} title=\"{Title}\" rect={v.X},{v.Y} {v.Width}x{v.Height}";
    }

    public void Dispose()
    {
    }
}
