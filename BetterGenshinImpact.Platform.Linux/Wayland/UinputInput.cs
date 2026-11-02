using System.Runtime.InteropServices;
using BetterGenshinImpact.Core.Input;
using static BetterGenshinImpact.Platform.Linux.Wayland.LinuxInputNative;

namespace BetterGenshinImpact.Platform.Linux.Wayland;

/// <summary>
/// uinput 虚拟键鼠（内核层，任何合成器都当成真实设备，输入发给当前焦点窗口）。
/// 建两个设备：键盘 + 相对鼠标（视角转动用 REL_X/REL_Y），以及绝对定位指针（ABS_X/ABS_Y 0..65535，合成器映射到全部输出的外接矩形）。
/// 需要 /dev/uinput 可写（udev 规则或 input 组 + uaccess），否则构造时抛出带说明的异常
/// </summary>
public sealed unsafe class UinputDevice : IDisposable
{
    public const int AbsMax = 65535;
    private readonly object _gate = new();
    private int _rel = -1;
    private int _abs = -1;

    public UinputDevice()
    {
        _rel = Create(absolute: false);
        try
        {
            _abs = Create(absolute: true);
        }
        catch
        {
            Close(ref _rel);
            throw;
        }
    }

    public bool IsDisposed => _rel < 0;

    /// <summary>/dev/uinput 不可写时给用户的说明</summary>
    public static string PermissionHint() =>
        "无法打开 /dev/uinput。Wayland 下键鼠操作通过 uinput 虚拟设备注入，请加载 uinput 模块并允许当前用户写入，例如：" +
        "echo uinput | sudo tee /etc/modules-load.d/uinput.conf；" +
        "echo 'KERNEL==\"uinput\", GROUP=\"input\", MODE=\"0660\", OPTIONS+=\"static_node=uinput\"' | sudo tee /etc/udev/rules.d/99-bgi-uinput.rules；" +
        "sudo usermod -aG input $USER，然后重新登录";

    /// <summary>/dev/uinput 是否存在且可写</summary>
    public static bool IsAvailable(out string? problem)
    {
        var fd = open("/dev/uinput", O_WRONLY | O_NONBLOCK | O_CLOEXEC);
        if (fd < 0)
        {
            problem = PermissionHint() + $"（{Errno()}）";
            return false;
        }

        close(fd);
        problem = null;
        return true;
    }

    private static void Check(int r, string what)
    {
        if (r < 0) throw new InvalidOperationException($"uinput {what} 失败：{Errno()}");
    }

    private static int Create(bool absolute)
    {
        var fd = open("/dev/uinput", O_WRONLY | O_NONBLOCK | O_CLOEXEC);
        if (fd < 0) throw new InvalidOperationException(PermissionHint() + $"（{Errno()}）");
        try
        {
            Check(ioctl(fd, UI_SET_EVBIT, EV_KEY), "UI_SET_EVBIT");
            Check(ioctl(fd, UI_SET_EVBIT, EV_SYN), "UI_SET_EVBIT");
            foreach (var b in new[] { EvdevKeyMap.BTN_LEFT, EvdevKeyMap.BTN_RIGHT, EvdevKeyMap.BTN_MIDDLE, EvdevKeyMap.BTN_SIDE, EvdevKeyMap.BTN_EXTRA })
                Check(ioctl(fd, UI_SET_KEYBIT, b), "UI_SET_KEYBIT");
            var setup = stackalloc int[7];
            if (absolute)
            {
                Check(ioctl(fd, UI_SET_EVBIT, EV_ABS), "UI_SET_EVBIT");
                foreach (var axis in new[] { ABS_X, ABS_Y })
                {
                    Check(ioctl(fd, UI_SET_ABSBIT, axis), "UI_SET_ABSBIT");
                    new Span<int>(setup, 7).Clear();
                    *(ushort*)setup = axis;
                    setup[1] = 0; // value
                    setup[2] = 0; // minimum
                    setup[3] = AbsMax; // maximum
                    Check(ioctl(fd, UI_ABS_SETUP, setup), "UI_ABS_SETUP");
                }
            }
            else
            {
                Check(ioctl(fd, UI_SET_EVBIT, EV_REL), "UI_SET_EVBIT");
                foreach (var axis in new[] { REL_X, REL_Y, REL_WHEEL }) Check(ioctl(fd, UI_SET_RELBIT, axis), "UI_SET_RELBIT");
                foreach (var code in EvdevKeyMap.AllCodes.Where(c => c < EvdevKeyMap.BTN_LEFT)) Check(ioctl(fd, UI_SET_KEYBIT, code), "UI_SET_KEYBIT");
            }

            var s = stackalloc byte[92];
            new Span<byte>(s, 92).Clear();
            *(ushort*)s = 0x06; // BUS_VIRTUAL
            *(ushort*)(s + 2) = 0x1209;
            *(ushort*)(s + 4) = absolute ? (ushort)0xB602 : (ushort)0xB601;
            var name = System.Text.Encoding.UTF8.GetBytes(Evdev.VirtualDeviceName + (absolute ? " (abs)" : ""));
            name.AsSpan(0, Math.Min(79, name.Length)).CopyTo(new Span<byte>(s + 8, 79));
            Check(ioctl(fd, UI_DEV_SETUP, s), "UI_DEV_SETUP");
            Check(ioctl(fd, UI_DEV_CREATE, 0), "UI_DEV_CREATE");
            return fd;
        }
        catch
        {
            close(fd);
            throw;
        }
    }

    private static void Emit(int fd, ushort type, ushort code, int value)
    {
        var e = new InputEvent { Type = type, Code = code, Value = value };
        write(fd, &e, sizeof(InputEvent));
    }

    private void Sync(int fd) => Emit(fd, EV_SYN, SYN_REPORT, 0);

    public void Key(int code, bool down)
    {
        lock (_gate)
        {
            if (_rel < 0) return;
            Emit(_rel, EV_KEY, (ushort)code, down ? 1 : 0);
            Sync(_rel);
        }
    }

    public void MoveBy(int dx, int dy)
    {
        lock (_gate)
        {
            if (_rel < 0) return;
            if (dx != 0) Emit(_rel, EV_REL, REL_X, dx);
            if (dy != 0) Emit(_rel, EV_REL, REL_Y, dy);
            Sync(_rel);
        }
    }

    public void Wheel(int clicks)
    {
        lock (_gate)
        {
            if (_rel < 0) return;
            Emit(_rel, EV_REL, REL_WHEEL, clicks);
            Sync(_rel);
        }
    }

    /// <summary>绝对定位，0..<see cref="AbsMax"/> 归一化到合成器全部输出的外接矩形</summary>
    public void MoveTo(int x, int y)
    {
        lock (_gate)
        {
            if (_abs < 0) return;
            Emit(_abs, EV_ABS, ABS_X, Math.Clamp(x, 0, AbsMax));
            Emit(_abs, EV_ABS, ABS_Y, Math.Clamp(y, 0, AbsMax));
            Sync(_abs);
        }
    }

    private static void Close(ref int fd)
    {
        if (fd < 0) return;
        ioctl(fd, UI_DEV_DESTROY, 0);
        close(fd);
        fd = -1;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            Close(ref _rel);
            Close(ref _abs);
        }
    }
}

/// <summary>
/// uinput 输入通道。像素坐标为合成器全局坐标（物理像素），按 <paramref name="layoutBounds"/>（全部输出外接矩形）换算成绝对坐标
/// </summary>
public sealed class UinputInputChannel(UinputDevice device, Func<PixelRect> layoutBounds) : InputChannelBase
{
    public const int PressHoldMs = 30;

    /// <summary>
    /// uinput 是真实设备：需要游戏在前台；能做相对移动；IsKeyDown 只反映本通道按下的键。
    /// Wine 把绝对移动转成相对增量的问题同样存在，按会转动视角处理
    /// </summary>
    public override InputCapabilities Capabilities { get; } = new(RequiresForeground: true, SupportsRelativeMotion: true,
        SupportsKeyStateQuery: false, AbsoluteMoveAffectsCamera: true);

    private void Key(GameKey key, bool down)
    {
        var code = EvdevKeyMap.ToEvdev(key);
        if (code == 0)
        {
            WarnUnsupported($"键 {key} 没有对应的 Linux 输入事件码");
            return;
        }

        device.Key(code, down);
    }

    protected override void OnKeyDown(GameKey key) => Key(key, true);

    protected override void OnKeyUp(GameKey key) => Key(key, false);

    protected override void OnKeyPress(GameKey key)
    {
        Key(key, true);
        Thread.Sleep(PressHoldMs);
        Key(key, false);
    }

    private static int Button(InputMouseButton b) => b switch
    {
        InputMouseButton.Left => EvdevKeyMap.BTN_LEFT,
        InputMouseButton.Right => EvdevKeyMap.BTN_RIGHT,
        InputMouseButton.Middle => EvdevKeyMap.BTN_MIDDLE,
        InputMouseButton.X1 => EvdevKeyMap.BTN_SIDE,
        _ => EvdevKeyMap.BTN_EXTRA,
    };

    protected override void OnMouseButton(InputMouseButton button, bool down) => device.Key(Button(button), down);

    protected override void OnMouseClick(InputMouseButton button)
    {
        device.Key(Button(button), true);
        Thread.Sleep(PressHoldMs);
        device.Key(Button(button), false);
    }

    protected override void OnMoveBy(int dx, int dy) => device.MoveBy(dx, dy);

    /// <summary>Win32 语义的 0..65535 归一化坐标（相对主屏）：这里按全部输出外接矩形处理</summary>
    protected override void OnMoveTo(double absX, double absY) =>
        device.MoveTo((int)Math.Round(absX * UinputDevice.AbsMax / 65535d), (int)Math.Round(absY * UinputDevice.AbsMax / 65535d));

    protected override void OnMoveToPixel(int screenX, int screenY)
    {
        var (x, y) = ToAbsolute(layoutBounds(), screenX, screenY);
        device.MoveTo(x, y);
    }

    /// <summary>合成器全局像素坐标 → uinput 绝对坐标（0..AbsMax 对应外接矩形左上到右下）</summary>
    public static (int X, int Y) ToAbsolute(PixelRect bounds, int screenX, int screenY)
    {
        if (bounds.Width <= 1 || bounds.Height <= 1) return (0, 0);
        var x = (screenX - bounds.X) * (double)UinputDevice.AbsMax / (bounds.Width - 1);
        var y = (screenY - bounds.Y) * (double)UinputDevice.AbsMax / (bounds.Height - 1);
        return ((int)Math.Round(x), (int)Math.Round(y));
    }

    protected override void OnScroll(int clicks) => device.Wheel(clicks);
}

/// <summary>uinput 输入后端（Wayland 原生窗口；XWayland / xwayland-satellite 下的 X 窗口仍用 XTest）</summary>
public sealed class UinputInputBackend : IInputBackend
{
    private readonly UinputDevice _device;

    public UinputInputBackend(Func<PixelRect> layoutBounds)
    {
        _device = new UinputDevice();
        Foreground = new UinputInputChannel(_device, layoutBounds);
    }

    public InputBackendKind Kind => InputBackendKind.Uinput;

    public IInputChannel Foreground { get; }

    public IInputChannel? Background => null;

    public void ReleaseAll() => Foreground.ReleaseAll();

    public void Dispose()
    {
        ReleaseAll();
        _device.Dispose();
    }
}
