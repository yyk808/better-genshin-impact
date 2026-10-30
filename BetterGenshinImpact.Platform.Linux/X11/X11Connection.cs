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
