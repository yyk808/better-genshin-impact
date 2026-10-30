using System.Diagnostics;
using BetterGenshinImpact.Core.Monitor;
using static BetterGenshinImpact.Platform.Linux.X11.Native.Xlib;

namespace BetterGenshinImpact.Platform.Linux.X11;

/// <summary>
/// XInput2 原始鼠标移动（XI_RawMotion）实现的相对鼠标监听，对应 Windows 的 RawInputMonitor / DirectInputMonitor。
/// <list type="bullet">
/// <item>在根窗口上为全部主设备选择 XI_RawMotion，不抢占输入；回调在采集线程执行。声明 XI 2.2，使其它客户端抓取指针时仍能收到。</item>
/// <item>使用未加速的 raw 值；小数部分按设备累计，避免高精度设备的增量被截断。</item>
/// <item>持有指针抓取时，X 服务器会把同一事件按“抓取”与“根窗口选择”各投递一次，按 (time, sourceid, raw) 去重。</item>
/// <item>绝对坐标设备（数位板、虚拟机 / 远程桌面的绝对指针）的 raw 值是位置而不是增量，按设备记住上一个位置换算为增量。</item>
/// </list>
/// 首个订阅建立独立连接并启动采集线程，最后一个订阅释放后停止。
/// </summary>
public sealed unsafe class X11RelativeMouseInputMonitor : IRelativeMouseInputMonitor, IDisposable
{
    private const int XIAllMasterDevices = 1;
    private const int XI_RawMotion = 17;
    private const int XIValuatorClass = 2;
    private const int XIModeAbsolute = 1;

    private readonly string? _display;
    private readonly object _gate = new();
    private readonly List<EventHandler<RelativeMouseMoveEventArgs>> _handlers = [];
    private Thread? _thread;
    private volatile bool _stop;

    /// <param name="display">X 显示名，为 null 时使用 $DISPLAY</param>
    public X11RelativeMouseInputMonitor(string? display = null) => _display = display;

    /// <summary>采集线程是否在运行（测试用）</summary>
    public bool IsRunning => _thread is { IsAlive: true };

    public IDisposable Subscribe(EventHandler<RelativeMouseMoveEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            _handlers.Add(handler);
            if (_thread == null)
            {
                _stop = false;
                using var ready = new ManualResetEventSlim();
                Exception? startError = null;
                _thread = new Thread(() => Run(ready, e => startError = e)) { IsBackground = true, Name = "X11RawMouse" };
                _thread.Start();
                ready.Wait();
                if (startError != null)
                {
                    _thread = null;
                    _handlers.Remove(handler);
                    throw new InvalidOperationException("XInput2 原始鼠标事件不可用", startError);
                }
            }
        }

        return new Subscription(this, handler);
    }

    private void Unsubscribe(EventHandler<RelativeMouseMoveEventArgs> handler)
    {
        Thread? toJoin = null;
        lock (_gate)
        {
            if (!_handlers.Remove(handler) || _handlers.Count > 0) return;
            _stop = true;
            toJoin = _thread;
            _thread = null;
        }

        if (toJoin != null && toJoin != Thread.CurrentThread) toJoin.Join(1000);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _handlers.Clear();
            _stop = true;
        }

        _thread?.Join(1000);
        _thread = null;
    }

    private void Run(ManualResetEventSlim ready, Action<Exception> onStartError)
    {
        X11Connection conn;
        int opcode;
        try
        {
            conn = new X11Connection(_display);
            if (!XQueryExtension(conn.Display, "XInputExtension", out opcode, out _, out _))
                throw new NotSupportedException("X 服务器没有 XInputExtension");
            // 必须声明 2.1 及以上：XI 2.0 语义下其它客户端抓取指针（游戏锁定视角）时根窗口收不到原始事件
            int major = 2, minor = 2;
            if (XIQueryVersion(conn.Display, ref major, ref minor) != 0 || major < 2 || (major == 2 && minor < 1))
                throw new NotSupportedException("X 服务器不支持 XInput 2.1");
            var m = stackalloc byte[4];
            new Span<byte>(m, 4).Clear();
            m[XI_RawMotion >> 3] |= (byte)(1 << (XI_RawMotion & 7));
            var mask = new Native.XIEventMask { deviceid = XIAllMasterDevices, mask_len = 4, mask = m };
            XISelectEvents(conn.Display, conn.Root, &mask, 1);
            XSync(conn.Display, false);
        }
        catch (Exception e)
        {
            onStartError(e);
            ready.Set();
            return;
        }

        ready.Set();
        var state = new RawMotionState(id => IsAbsoluteDevice(conn.Display, id));
        var ev = stackalloc byte[192];
        var fd = new Native.PollFd { fd = XConnectionNumber(conn.Display), events = 1 /*POLLIN*/ };
        try
        {
            while (!_stop)
            {
                if (XPending(conn.Display) == 0)
                {
                    poll(&fd, 1, 100);
                    continue;
                }

                XNextEvent(conn.Display, ev);
                if (*(int*)ev != GenericEvent || *(int*)(ev + 32) != opcode || *(int*)(ev + 36) != XI_RawMotion) continue;
                if (!XGetEventData(conn.Display, ev)) continue;
                RelativeMouseMoveEventArgs? args;
                try
                {
                    args = state.Process(*(nint*)(ev + 48));
                }
                finally
                {
                    XFreeEventData(conn.Display, ev);
                }

                if (args != null) Raise(args);
            }
        }
        finally
        {
            conn.Dispose();
        }
    }

    private void Raise(RelativeMouseMoveEventArgs args)
    {
        EventHandler<RelativeMouseMoveEventArgs>[] handlers;
        lock (_gate) handlers = [.. _handlers];
        foreach (var h in handlers)
        {
            try
            {
                h(this, args);
            }
            catch (Exception e)
            {
                Debug.WriteLine($"[X11RawMouse] 回调异常: {e}");
            }
        }
    }

    /// <summary>设备的 X / Y 轴（valuator 0 / 1）是否为绝对模式</summary>
    private static bool IsAbsoluteDevice(nint display, int deviceId)
    {
        var info = XIQueryDevice(display, deviceId, out int n);
        if (info == 0) return false;
        try
        {
            if (n < 1) return false;
            // XIDeviceInfo: deviceid@0 name@8 use@16 attachment@20 enabled@24 num_classes@28 classes@32
            var p = (byte*)info;
            int numClasses = *(int*)(p + 28);
            var classes = *(nint**)(p + 32);
            for (int i = 0; i < numClasses; i++)
            {
                // XIValuatorClassInfo: type@0 sourceid@4 number@8 label@16 min@24 max@32 value@40 resolution@48 mode@52
                var c = (byte*)classes[i];
                if (*(int*)c == XIValuatorClass && *(int*)(c + 8) is 0 or 1 && *(int*)(c + 52) == XIModeAbsolute) return true;
            }

            return false;
        }
        finally
        {
            XIFreeDeviceInfo(info);
        }
    }

    /// <summary>
    /// 解析 XIRawEvent 并换算为整数增量：去重、绝对设备换算、小数累计。
    /// XIRawEvent: time@40 deviceid@48 sourceid@52 detail@56 flags@60 valuators{mask_len@64 mask@72 values@80} raw_values@88
    /// </summary>
    public sealed class RawMotionState(Func<int, bool> isAbsolute)
    {
        private readonly Dictionary<int, bool> _absolute = new();
        private readonly Dictionary<int, (double X, double Y, bool HasX, bool HasY)> _lastAbsolute = new();
        private readonly Dictionary<int, (double X, double Y)> _remainder = new();
        private (ulong Time, int Source, double X, double Y, bool HasX, bool HasY) _last;

        public RelativeMouseMoveEventArgs? Process(nint data)
        {
            var p = (byte*)data;
            ulong time = *(ulong*)(p + 40);
            int sourceId = *(int*)(p + 52);
            int maskLen = *(int*)(p + 64);
            byte* mask = *(byte**)(p + 72);
            double* raw = *(double**)(p + 88);
            double x = 0, y = 0;
            bool hasX = false, hasY = false;
            int idx = 0;
            for (int bit = 0; bit < maskLen * 8 && bit < 2; bit++)
            {
                if ((mask[bit >> 3] & (1 << (bit & 7))) == 0) continue;
                if (bit == 0) { x = raw[idx]; hasX = true; }
                else { y = raw[idx]; hasY = true; }
                idx++;
            }

            return Process(time, sourceId, x, y, hasX, hasY);
        }

        /// <summary>按已解析的字段处理一个原始移动事件，没有有效增量时返回 null</summary>
        public RelativeMouseMoveEventArgs? Process(ulong time, int sourceId, double x, double y, bool hasX, bool hasY)
        {
            if (!hasX && !hasY) return null;
            var key = (time, sourceId, x, y, hasX, hasY);
            if (key == _last) return null; // 抓取与根窗口选择的重复投递
            _last = key;

            if (!_absolute.TryGetValue(sourceId, out var abs))
            {
                abs = isAbsolute(sourceId);
                _absolute[sourceId] = abs;
            }

            double dx, dy;
            if (abs)
            {
                _lastAbsolute.TryGetValue(sourceId, out var prev);
                dx = hasX && prev.HasX ? x - prev.X : 0;
                dy = hasY && prev.HasY ? y - prev.Y : 0;
                _lastAbsolute[sourceId] = (hasX ? x : prev.X, hasY ? y : prev.Y, prev.HasX || hasX, prev.HasY || hasY);
            }
            else
            {
                dx = hasX ? x : 0;
                dy = hasY ? y : 0;
            }

            _remainder.TryGetValue(sourceId, out var r);
            double fx = dx + r.X, fy = dy + r.Y;
            int ix = (int)Math.Truncate(fx), iy = (int)Math.Truncate(fy);
            _remainder[sourceId] = (fx - ix, fy - iy);
            if (ix == 0 && iy == 0) return null;
            return new RelativeMouseMoveEventArgs(ix, iy, DateTime.UtcNow);
        }
    }

    private sealed class Subscription(X11RelativeMouseInputMonitor owner, EventHandler<RelativeMouseMoveEventArgs> handler) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Unsubscribe(handler);
        }
    }
}

/// <summary>Linux 的相对鼠标监听工厂：DirectInput 与 RawInput 两种类型都使用 XInput2 原始事件</summary>
public sealed class X11RelativeMouseInputMonitorFactory(X11RelativeMouseInputMonitor monitor) : IRelativeMouseInputMonitorFactory
{
    public IRelativeMouseInputMonitor Get(RelativeMouseInputType type) => type switch
    {
        RelativeMouseInputType.DirectInput or RelativeMouseInputType.RawInput => monitor,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "不支持的相对鼠标输入类型")
    };
}
