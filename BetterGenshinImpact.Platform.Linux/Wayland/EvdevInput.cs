using System.Text;
using static BetterGenshinImpact.Platform.Linux.Wayland.LinuxInputNative;

namespace BetterGenshinImpact.Platform.Linux.Wayland;

/// <summary>/dev/input/event* 中的一个设备</summary>
public sealed record EvdevDeviceInfo(string Path, string Name, bool IsKeyboard, bool IsMouse);

/// <summary>
/// evdev 设备枚举与权限检查。读取 /dev/input/event* 需要当前用户在 input 组（或有 ACL）；
/// 不依赖合成器，所以在任何 Wayland 合成器（含 niri）下都能做全局热键
/// </summary>
public static unsafe class Evdev
{
    /// <summary>本程序 uinput 虚拟设备的名字：监听时据此把事件标为本进程注入</summary>
    public const string VirtualDeviceName = "BetterGI virtual input";

    public static IReadOnlyList<EvdevDeviceInfo> Enumerate(out string? problem)
    {
        problem = null;
        var list = new List<EvdevDeviceInfo>();
        if (!Directory.Exists("/dev/input"))
        {
            problem = "没有 /dev/input（容器或无输入设备环境）";
            return list;
        }

        var denied = 0;
        foreach (var path in Directory.GetFiles("/dev/input", "event*").OrderBy(p => p.Length).ThenBy(p => p, StringComparer.Ordinal))
        {
            var fd = open(path, O_RDONLY | O_NONBLOCK | O_CLOEXEC);
            if (fd < 0)
            {
                denied++;
                continue;
            }

            try
            {
                list.Add(Describe(fd, path));
            }
            finally
            {
                close(fd);
            }
        }

        if (list.Count == 0 && denied > 0)
        {
            problem = PermissionHint();
        }

        return list;
    }

    /// <summary>读不了输入设备时给用户的说明</summary>
    public static string PermissionHint() =>
        "无法读取 /dev/input/event*（权限不足）。Wayland 下全局热键直接读键盘设备，请把当前用户加入 input 组后重新登录：" +
        "sudo usermod -aG input $USER";

    internal static EvdevDeviceInfo Describe(int fd, string path)
    {
        var name = new byte[256];
        string devName;
        fixed (byte* p = name)
        {
            var n = ioctl(fd, EVIOCGNAME((uint)name.Length), p);
            var end = Array.IndexOf(name, (byte)0);
            devName = n > 0 ? Encoding.UTF8.GetString(name, 0, end >= 0 ? end : Math.Min(n, name.Length)) : "";
        }

        var keys = new byte[KEY_MAX / 8 + 1];
        var rel = new byte[4];
        fixed (byte* k = keys)
        fixed (byte* r = rel)
        {
            ioctl(fd, EVIOCGBIT(EV_KEY, (uint)keys.Length), k);
            ioctl(fd, EVIOCGBIT(EV_REL, (uint)rel.Length), r);
        }

        // 键盘：有 A、Z、空格；鼠标：有左键和相对 X/Y
        var isKeyboard = TestBit(keys, 30) && TestBit(keys, 44) && TestBit(keys, 57);
        var isMouse = TestBit(keys, EvdevKeyMap.BTN_LEFT) && TestBit(rel, REL_X) && TestBit(rel, REL_Y);
        return new EvdevDeviceInfo(path, devName, isKeyboard, isMouse);
    }
}

/// <summary>
/// 基于 evdev 的全局键鼠监听（只读，不抓取设备，游戏照常收到输入）。
/// 一个后台线程 poll 所有键盘 / 鼠标设备；本程序 uinput 虚拟设备的事件标记为 <see cref="GlobalKeyEventArgs.IsSelfInjected"/>。
/// evdev 没有屏幕坐标：鼠标事件的位置取 <paramref name="cursor"/>（例如合成器 / X 指针位置），拿不到时为 (0,0)
/// </summary>
public sealed unsafe class EvdevGlobalInputHook : IGlobalInputHook, IKeyStateProvider
{
    private readonly List<(int Fd, EvdevDeviceInfo Info)> _devices = [];
    private readonly Thread _thread;
    private readonly HashSet<GameKey> _pressed = [];
    private readonly Func<PixelPoint?>? _cursor;
    private volatile bool _stop;

    public EvdevGlobalInputHook(Func<PixelPoint?>? cursor = null)
    {
        _cursor = cursor;
        var devices = Evdev.Enumerate(out var problem);
        foreach (var d in devices.Where(d => d.IsKeyboard || d.IsMouse))
        {
            var fd = open(d.Path, O_RDONLY | O_NONBLOCK | O_CLOEXEC);
            if (fd >= 0) _devices.Add((fd, d));
        }

        if (_devices.Count == 0)
        {
            throw new InvalidOperationException(problem ?? "没有找到可读的键盘 / 鼠标输入设备（/dev/input/event*）。" + Evdev.PermissionHint());
        }

        _thread = new Thread(Loop) { IsBackground = true, Name = "evdev-input" };
        _thread.Start();
    }

    /// <summary>正在监听的设备</summary>
    public IReadOnlyList<EvdevDeviceInfo> Devices => _devices.Select(d => d.Info).ToList();

    public event EventHandler<GlobalKeyEventArgs>? KeyDown;
    public event EventHandler<GlobalKeyEventArgs>? KeyUp;
    public event EventHandler<GlobalMouseEventArgs>? MouseDown;
    public event EventHandler<GlobalMouseEventArgs>? MouseUp;
    public event EventHandler<GlobalMouseEventArgs>? MouseMove;
    public event EventHandler<GlobalMouseEventArgs>? MouseWheel;

    public bool SupportsWheel => true;

    private KeyModifiers Modifiers()
    {
        var m = KeyModifiers.None;
        lock (_pressed)
        {
            if (_pressed.Contains(GameKey.VK_LSHIFT) || _pressed.Contains(GameKey.VK_RSHIFT)) m |= KeyModifiers.Shift;
            if (_pressed.Contains(GameKey.VK_LCONTROL) || _pressed.Contains(GameKey.VK_RCONTROL)) m |= KeyModifiers.Control;
            if (_pressed.Contains(GameKey.VK_LMENU) || _pressed.Contains(GameKey.VK_RMENU)) m |= KeyModifiers.Alt;
        }

        return m;
    }

    private void Loop()
    {
        var fds = new PollFd[_devices.Count];
        for (var i = 0; i < fds.Length; i++) fds[i] = new PollFd { Fd = _devices[i].Fd, Events = POLLIN };
        var ev = new InputEvent[64];
        while (!_stop)
        {
            int n;
            fixed (PollFd* p = fds) n = poll(p, (nuint)fds.Length, 200);
            if (n <= 0) continue;
            for (var i = 0; i < fds.Length; i++)
            {
                if ((fds[i].Revents & POLLIN) == 0) continue;
                nint bytes;
                fixed (InputEvent* e = ev) bytes = read(fds[i].Fd, e, sizeof(InputEvent) * ev.Length);
                if (bytes <= 0) continue;
                var self = _devices[i].Info.Name == Evdev.VirtualDeviceName;
                for (var j = 0; j < bytes / sizeof(InputEvent); j++) Dispatch(ev[j], self);
            }
        }
    }

    private void Dispatch(in InputEvent e, bool self)
    {
        var ts = (int)(e.TvSec * 1000 + e.TvUsec / 1000);
        if (e.Type == EV_KEY && e.Value != 2)
        {
            var key = EvdevKeyMap.FromEvdev(e.Code);
            if (key == GameKey.None) return;
            var down = e.Value == 1;
            lock (_pressed)
            {
                if (down) _pressed.Add(key);
                else _pressed.Remove(key);
            }

            if (e.Code is >= EvdevKeyMap.BTN_LEFT and <= EvdevKeyMap.BTN_EXTRA)
            {
                var args = new GlobalMouseEventArgs(key, _cursor?.Invoke() ?? default, 0, ts) { IsSelfInjected = self };
                (down ? MouseDown : MouseUp)?.Invoke(this, args);
            }
            else
            {
                var args = new GlobalKeyEventArgs(key, Modifiers(), ts) { IsSelfInjected = self };
                (down ? KeyDown : KeyUp)?.Invoke(this, args);
            }
        }
        else if (e.Type == EV_REL)
        {
            if (e.Code == REL_WHEEL)
            {
                MouseWheel?.Invoke(this, new GlobalMouseEventArgs(GameKey.None, _cursor?.Invoke() ?? default, e.Value * 120, ts) { IsSelfInjected = self });
            }
            else if (e.Code is REL_X or REL_Y && MouseMove is { } move)
            {
                move(this, new GlobalMouseEventArgs(GameKey.None, _cursor?.Invoke() ?? default, 0, ts) { IsSelfInjected = self });
            }
        }
    }

    public bool IsKeyDown(GameKey key)
    {
        lock (_pressed) return _pressed.Contains(key);
    }

    public IReadOnlySet<GameKey> GetPressedKeys()
    {
        lock (_pressed) return _pressed.ToHashSet();
    }

    public PixelPoint? GetCursorPosition() => _cursor?.Invoke();

    public void Dispose()
    {
        _stop = true;
        _thread.Join(1000);
        foreach (var (fd, _) in _devices) close(fd);
        _devices.Clear();
    }
}
