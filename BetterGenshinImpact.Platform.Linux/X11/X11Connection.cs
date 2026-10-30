using System.Runtime.InteropServices;
using System.Text;
using BetterGenshinImpact.Platform.Linux.X11.Native;
using static BetterGenshinImpact.Platform.Linux.X11.Native.Xlib;

namespace BetterGenshinImpact.Platform.Linux.X11;

/// <summary>
/// 一个 Display 连接 + 常用原子/属性读取工具。Xlib 连接不保证跨线程安全，
/// 因此每个服务（窗口管理/截图/输入/热键线程）各自持有一个连接，并用 lock 串行化。
/// </summary>
public sealed unsafe class X11Connection : IDisposable
{
    public nint Display { get; }
    public int Screen { get; }
    public nint Root { get; }
    public string Name { get; }
    public readonly object Gate = new();
    readonly Dictionary<string, nint> _atoms = new();

    public X11Connection(string? displayName = null)
    {
        EnsureInit();
        Name = displayName ?? Environment.GetEnvironmentVariable("DISPLAY") ?? ":0";
        Display = XOpenDisplay(Name);
        if (Display == 0) throw new InvalidOperationException($"无法连接 X 显示 {Name}");
        Screen = XDefaultScreen(Display);
        Root = XRootWindow(Display, Screen);
    }

    public nint Atom(string name)
    {
        lock (_atoms)
        {
            if (!_atoms.TryGetValue(name, out var a)) _atoms[name] = a = XInternAtom(Display, name, false);
            return a;
        }
    }

    /// <summary>读取属性原始字节。注意 format=32 时每项是 C long（8 字节）。</summary>
    public byte[]? GetProperty(nint window, string prop, nint type, out int format, out long nitems)
    {
        format = 0; nitems = 0;
        lock (Gate)
        {
            LastErrorCode = 0;
            int r = XGetWindowProperty(Display, window, Atom(prop), 0, 1 << 20, false, type, out var actualType, out format, out var n, out _, out var data);
            if (r != 0 || actualType == 0 || data == 0) { if (data != 0) XFree(data); return null; }
            nitems = n;
            int unit = format == 32 ? sizeof(nint) : format / 8;
            var bytes = new byte[n * unit];
            Marshal.Copy(data, bytes, 0, bytes.Length);
            XFree(data);
            return bytes;
        }
    }

    public long[] GetCardinals(nint window, string prop, nint type)
    {
        var b = GetProperty(window, prop, type, out var fmt, out var n);
        if (b == null || fmt != 32) return Array.Empty<long>();
        var r = new long[n];
        for (int i = 0; i < n; i++) r[i] = BitConverter.ToInt64(b, i * 8);
        return r;
    }

    public string? GetString(nint window, string prop, bool utf8)
    {
        var b = GetProperty(window, prop, utf8 ? Atom("UTF8_STRING") : AnyPropertyType, out var fmt, out _);
        if (b == null || fmt != 8) return null;
        return Encoding.UTF8.GetString(b).TrimEnd('\0');
    }

    /// <summary>
    /// 是否有遵循 EWMH 的窗口管理器：根窗口的 _NET_SUPPORTING_WM_CHECK 指向的窗口上同名属性指向自己
    /// （窗口管理器退出后根窗口上的属性可能残留，此时那个窗口已不存在）
    /// </summary>
    public bool HasWindowManager
    {
        get
        {
            var check = GetCardinals(Root, "_NET_SUPPORTING_WM_CHECK", XA_WINDOW).FirstOrDefault();
            return check != 0 && GetCardinals((nint)check, "_NET_SUPPORTING_WM_CHECK", XA_WINDOW).FirstOrDefault() == check;
        }
    }

    /// <summary>EWMH 当前活动窗口（客户端窗口），没有时为 0</summary>
    public nint ActiveWindow => (nint)GetCardinals(Root, "_NET_ACTIVE_WINDOW", XA_WINDOW).FirstOrDefault();

    /// <summary>键盘焦点所在窗口（XGetInputFocus）；None / PointerRoot 时为 0</summary>
    public nint FocusWindow
    {
        get
        {
            lock (Gate)
            {
                XGetInputFocus(Display, out var f, out _);
                return f > 1 ? f : 0;
            }
        }
    }

    /// <summary>沿父窗口上溯到根窗口的直接子窗口（顶层窗口；有窗口管理器时通常是 frame）；窗口不存在时返回 0</summary>
    public nint TopLevelOf(nint window)
    {
        lock (Gate)
        {
            for (var depth = 0; window != 0 && depth < 64; depth++)
            {
                LastErrorCode = 0;
                if (XQueryTree(Display, window, out _, out var parent, out var children, out _) == 0 || LastErrorCode != 0)
                {
                    return 0;
                }

                if (children != 0) XFree(children);
                if (parent == 0 || parent == Root) return window;
                window = parent;
            }

            return 0;
        }
    }

    /// <summary>
    /// 窗口是否在前台。有窗口管理器时以 _NET_ACTIVE_WINDOW 为准（与窗口同一顶层也算，如 Wine 的子窗口）；
    /// 没有窗口管理器（或它没有设置活动窗口）时用 XGetInputFocus，焦点窗口与该窗口上溯到同一个顶层窗口即为前台
    /// </summary>
    public bool IsForegroundWindow(nint window)
    {
        if (window == 0) return false;
        if (HasWindowManager)
        {
            var active = ActiveWindow;
            if (active != 0)
            {
                return active == window || TopLevelOf(active) == TopLevelOf(window);
            }
        }

        var focus = FocusWindow;
        return focus != 0 && (focus == window || TopLevelOf(focus) == TopLevelOf(window));
    }

    /// <summary>连接已关闭。持有 <see cref="Gate"/> 时检查，关闭后不能再用 <see cref="Display"/> 调用 Xlib</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>关闭连接。可重复调用：运行环境解绑时输入后端可能被释放两次，第二次不能再关同一个 Display</summary>
    public void Dispose()
    {
        lock (Gate)
        {
            if (IsDisposed) return;
            IsDisposed = true;
            XCloseDisplay(Display);
        }
    }
}
