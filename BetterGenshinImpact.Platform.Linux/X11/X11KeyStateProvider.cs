using BetterGenshinImpact.Platform.Linux.X11.Native;
using static BetterGenshinImpact.Platform.Linux.X11.Native.Xlib;

namespace BetterGenshinImpact.Platform.Linux.X11;

/// <summary>
/// XQueryKeymap / XQueryPointer 实现的系统键状态，相当于 GetAsyncKeyState。
/// XWayland 下只有焦点在 X 客户端（如 Wine 游戏窗口）时才能观察到按键
/// </summary>
public sealed unsafe class X11KeyStateProvider : IKeyStateProvider, IDisposable
{
    private const uint Button1Mask = 1u << 8, Button2Mask = 1u << 9, Button3Mask = 1u << 10, Button4Mask = 1u << 11, Button5Mask = 1u << 12;

    private readonly X11Connection _c;
    private readonly bool _ownsConnection;

    /// <summary>keycode → GameKey，按当前键盘映射建立</summary>
    private readonly GameKey[] _fromKeyCode = new GameKey[256];

    public X11KeyStateProvider(X11Connection? connection = null, bool ownsConnection = false)
    {
        _c = connection ?? new X11Connection();
        _ownsConnection = connection == null || ownsConnection;
        BuildKeyCodeMap();
    }

    private void BuildKeyCodeMap()
    {
        lock (_c.Gate)
        {
            // 后写覆盖先写：VK_LSHIFT(0xA0) 覆盖 VK_SHIFT(0x10)，与 Windows 低级钩子报告左右键一致
            foreach (var key in Enum.GetValues<GameKey>())
            {
                if (KeyModifierHelper.IsMouseButton(key) || key == GameKey.None) continue;
                var code = KeyMap.ToKeyCode(_c.Display, key);
                if (code is > 0 and < 256)
                {
                    _fromKeyCode[code] = key;
                }
            }
        }
    }

    public bool IsKeyDown(GameKey key)
    {
        lock (_c.Gate)
        {
            if (KeyModifierHelper.IsMouseButton(key))
            {
                XQueryPointer(_c.Display, _c.Root, out _, out _, out _, out _, out _, out _, out var mask);
                return (mask & ButtonMask(key)) != 0;
            }

            var code = KeyMap.ToKeyCode(_c.Display, key);
            if (code == 0) return false;
            var keys = stackalloc byte[32];
            XQueryKeymap(_c.Display, keys);
            return (keys[code >> 3] & (1 << (code & 7))) != 0;
        }
    }

    public IReadOnlySet<GameKey> GetPressedKeys()
    {
        var set = new HashSet<GameKey>();
        lock (_c.Gate)
        {
            var keys = stackalloc byte[32];
            XQueryKeymap(_c.Display, keys);
            for (var code = 8; code < 256; code++)
            {
                if ((keys[code >> 3] & (1 << (code & 7))) == 0) continue;
                var key = _fromKeyCode[code];
                if (key == GameKey.None) continue;
                set.Add(key);
                // 与 GetAsyncKeyState 一致：左右修饰键按下时通用修饰键也视为按下
                switch (key)
                {
                    case GameKey.VK_LSHIFT or GameKey.VK_RSHIFT: set.Add(GameKey.VK_SHIFT); break;
                    case GameKey.VK_LCONTROL or GameKey.VK_RCONTROL: set.Add(GameKey.VK_CONTROL); break;
                    case GameKey.VK_LMENU or GameKey.VK_RMENU: set.Add(GameKey.VK_MENU); break;
                }
            }

            if (XQueryPointer(_c.Display, _c.Root, out _, out _, out _, out _, out _, out _, out var mask))
            {
                if ((mask & Button1Mask) != 0) set.Add(GameKey.VK_LBUTTON);
                if ((mask & Button2Mask) != 0) set.Add(GameKey.VK_MBUTTON);
                if ((mask & Button3Mask) != 0) set.Add(GameKey.VK_RBUTTON);
            }
        }

        return set;
    }

    public PixelPoint? GetCursorPosition()
    {
        lock (_c.Gate)
        {
            return XQueryPointer(_c.Display, _c.Root, out _, out _, out var x, out var y, out _, out _, out _)
                ? new PixelPoint(x, y)
                : null;
        }
    }

    /// <summary>X11 核心协议只有 5 个按钮（4、5 为滚轮），侧键 VK_XBUTTON1/2 无法用掩码查询，返回 0</summary>
    private static uint ButtonMask(GameKey key) => key switch
    {
        GameKey.VK_LBUTTON => Button1Mask,
        GameKey.VK_MBUTTON => Button2Mask,
        GameKey.VK_RBUTTON => Button3Mask,
        _ => 0,
    };

    public void Dispose()
    {
        if (_ownsConnection) _c.Dispose();
    }
}
