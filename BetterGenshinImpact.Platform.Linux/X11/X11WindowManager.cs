using static BetterGenshinImpact.Platform.Linux.X11.Native.Xlib;

namespace BetterGenshinImpact.Platform.Linux.X11;

/// <summary>窗口枚举：优先 EWMH _NET_CLIENT_LIST，没有窗口管理器时回退 XQueryTree（取带 WM_STATE 的窗口）</summary>
public sealed unsafe class X11WindowManager : IWindowManager
{
    private readonly X11Connection _c;
    private readonly bool _owns;

    public X11WindowManager(X11Connection? c = null)
    {
        _owns = c == null;
        _c = c ?? new X11Connection();
    }

    /// <summary>最近一次枚举用的方法，用于诊断</summary>
    public string LastMethod { get; private set; } = "";

    public IReadOnlyList<IPlatformWindow> EnumerateWindows()
    {
        var list = _c.GetCardinals(_c.Root, "_NET_CLIENT_LIST", XA_WINDOW);
        if (list.Length > 0)
        {
            LastMethod = "_NET_CLIENT_LIST";
            return list.Select(x => (IPlatformWindow)new X11Window(_c, (nint)x)).ToList();
        }

        var noWm = !_c.HasWindowManager;
        LastMethod = noWm ? "XQueryTree（无窗口管理器）" : "XQueryTree";
        var result = new List<IPlatformWindow>();
        Walk(_c.Root, 0, result, noWm);
        return result;
    }

    /// <summary>已映射（IsViewable）且有 WM_CLASS 的窗口</summary>
    private bool IsViewableClient(nint w)
    {
        if (_c.GetProperty(w, "WM_CLASS", XA_STRING, out _, out _) == null) return false;
        var attrs = stackalloc byte[256];
        lock (_c.Gate)
        {
            LastErrorCode = 0;
            if (XGetWindowAttributes(_c.Display, w, attrs) == 0 || LastErrorCode != 0) return false;
        }

        return *(int*)(attrs + MapStateOffset) == IsViewable;
    }

    private void Walk(nint w, int depth, List<IPlatformWindow> acc, bool noWm)
    {
        nint children;
        uint n;
        lock (_c.Gate)
        {
            if (XQueryTree(_c.Display, w, out _, out _, out children, out n) == 0) return;
        }

        try
        {
            for (int i = 0; i < n; i++)
            {
                var child = ((nint*)children)[i];
                if (_c.GetProperty(child, "WM_STATE", _c.Atom("WM_STATE"), out _, out _) != null) acc.Add(new X11Window(_c, child));
                // 没有窗口管理器时没有 WM_STATE：根窗口下已映射、设置了 WM_CLASS 的顶层窗口就是客户端窗口
                else if (depth == 0 && noWm && IsViewableClient(child)) acc.Add(new X11Window(_c, child));
                else if (depth < 3) Walk(child, depth + 1, acc, noWm);
            }
        }
        finally
        {
            if (children != 0) XFree(children);
        }
    }

    public IReadOnlyList<IPlatformWindow> FindWindows(WindowQuery query) =>
        EnumerateWindows().Cast<X11Window>()
            .Where(w => query.Matches(w.Title, w.ClassName, w.ProcessId, query.ProcessName == null ? null : w.ProcessName))
            .Cast<IPlatformWindow>().ToList();

    public IPlatformWindow? GetForegroundWindow()
    {
        var wm = _c.HasWindowManager;
        var a = wm ? _c.ActiveWindow : 0;
        if (a != 0) return new X11Window(_c, a);
        var f = _c.FocusWindow;
        if (f == 0) return null;
        // 没有窗口管理器时顶层窗口就是客户端窗口（焦点可能在它的子窗口上）
        return new X11Window(_c, wm ? f : _c.TopLevelOf(f) is var top and not 0 ? top : f);
    }

    public IPlatformWindow? FromHandle(nint handle) => handle == 0 ? null : new X11Window(_c, handle);

    public void Dispose()
    {
        if (_owns) _c.Dispose();
    }
}
