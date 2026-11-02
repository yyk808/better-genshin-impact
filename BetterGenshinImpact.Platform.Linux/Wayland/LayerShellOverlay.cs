using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace BetterGenshinImpact.Platform.Linux.Wayland;

/// <summary>遮罩上画的一个矩形（相对覆盖层左上角的像素坐标，颜色为 0xAARRGGBB）</summary>
public readonly record struct OverlayRect(int X, int Y, int Width, int Height, uint Argb, int Thickness = 2, bool Fill = false);

/// <summary>
/// wlr-layer-shell 透明覆盖层的最小实现：直接按 Wayland 线协议与合成器通信（不依赖 libwayland / GTK），
/// overlay 层 + 空输入区域（鼠标点击穿透到下面的游戏）+ 不接收键盘，wl_shm（ARGB8888 预乘）绘制矩形。
/// Avalonia 没有 layer-shell 支持，X11 遮罩窗口经 xwayland-satellite 会变成普通平铺窗口，所以 Wayland 下用这个代替。
/// 目前只画矩形（识别框、边框），不画文字；只在合成器选择的输出（通常是有焦点的输出）上显示
/// </summary>
public sealed class LayerShellOverlay : IDisposable
{
    private const uint DisplayId = 1;
    private readonly int _fd;
    private uint _nextId = 2;
    private readonly Dictionary<string, (uint Name, uint Version)> _globals = new();
    private readonly byte[] _rx = new byte[65536];
    private int _rxLen;
    private uint _compositor, _shm, _layerShell, _surface, _layerSurface, _pool;
    private readonly uint[] _buffers = new uint[2];
    private readonly bool[] _busy = new bool[2];
    private int _current;
    private IntPtr _map = IntPtr.Zero;
    private int _poolFd = -1;
    private long _poolSize;
    private int _width, _height;
    private uint? _configureSerial;
    private bool _configured;
    private uint _syncId;
    private bool _syncDone;

    /// <summary>合成器关闭了覆盖层（例如输出被移除）</summary>
    public bool Closed { get; private set; }

    public int Width => _width;
    public int Height => _height;

    private LayerShellOverlay(int fd) => _fd = fd;

    /// <summary>连接 $WAYLAND_DISPLAY；合成器不支持 zwlr_layer_shell_v1 时抛出 <see cref="NotSupportedException"/></summary>
    public static LayerShellOverlay Connect(string? display = null)
    {
        display ??= Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "wayland-0";
        var path = display.StartsWith('/') ? display
            : Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? throw new InvalidOperationException("没有 XDG_RUNTIME_DIR"), display);
        var fd = Native.socket(1 /*AF_UNIX*/, 1 /*SOCK_STREAM*/ | 0x80000 /*CLOEXEC*/, 0);
        if (fd < 0) throw new IOException($"socket 失败 errno={Marshal.GetLastWin32Error()}");
        var addr = new byte[110];
        BinaryPrimitives.WriteUInt16LittleEndian(addr, 1);
        Encoding.UTF8.GetBytes(path).CopyTo(addr, 2);
        if (Native.connect(fd, addr, addr.Length) != 0)
        {
            var errno = Marshal.GetLastWin32Error();
            Native.close(fd);
            throw new IOException($"连接 Wayland 显示 {path} 失败 errno={errno}");
        }

        var o = new LayerShellOverlay(fd);
        try
        {
            o.Send(DisplayId, 1, w => w.NewId(2)); // wl_display.get_registry
            o._nextId = 3;
            o.Roundtrip();
            o._compositor = o.Bind("wl_compositor", 4);
            o._shm = o.Bind("wl_shm", 1);
            o._layerShell = o.Bind("zwlr_layer_shell_v1", 3);
            o.Roundtrip();
            return o;
        }
        catch
        {
            o.Dispose();
            throw;
        }
    }

    /// <summary>在 (x, y)（合成器逻辑坐标，相对输出左上角）显示 width×height 的覆盖层</summary>
    public void Show(int x, int y, int width, int height)
    {
        if (_surface == 0)
        {
            _surface = NewId();
            Send(_compositor, 0, w => w.NewId(_surface)); // wl_compositor.create_surface
            var region = NewId();
            Send(_compositor, 1, w => w.NewId(region)); // create_region：空区域 = 不接收任何指针输入
            Send(_surface, 5, w => w.Object(region)); // wl_surface.set_input_region
            Send(region, 0, _ => { }); // wl_region.destroy
            _layerSurface = NewId();
            Send(_layerShell, 0, w => w.NewId(_layerSurface).Object(_surface).Object(0).UInt(3 /*overlay*/).String("bettergi-mask"));
            Send(_layerSurface, 4, w => w.UInt(0)); // keyboard_interactivity none
            Send(_layerSurface, 2, w => w.Int(-1)); // exclusive_zone -1：不被其它面板挤开
        }

        Send(_layerSurface, 1, w => w.UInt(1 | 4)); // anchor top | left
        Send(_layerSurface, 3, w => w.Int(y).Int(0).Int(0).Int(x)); // margin
        Send(_layerSurface, 0, w => w.UInt((uint)width).UInt((uint)height)); // set_size
        if (!_configured)
        {
            Send(_surface, 6, _ => { }); // 首次 commit（不带缓冲），等 configure
            Roundtrip();
            if (_configureSerial is null) throw new InvalidOperationException("layer surface 没有收到 configure");
        }

        if (width != _width || height != _height) AllocateBuffers(width, height);
        Render([]);
    }

    /// <summary>重画全部矩形（透明背景）并提交</summary>
    public void Render(IReadOnlyList<OverlayRect> rects)
    {
        if (_width == 0) return;
        Dispatch(blocking: false);
        if (_busy[_current]) _current ^= 1;
        var offset = (long)_current * _width * _height * 4;
        unsafe
        {
            var px = new Span<uint>((void*)(_map + (nint)offset), _width * _height);
            px.Clear();
            foreach (var r in rects) Draw(px, r);
        }

        if (_configureSerial is { } serial)
        {
            Send(_layerSurface, 6, w => w.UInt(serial)); // ack_configure
            _configureSerial = null;
            _configured = true;
        }

        Send(_surface, 1, w => w.Object(_buffers[_current]).Int(0).Int(0)); // attach
        Send(_surface, 2, w => w.Int(0).Int(0).Int(_width).Int(_height)); // damage
        Send(_surface, 6, _ => { }); // commit
        _busy[_current] = true;
        _current ^= 1;
        Roundtrip();
    }

    /// <summary>隐藏（提交空缓冲）；之后可再 <see cref="Show"/></summary>
    public void Hide()
    {
        if (_surface == 0) return;
        Send(_surface, 1, w => w.Object(0).Int(0).Int(0));
        Send(_surface, 6, _ => { });
        _configured = false;
        Roundtrip();
    }

    private void Draw(Span<uint> px, OverlayRect r)
    {
        // ARGB8888 预乘 alpha
        var a = r.Argb >> 24;
        uint Pm(uint c) => c * a / 255;
        var color = (a << 24) | (Pm((r.Argb >> 16) & 0xFF) << 16) | (Pm((r.Argb >> 8) & 0xFF) << 8) | Pm(r.Argb & 0xFF);
        var x0 = Math.Max(0, r.X);
        var y0 = Math.Max(0, r.Y);
        var x1 = Math.Min(_width, r.X + r.Width);
        var y1 = Math.Min(_height, r.Y + r.Height);
        var t = Math.Max(1, r.Thickness);
        for (var y = y0; y < y1; y++)
        {
            var row = px.Slice(y * _width, _width);
            var edgeRow = r.Fill || y < r.Y + t || y >= r.Y + r.Height - t;
            for (var x = x0; x < x1; x++)
            {
                if (edgeRow || x < r.X + t || x >= r.X + r.Width - t) row[x] = color;
            }
        }
    }

    private void AllocateBuffers(int width, int height)
    {
        if (_pool != 0)
        {
            foreach (var b in _buffers) Send(b, 0, _ => { }); // wl_buffer.destroy
            Send(_pool, 1, _ => { }); // wl_shm_pool.destroy
            Native.munmap(_map, (nuint)_poolSize);
            Native.close(_poolFd);
        }

        _width = width;
        _height = height;
        var stride = width * 4;
        _poolSize = (long)stride * height * 2;
        _poolFd = Native.memfd_create("bettergi-overlay", 1 /*MFD_CLOEXEC*/);
        if (_poolFd < 0 || Native.ftruncate(_poolFd, _poolSize) != 0) throw new IOException($"memfd 失败 errno={Marshal.GetLastWin32Error()}");
        _map = Native.mmap(IntPtr.Zero, (nuint)_poolSize, 3 /*READ|WRITE*/, 1 /*SHARED*/, _poolFd, 0);
        if (_map == new IntPtr(-1)) throw new IOException($"mmap 失败 errno={Marshal.GetLastWin32Error()}");
        _pool = NewId();
        Send(_shm, 0, w => w.NewId(_pool).Int((int)_poolSize), _poolFd); // fd 参数不占消息体，只走 SCM_RIGHTS
        for (var i = 0; i < 2; i++)
        {
            _buffers[i] = NewId();
            var id = _buffers[i];
            var off = i * stride * height;
            Send(_pool, 0, w => w.NewId(id).Int(off).Int(width).Int(height).Int(stride).UInt(0 /*ARGB8888*/));
            _busy[i] = false;
        }
    }

    private uint NewId() => _nextId++;

    private uint Bind(string iface, uint maxVersion)
    {
        if (!_globals.TryGetValue(iface, out var g))
        {
            throw iface == "zwlr_layer_shell_v1"
                ? new NotSupportedException("合成器不支持 wlr-layer-shell（zwlr_layer_shell_v1），无法显示 Wayland 遮罩")
                : new NotSupportedException($"合成器缺少 {iface}");
        }

        var id = NewId();
        var version = Math.Min(maxVersion, g.Version);
        Send(2, 0, w => w.UInt(g.Name).String(iface).UInt(version).NewId(id)); // wl_registry.bind
        return id;
    }

    private void Roundtrip()
    {
        _syncId = NewId();
        _syncDone = false;
        Send(DisplayId, 0, w => w.NewId(_syncId)); // wl_display.sync
        while (!_syncDone) Dispatch(blocking: true);
    }

    private void Dispatch(bool blocking)
    {
        var n = Native.recv(_fd, ref _rx[_rxLen], _rx.Length - _rxLen, blocking ? 0 : 0x40 /*MSG_DONTWAIT*/);
        if (n < 0)
        {
            var errno = Marshal.GetLastWin32Error();
            if (!blocking && errno == 11) return;
            throw new IOException($"读取 Wayland 事件失败 errno={errno}");
        }

        if (n == 0) throw new IOException("Wayland 合成器断开了连接");
        _rxLen += (int)n;
        var pos = 0;
        while (_rxLen - pos >= 8)
        {
            var obj = BinaryPrimitives.ReadUInt32LittleEndian(_rx.AsSpan(pos));
            var word = BinaryPrimitives.ReadUInt32LittleEndian(_rx.AsSpan(pos + 4));
            var size = (int)(word >> 16);
            var op = word & 0xFFFF;
            if (size < 8 || _rxLen - pos < size) break;
            HandleEvent(obj, op, _rx.AsSpan(pos + 8, size - 8));
            pos += size;
        }

        Array.Copy(_rx, pos, _rx, 0, _rxLen - pos);
        _rxLen -= pos;
    }

    private void HandleEvent(uint obj, uint op, ReadOnlySpan<byte> a)
    {
        if (obj == DisplayId && op == 0)
        {
            var code = BinaryPrimitives.ReadUInt32LittleEndian(a[4..]);
            throw new InvalidOperationException($"Wayland 协议错误：对象 {BinaryPrimitives.ReadUInt32LittleEndian(a)} 代码 {code} {ReadString(a[8..], out _)}");
        }

        if (obj == 2 && op == 0) // wl_registry.global
        {
            var name = BinaryPrimitives.ReadUInt32LittleEndian(a);
            var iface = ReadString(a[4..], out var used);
            var version = BinaryPrimitives.ReadUInt32LittleEndian(a[(4 + used)..]);
            _globals[iface] = (name, version);
        }
        else if (obj == _syncId && op == 0)
        {
            _syncDone = true;
        }
        else if (obj == _layerSurface && _layerSurface != 0)
        {
            if (op == 0) _configureSerial = BinaryPrimitives.ReadUInt32LittleEndian(a);
            else if (op == 1) Closed = true;
        }
        else
        {
            for (var i = 0; i < 2; i++)
            {
                if (obj == _buffers[i] && op == 0) _busy[i] = false; // wl_buffer.release
            }
        }
    }

    private static string ReadString(ReadOnlySpan<byte> a, out int used)
    {
        var len = (int)BinaryPrimitives.ReadUInt32LittleEndian(a);
        used = 4 + ((len + 3) & ~3);
        return len == 0 ? "" : Encoding.UTF8.GetString(a.Slice(4, len - 1));
    }

    private void Send(uint obj, uint op, Action<ArgWriter> args, int fd = -1)
    {
        var w = new ArgWriter();
        args(w);
        var body = w.ToArray();
        var msg = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(msg, obj);
        BinaryPrimitives.WriteUInt32LittleEndian(msg.AsSpan(4), ((uint)msg.Length << 16) | op);
        body.CopyTo(msg, 8);
        if (Native.SendWithFd(_fd, msg, fd) != msg.Length) throw new IOException($"发送 Wayland 请求失败 errno={Marshal.GetLastWin32Error()}");
    }

    public void Dispose()
    {
        try
        {
            if (_layerSurface != 0) Send(_layerSurface, 7, _ => { });
            if (_surface != 0) Send(_surface, 0, _ => { });
        }
        catch (Exception e) when (e is IOException or InvalidOperationException)
        {
        }

        if (_map != IntPtr.Zero) Native.munmap(_map, (nuint)_poolSize);
        if (_poolFd >= 0) Native.close(_poolFd);
        Native.close(_fd);
    }

    private sealed class ArgWriter
    {
        private readonly MemoryStream _ms = new();

        public ArgWriter UInt(uint v)
        {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(b, v);
            _ms.Write(b);
            return this;
        }

        public ArgWriter Int(int v) => UInt(unchecked((uint)v));
        public ArgWriter NewId(uint v) => UInt(v);
        public ArgWriter Object(uint v) => UInt(v);

        public ArgWriter String(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            UInt((uint)bytes.Length + 1);
            _ms.Write(bytes);
            var pad = 4 - (bytes.Length % 4);
            for (var i = 0; i < pad; i++) _ms.WriteByte(0);
            return this;
        }

        public byte[] ToArray() => _ms.ToArray();
    }

    private static class Native
    {
        [DllImport("libc", SetLastError = true)] public static extern int socket(int domain, int type, int protocol);
        [DllImport("libc", SetLastError = true)] public static extern int connect(int fd, byte[] addr, int len);
        [DllImport("libc", SetLastError = true)] public static extern nint recv(int fd, ref byte buf, nint len, int flags);
        [DllImport("libc", SetLastError = true)] public static extern int close(int fd);
        [DllImport("libc", SetLastError = true)] public static extern int memfd_create(string name, uint flags);
        [DllImport("libc", SetLastError = true)] public static extern int ftruncate(int fd, long len);
        [DllImport("libc", SetLastError = true)] public static extern IntPtr mmap(IntPtr addr, nuint len, int prot, int flags, int fd, long off);
        [DllImport("libc", SetLastError = true)] public static extern int munmap(IntPtr addr, nuint len);
        [DllImport("libc", SetLastError = true)] private static extern nint sendmsg(int fd, ref MsgHdr msg, int flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct IoVec { public IntPtr Base; public nuint Len; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MsgHdr
        {
            public IntPtr Name; public uint NameLen; public IntPtr Iov; public nuint IovLen;
            public IntPtr Control; public nuint ControlLen; public int Flags;
        }

        /// <summary>sendmsg，<paramref name="fd"/> ≥ 0 时用 SCM_RIGHTS 附带一个文件描述符（wl_shm.create_pool）</summary>
        public static unsafe nint SendWithFd(int sock, byte[] data, int fd)
        {
            fixed (byte* p = data)
            {
                var iov = new IoVec { Base = (IntPtr)p, Len = (nuint)data.Length };
                var control = stackalloc byte[24]; // CMSG_SPACE(sizeof(int))
                var msg = new MsgHdr { Iov = (IntPtr)(&iov), IovLen = 1 };
                if (fd >= 0)
                {
                    new Span<byte>(control, 24).Clear();
                    *(ulong*)control = 20; // cmsg_len = CMSG_LEN(4)
                    *(int*)(control + 8) = 1; // SOL_SOCKET
                    *(int*)(control + 12) = 1; // SCM_RIGHTS
                    *(int*)(control + 16) = fd;
                    msg.Control = (IntPtr)control;
                    msg.ControlLen = 24;
                }

                return sendmsg(sock, ref msg, 0x4000 /*MSG_NOSIGNAL*/);
            }
        }
    }
}
