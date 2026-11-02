using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace BetterGenshinImpact.Platform.Linux.Wayland;

/// <summary>遮罩上画的一个矩形（相对覆盖层左上角的像素坐标，颜色为 0xAARRGGBB）</summary>
/// <summary>
/// 覆盖层上的指针事件（坐标为覆盖层逻辑坐标；Button 为 Linux 按键码，如 0x110 左键、0x111 右键；
/// Scroll 的 Delta 为 wl_pointer.axis 的竖直滚动量，正数向下，滚轮一格通常为 10 或 15）
/// </summary>
public readonly record struct OverlayPointerEvent(OverlayPointerKind Kind, double X, double Y, uint Button = 0, double Delta = 0);

public enum OverlayPointerKind { Enter, Leave, Move, Press, Release, Scroll }

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
    private readonly List<(uint Name, uint Version)> _outputGlobals = [];
    private readonly Dictionary<uint, string> _outputs = new();
    private string? _outputName;
    private int _bufferScale = 1;
    private uint _seat, _pointer, _cursorShapeManager, _cursorShapeDevice, _enterSerial;
    private uint _cursorShape = 1;
    private double _pointerX, _pointerY;
    private IReadOnlyList<(int X, int Y, int Width, int Height)> _inputRects = [];

    /// <summary>
    /// 指针事件（只有输入区域非空时合成器才会发送，见 <see cref="SetInputRegion"/>）。
    /// 在调用 <see cref="Pump"/> 的线程上触发
    /// </summary>
    public event Action<OverlayPointerEvent>? Pointer;

    /// <summary>读事件时先排队，只在 <see cref="Pump"/> 里送出（避免在渲染中途重入）</summary>
    private readonly Queue<OverlayPointerEvent> _pointerQueue = new();

    /// <summary>合成器上的输出名（wl_output.name，与 niri 的输出名相同）</summary>
    public IReadOnlyCollection<string> OutputNames => _outputs.Values;

    /// <summary>当前覆盖层所在的输出（null 表示由合成器选择）</summary>
    public string? OutputName => _outputName;

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
            if (o._globals.ContainsKey("wl_seat"))
            {
                o._seat = o.Bind("wl_seat", 5); // capabilities 事件里有指针时再 get_pointer
            }

            if (o._globals.ContainsKey("wp_cursor_shape_manager_v1"))
            {
                o._cursorShapeManager = o.Bind("wp_cursor_shape_manager_v1", 1); // 指针在覆盖层上时的光标形状
            }
            foreach (var (name, version) in o._outputGlobals)
            {
                var id = o.NewId();
                o.Send(2, 0, w => w.UInt(name).String("wl_output").UInt(Math.Min(4u, version)).NewId(id));
                o._outputs[id] = "";
            }

            o.Roundtrip();
            return o;
        }
        catch
        {
            o.Dispose();
            throw;
        }
    }

    /// <summary>在 (x, y)（合成器逻辑坐标，相对输出左上角）显示 width×height 的覆盖层（输出由合成器选择）</summary>
    public void Show(int x, int y, int width, int height) => Show(null, x, y, width, height);

    /// <summary>
    /// 在输出 <paramref name="output"/>（wl_output 名；null 或找不到时由合成器选择）的 (x, y) 处显示 width×height（逻辑像素）的覆盖层；
    /// <paramref name="bufferScale"/> 为整数缩放时缓冲区按物理像素分配（width×scale），清晰度与输出一致。
    /// 只设置位置和大小，内容由 <see cref="Render"/> / <see cref="RenderPixels"/> 提交
    /// </summary>
    public void Show(string? output, int x, int y, int width, int height, int bufferScale = 1)
    {
        bufferScale = Math.Max(1, bufferScale);
        if (_surface != 0 && output != _outputName)
        {
            DestroySurfaces(); // 输出只能在创建 layer surface 时指定
        }

        if (_surface == 0)
        {
            _outputName = output;
            var outputId = output is null ? 0u : _outputs.FirstOrDefault(kv => kv.Value == output).Key;
            _surface = NewId();
            Send(_compositor, 0, w => w.NewId(_surface)); // wl_compositor.create_surface
            SendInputRegion(); // 默认空区域 = 不接收任何指针输入
            _layerSurface = NewId();
            Send(_layerShell, 0, w => w.NewId(_layerSurface).Object(_surface).Object(outputId).UInt(3 /*overlay*/).String("bettergi-mask"));
            Send(_layerSurface, 4, w => w.UInt(0)); // keyboard_interactivity none
            Send(_layerSurface, 2, w => w.Int(-1)); // exclusive_zone -1：不被其它面板挤开
            _bufferScale = 1;
        }

        if (bufferScale != _bufferScale)
        {
            Send(_surface, 8, w => w.Int(bufferScale)); // wl_surface.set_buffer_scale
            _bufferScale = bufferScale;
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

        var pw = width * bufferScale;
        var ph = height * bufferScale;
        if (pw != _width || ph != _height) AllocateBuffers(pw, ph);
    }

    /// <summary>
    /// 设置输入区域（逻辑坐标的矩形；空列表 = 完全点击穿透）。矩形内的指针事件经 <see cref="Pointer"/> 送出，
    /// 其余部分仍穿透到下面的窗口。覆盖层重建（换输出）后沿用
    /// </summary>
    public void SetInputRegion(IReadOnlyList<(int X, int Y, int Width, int Height)> rects)
    {
        _inputRects = rects.ToArray();
        if (_surface == 0) return;
        SendInputRegion();
        if (_configured) Send(_surface, 6, _ => { }); // wl_surface.commit（输入区域是双缓冲状态）
        Flush();
    }

    private void SendInputRegion()
    {
        var region = NewId();
        Send(_compositor, 1, w => w.NewId(region)); // wl_compositor.create_region
        foreach (var (x, y, width, height) in _inputRects)
        {
            if (width > 0 && height > 0) Send(region, 1, w => w.Int(x).Int(y).Int(width).Int(height)); // wl_region.add
        }

        Send(_surface, 5, w => w.Object(region)); // wl_surface.set_input_region
        Send(region, 0, _ => { }); // wl_region.destroy
    }

    /// <summary>
    /// 指针在覆盖层上时的光标形状（wp_cursor_shape_device_v1.shape：1 默认、13 移动、26 左右、27 上下、28 右上左下、29 左上右下）；
    /// 合成器不支持 cursor-shape 时忽略
    /// </summary>
    public void SetCursorShape(uint shape)
    {
        if (shape == _cursorShape) return;
        _cursorShape = shape;
        SendCursorShape();
    }

    private void SendCursorShape()
    {
        if (_cursorShapeDevice == 0 || _enterSerial == 0) return;
        var serial = _enterSerial;
        var shape = _cursorShape;
        Send(_cursorShapeDevice, 1, w => w.UInt(serial).UInt(shape)); // set_shape
    }

    /// <summary>读取并处理已到达的事件（不阻塞），指针事件在此触发</summary>
    public void Pump()
    {
        Flush();
        while (_pointerQueue.TryDequeue(out var e)) Pointer?.Invoke(e);
    }

    private void Flush()
    {
        while (Dispatch(blocking: false))
        {
        }
    }

    /// <summary>缓冲区像素尺寸（逻辑尺寸 × 缓冲缩放）</summary>
    public int BufferScale => _bufferScale;

    /// <summary>重画全部矩形（透明背景，坐标为缓冲区像素）并提交</summary>
    public void Render(IReadOnlyList<OverlayRect> rects)
    {
        if (_width == 0) return;
        var px = BeginFrame();
        px.Clear();
        foreach (var r in rects) Draw(px, r);
        EndFrame();
    }

    /// <summary>
    /// 提交一帧整幅位图（预乘 alpha，每像素 4 字节；<paramref name="rgba"/> 为 true 时源为 RGBA 顺序，否则为 BGRA = ARGB8888 小端）。
    /// 源比缓冲区小的部分保持透明，大的部分裁掉
    /// </summary>
    public void RenderPixels(ReadOnlySpan<byte> pixels, int width, int height, int stride, bool rgba = false)
    {
        if (_width == 0) return;
        var px = BeginFrame();
        px.Clear();
        var w = Math.Min(width, _width);
        var h = Math.Min(height, _height);
        var dst = MemoryMarshal.AsBytes(px);
        for (var y = 0; y < h; y++)
        {
            var src = pixels.Slice(y * stride, w * 4);
            var row = dst.Slice(y * _width * 4, w * 4);
            src.CopyTo(row);
            if (rgba)
            {
                for (var i = 0; i < row.Length; i += 4) (row[i], row[i + 2]) = (row[i + 2], row[i]);
            }
        }

        EndFrame();
    }

    private unsafe Span<uint> BeginFrame()
    {
        Dispatch(blocking: false);
        if (_busy[_current]) _current ^= 1;
        var offset = (long)_current * _width * _height * 4;
        return new Span<uint>((void*)(_map + (nint)offset), _width * _height);
    }

    private void EndFrame()
    {
        if (_configureSerial is { } serial)
        {
            Send(_layerSurface, 6, w => w.UInt(serial)); // ack_configure
            _configureSerial = null;
            _configured = true;
        }

        Send(_surface, 1, w => w.Object(_buffers[_current]).Int(0).Int(0)); // attach
        Send(_surface, 2, w => w.Int(0).Int(0).Int(int.MaxValue).Int(int.MaxValue)); // damage（整个表面）
        Send(_surface, 6, _ => { }); // commit
        _busy[_current] = true;
        _current ^= 1;
        Roundtrip();
    }

    /// <summary>隐藏：销毁 layer surface（之后可再 <see cref="Show(string?, int, int, int, int, int)"/>）</summary>
    public void Hide()
    {
        if (_surface == 0) return;
        DestroySurfaces();
        Roundtrip();
    }

    private void DestroySurfaces()
    {
        Send(_layerSurface, 7, _ => { }); // zwlr_layer_surface_v1.destroy
        Send(_surface, 0, _ => { }); // wl_surface.destroy
        _surface = _layerSurface = 0;
        _configured = false;
        _configureSerial = null;
        _busy[0] = _busy[1] = false;
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

    /// <summary>读一次事件并处理；非阻塞且没有数据时返回 false</summary>
    private bool Dispatch(bool blocking)
    {
        var n = Native.recv(_fd, ref _rx[_rxLen], _rx.Length - _rxLen, blocking ? 0 : 0x40 /*MSG_DONTWAIT*/);
        if (n < 0)
        {
            var errno = Marshal.GetLastWin32Error();
            if (!blocking && errno == 11) return false;
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
        return true;
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
            if (iface == "wl_output") _outputGlobals.Add((name, version));
        }
        else if (obj == _syncId && op == 0)
        {
            _syncDone = true;
        }
        else if (op == 4 && _outputs.ContainsKey(obj)) // wl_output.name（v4）
        {
            _outputs[obj] = ReadString(a, out _);
        }
        else if (obj == _seat && _seat != 0 && op == 0) // wl_seat.capabilities
        {
            var caps = BinaryPrimitives.ReadUInt32LittleEndian(a);
            if ((caps & 1) != 0 && _pointer == 0)
            {
                _pointer = NewId();
                Send(_seat, 0, w => w.NewId(_pointer)); // wl_seat.get_pointer
                if (_cursorShapeManager != 0)
                {
                    _cursorShapeDevice = NewId();
                    Send(_cursorShapeManager, 1, w => w.NewId(_cursorShapeDevice).Object(_pointer)); // get_pointer
                }
            }
            else if ((caps & 1) == 0 && _pointer != 0)
            {
                if (_cursorShapeDevice != 0) Send(_cursorShapeDevice, 0, _ => { }); // destroy
                Send(_pointer, 1, _ => { }); // wl_pointer.release（v3+）
                _pointer = _cursorShapeDevice = _enterSerial = 0;
            }
        }
        else if (obj == _pointer && _pointer != 0)
        {
            HandlePointer(op, a);
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

    private void HandlePointer(uint op, ReadOnlySpan<byte> a)
    {
        static double Fixed(ReadOnlySpan<byte> b) => BinaryPrimitives.ReadInt32LittleEndian(b) / 256.0;
        switch (op)
        {
            case 0: // enter(serial, surface, x, y)
                if (BinaryPrimitives.ReadUInt32LittleEndian(a[4..]) != _surface) return;
                _enterSerial = BinaryPrimitives.ReadUInt32LittleEndian(a);
                SendCursorShape();
                _pointerX = Fixed(a[8..]);
                _pointerY = Fixed(a[12..]);
                _pointerQueue.Enqueue(new OverlayPointerEvent(OverlayPointerKind.Enter, _pointerX, _pointerY));
                break;
            case 1: // leave(serial, surface)
                _pointerQueue.Enqueue(new OverlayPointerEvent(OverlayPointerKind.Leave, _pointerX, _pointerY));
                break;
            case 2: // motion(time, x, y)
                _pointerX = Fixed(a[4..]);
                _pointerY = Fixed(a[8..]);
                _pointerQueue.Enqueue(new OverlayPointerEvent(OverlayPointerKind.Move, _pointerX, _pointerY));
                break;
            case 3: // button(serial, time, button, state)
                var button = BinaryPrimitives.ReadUInt32LittleEndian(a[8..]);
                var pressed = BinaryPrimitives.ReadUInt32LittleEndian(a[12..]) == 1;
                _pointerQueue.Enqueue(new OverlayPointerEvent(pressed ? OverlayPointerKind.Press : OverlayPointerKind.Release, _pointerX, _pointerY, button));
                break;
            case 4: // axis(time, axis, value)：只取竖直方向（axis 0）
                if (BinaryPrimitives.ReadUInt32LittleEndian(a[4..]) == 0)
                {
                    _pointerQueue.Enqueue(new OverlayPointerEvent(OverlayPointerKind.Scroll, _pointerX, _pointerY, 0, Fixed(a[8..])));
                }

                break;
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
