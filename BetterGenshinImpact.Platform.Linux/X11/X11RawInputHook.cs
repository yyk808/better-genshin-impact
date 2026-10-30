using System.Diagnostics;
using static BetterGenshinImpact.Platform.Linux.X11.Native.Xlib;

namespace BetterGenshinImpact.Platform.Linux.X11;

/// <summary>
/// XInput2 原始事件（XI_RawKeyPress / Release、XI_RawButtonPress / Release、XI_RawMotion）实现的全局键鼠监听，
/// 对应 Windows 的低级键鼠钩子：只监听、不抓取，前台窗口（游戏）照常收到按键。
/// <list type="bullet">
/// <item>声明 XI 2.2：其它客户端抓取键盘 / 指针时根窗口仍收到原始事件；抓取时的重复投递按 (类型, time, sourceid, detail) 去重。</item>
/// <item>本进程经 XTest 注入的事件（<see cref="XTestInjectionLedger"/>）标记 IsSelfInjected（快捷键据此忽略，避免回环）；<see cref="IgnoreOwnInjection"/> 为 true 时直接丢弃。</item>
/// <item>按键自动重复（XIKeyRepeat）不报告；修饰键状态按原始事件自行维护。</item>
/// <item>移动事件用 XQueryPointer 取光标屏幕坐标；滚轮为按钮 4 / 5（±120）。</item>
/// </list>
/// 事件在采集线程上触发，订阅方不能长时间阻塞
/// </summary>
public sealed unsafe class X11RawInputHook : IGlobalInputHook
{
    private const int XIAllDevices = 0, XIAllMasterDevices = 1;
    private const int XI_RawKeyPress = 13, XI_RawKeyRelease = 14, XI_RawButtonPress = 15, XI_RawButtonRelease = 16, XI_RawMotion = 17;
    private const int XIKeyRepeat = 1 << 16;

    private readonly string? _display;
    private readonly Thread _thread;
    private readonly HashSet<GameKey> _pressed = [];
    private readonly HashSet<int> _xtestDevices = [];
    private volatile bool _stop;
    private nint _root;

    /// <param name="display">X 显示名，为 null 时使用 $DISPLAY</param>
    /// <param name="ignoreOwnInjection">丢弃本进程经 XTest 注入的按键与按钮（默认只标记）</param>
    /// <exception cref="InvalidOperationException">X 服务器不支持 XInput 2.1 以上</exception>
    public X11RawInputHook(string? display = null, bool ignoreOwnInjection = false)
    {
        _display = display;
        IgnoreOwnInjection = ignoreOwnInjection;
        using var ready = new ManualResetEventSlim();
        Exception? startError = null;
        _thread = new Thread(() => Run(ready, e => startError = e)) { IsBackground = true, Name = "X11RawInputHook" };
        _thread.Start();
        ready.Wait();
        if (startError != null) throw new InvalidOperationException("XInput2 原始事件不可用：" + startError.Message, startError);
    }

    public bool IgnoreOwnInjection { get; set; }

    /// <summary>识别出的自身注入事件数（测试用）</summary>
    public int IgnoredInjectionCount { get; private set; }

    public event EventHandler<GlobalKeyEventArgs>? KeyDown;
    public event EventHandler<GlobalKeyEventArgs>? KeyUp;
    public event EventHandler<GlobalMouseEventArgs>? MouseDown;
    public event EventHandler<GlobalMouseEventArgs>? MouseUp;
    public event EventHandler<GlobalMouseEventArgs>? MouseMove;
    public event EventHandler<GlobalMouseEventArgs>? MouseWheel;

    public bool SupportsWheel => true;

    private void Run(ManualResetEventSlim ready, Action<Exception> onStartError)
    {
        X11Connection conn;
        int opcode;
        try
        {
            conn = new X11Connection(_display);
            if (!XQueryExtension(conn.Display, "XInputExtension", out opcode, out _, out _))
                throw new NotSupportedException("X 服务器没有 XInputExtension");
            int major = 2, minor = 2;
            if (XIQueryVersion(conn.Display, ref major, ref minor) != 0 || major < 2 || (major == 2 && minor < 1))
                throw new NotSupportedException("X 服务器不支持 XInput 2.1");
            var m = stackalloc byte[4];
            new Span<byte>(m, 4).Clear();
            foreach (var t in new[] { XI_RawKeyPress, XI_RawKeyRelease, XI_RawButtonPress, XI_RawButtonRelease, XI_RawMotion })
                m[t >> 3] |= (byte)(1 << (t & 7));
            var mask = new Native.XIEventMask { deviceid = XIAllMasterDevices, mask_len = 4, mask = m };
            XISelectEvents(conn.Display, conn.Root, &mask, 1);
            XSync(conn.Display, false);
            FindXTestDevices(conn.Display);
            _root = conn.Root;
        }
        catch (Exception e)
        {
            onStartError(e);
            ready.Set();
            return;
        }

        ready.Set();
        var ev = stackalloc byte[192];
        var fd = new Native.PollFd { fd = XConnectionNumber(conn.Display), events = 1 };
        (int Type, ulong Time, int Source, int Detail) last = default;
        try
        {
            while (!_stop)
            {
                if (XPending(conn.Display) == 0)
                {
                    poll(&fd, 1, 50);
                    continue;
                }

                XNextEvent(conn.Display, ev);
                if (*(int*)ev != GenericEvent || *(int*)(ev + 32) != opcode) continue;
                var evtype = *(int*)(ev + 36);
                if (evtype is < XI_RawKeyPress or > XI_RawMotion) continue;
                if (!XGetEventData(conn.Display, ev)) continue;
                ulong time;
                int source, detail, flags;
                try
                {
                    // XIRawEvent: time@40 deviceid@48 sourceid@52 detail@56 flags@60
                    var p = *(byte**)(ev + 48);
                    time = *(ulong*)(p + 40);
                    source = *(int*)(p + 52);
                    detail = *(int*)(p + 56);
                    flags = *(int*)(p + 60);
                }
                finally
                {
                    XFreeEventData(conn.Display, ev);
                }

                var key = (evtype, time, source, detail);
                if (evtype != XI_RawMotion && key == last) continue; // 抓取与根窗口选择的重复投递
                last = key;
                try
                {
                    Dispatch(conn.Display, evtype, (int)time, source, detail, flags);
                }
                catch (Exception e)
                {
                    Debug.WriteLine($"[X11RawInputHook] 回调异常: {e}");
                }
            }
        }
        finally
        {
            conn.Dispose();
        }
    }

    private void Dispatch(nint display, int evtype, int time, int source, int detail, int flags)
    {
        switch (evtype)
        {
            case XI_RawKeyPress or XI_RawKeyRelease:
            {
                var down = evtype == XI_RawKeyPress;
                if (down && (flags & XIKeyRepeat) != 0) return;
                var own = IsOwnInjection(source, false, detail, down);
                if (own && IgnoreOwnInjection) return;
                var k = Native.KeyMap.FromKeyCode(display, detail);
                if (k == GameKey.None) return;
                if (down)
                {
                    if (!_pressed.Add(k)) return; // 已按下（重复）
                    KeyDown?.Invoke(this, new GlobalKeyEventArgs(k, KeyModifierHelper.FromPressedKeys(_pressed), time) { IsSelfInjected = own });
                }
                else
                {
                    _pressed.Remove(k);
                    KeyUp?.Invoke(this, new GlobalKeyEventArgs(k, KeyModifierHelper.FromPressedKeys(_pressed), time) { IsSelfInjected = own });
                }

                break;
            }
            case XI_RawButtonPress or XI_RawButtonRelease:
            {
                var down = evtype == XI_RawButtonPress;
                var own = IsOwnInjection(source, true, detail, down);
                if (own && IgnoreOwnInjection) return;
                if (detail is 4 or 5)
                {
                    if (down) MouseWheel?.Invoke(this, new GlobalMouseEventArgs(GameKey.None, Cursor(display), detail == 4 ? 120 : -120, time) { IsSelfInjected = own });
                    return;
                }

                var button = detail switch
                {
                    1 => GameKey.VK_LBUTTON,
                    2 => GameKey.VK_MBUTTON,
                    3 => GameKey.VK_RBUTTON,
                    8 => GameKey.VK_XBUTTON1,
                    9 => GameKey.VK_XBUTTON2,
                    _ => GameKey.None,
                };
                if (button == GameKey.None) return;
                var args = new GlobalMouseEventArgs(button, Cursor(display), 0, time) { IsSelfInjected = own };
                (down ? MouseDown : MouseUp)?.Invoke(this, args);
                break;
            }
            case XI_RawMotion:
                if (MouseMove is { } move) move(this, new GlobalMouseEventArgs(GameKey.None, Cursor(display), 0, time));
                break;
        }
    }

    private bool IsOwnInjection(int source, bool button, int code, bool down)
    {
        if (!_xtestDevices.Contains(source) || !XTestInjectionLedger.TryConsume(button, code, down)) return false;
        IgnoredInjectionCount++;
        return true;
    }

    private PixelPoint Cursor(nint display)
    {
        XQueryPointer(display, _root, out _, out _, out var x, out var y, out _, out _, out _);
        return new PixelPoint(x, y);
    }

    /// <summary>XTest 虚拟从设备（“Virtual core XTEST keyboard / pointer”）的设备号</summary>
    private void FindXTestDevices(nint display)
    {
        var info = XIQueryDevice(display, XIAllDevices, out var n);
        if (info == 0) return;
        try
        {
            // XIDeviceInfo（x86_64，40 字节）：deviceid@0 name@8
            for (var i = 0; i < n; i++)
            {
                var p = (byte*)info + i * 40;
                var name = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(*(nint*)(p + 8)) ?? "";
                if (name.Contains("XTEST", StringComparison.OrdinalIgnoreCase)) _xtestDevices.Add(*(int*)p);
            }
        }
        finally
        {
            XIFreeDeviceInfo(info);
        }
    }

    public void Dispose()
    {
        _stop = true;
        if (_thread != Thread.CurrentThread) _thread.Join(1000);
    }
}
