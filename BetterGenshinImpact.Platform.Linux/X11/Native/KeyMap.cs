namespace BetterGenshinImpact.Platform.Linux.X11.Native;

/// <summary>
/// GameKey（数值 = Win32 VK）↔ X11 KeySym 映射。KeySym 再经 XKeysymToKeycode 换成当前键盘映射下的 keycode。
/// 从平台验证工程移植，键名改为 GameKey 的 VK_* 成员；补充了 F13–F24、小键盘、OEM_102 与媒体键。
/// </summary>
public static class KeyMap
{
    private static readonly Dictionary<GameKey, int> ToSym = new();
    private static readonly Dictionary<int, GameKey> FromSym = new();

    private static GameKey K(GameKey first, int offset) => (GameKey)((int)first + offset);

    static KeyMap()
    {
        void M(GameKey k, int sym)
        {
            ToSym[k] = sym;
            FromSym.TryAdd(sym, k);
        }

        for (int i = 0; i < 26; i++)
        {
            M(K(GameKey.VK_A, i), 'a' + i);
            FromSym.TryAdd('A' + i, K(GameKey.VK_A, i));
        }

        for (int i = 0; i < 10; i++) M(K(GameKey.VK_0, i), '0' + i);
        for (int i = 0; i < 24; i++) M(K(GameKey.VK_F1, i), 0xFFBE + i);
        for (int i = 0; i < 10; i++) M(K(GameKey.VK_NUMPAD0, i), 0xFFB0 + i);

        M(GameKey.VK_BACK, 0xFF08); M(GameKey.VK_TAB, 0xFF09); M(GameKey.VK_CLEAR, 0xFF0B); M(GameKey.VK_RETURN, 0xFF0D);
        M(GameKey.VK_PAUSE, 0xFF13); M(GameKey.VK_SCROLL, 0xFF14); M(GameKey.VK_ESCAPE, 0xFF1B); M(GameKey.VK_DELETE, 0xFFFF);
        M(GameKey.VK_HOME, 0xFF50); M(GameKey.VK_LEFT, 0xFF51); M(GameKey.VK_UP, 0xFF52); M(GameKey.VK_RIGHT, 0xFF53);
        M(GameKey.VK_DOWN, 0xFF54); M(GameKey.VK_PRIOR, 0xFF55); M(GameKey.VK_NEXT, 0xFF56); M(GameKey.VK_END, 0xFF57);
        M(GameKey.VK_SELECT, 0xFF60); M(GameKey.VK_SNAPSHOT, 0xFF61); M(GameKey.VK_EXECUTE, 0xFF62); M(GameKey.VK_INSERT, 0xFF63);
        M(GameKey.VK_APPS, 0xFF67); M(GameKey.VK_HELP, 0xFF6A); M(GameKey.VK_CANCEL, 0xFF69); M(GameKey.VK_NUMLOCK, 0xFF7F);
        M(GameKey.VK_MULTIPLY, 0xFFAA); M(GameKey.VK_ADD, 0xFFAB); M(GameKey.VK_SEPARATOR, 0xFFAC); M(GameKey.VK_SUBTRACT, 0xFFAD);
        M(GameKey.VK_DECIMAL, 0xFFAE); M(GameKey.VK_DIVIDE, 0xFFAF);
        M(GameKey.VK_LSHIFT, 0xFFE1); M(GameKey.VK_RSHIFT, 0xFFE2); M(GameKey.VK_LCONTROL, 0xFFE3); M(GameKey.VK_RCONTROL, 0xFFE4);
        M(GameKey.VK_CAPITAL, 0xFFE5); M(GameKey.VK_LMENU, 0xFFE9); M(GameKey.VK_RMENU, 0xFFEA); M(GameKey.VK_LWIN, 0xFFEB); M(GameKey.VK_RWIN, 0xFFEC);

        // 通用修饰键（VK_SHIFT / VK_CONTROL / VK_MENU）映射到左侧键，只用于发送，不参与反查
        ToSym[GameKey.VK_SHIFT] = 0xFFE1;
        ToSym[GameKey.VK_CONTROL] = 0xFFE3;
        ToSym[GameKey.VK_MENU] = 0xFFE9;

        M(GameKey.VK_SPACE, 0x20); M(GameKey.VK_OEM_1, ';'); M(GameKey.VK_OEM_PLUS, '='); M(GameKey.VK_OEM_COMMA, ',');
        M(GameKey.VK_OEM_MINUS, '-'); M(GameKey.VK_OEM_PERIOD, '.'); M(GameKey.VK_OEM_2, '/'); M(GameKey.VK_OEM_3, '`');
        M(GameKey.VK_OEM_4, '['); M(GameKey.VK_OEM_5, '\\'); M(GameKey.VK_OEM_6, ']'); M(GameKey.VK_OEM_7, '\'');
        M(GameKey.VK_OEM_102, 0x3C); // less（ISO 102 键）

        // XF86 媒体键
        M(GameKey.VK_VOLUME_MUTE, 0x1008FF12); M(GameKey.VK_VOLUME_DOWN, 0x1008FF11); M(GameKey.VK_VOLUME_UP, 0x1008FF13);
        M(GameKey.VK_MEDIA_PLAY_PAUSE, 0x1008FF14); M(GameKey.VK_MEDIA_STOP, 0x1008FF15);
        M(GameKey.VK_MEDIA_PREV_TRACK, 0x1008FF16); M(GameKey.VK_MEDIA_NEXT_TRACK, 0x1008FF17);
    }

    public static int ToKeySym(GameKey k) => ToSym.TryGetValue(k, out var s) ? s : 0;

    public static GameKey FromKeySym(int sym) => FromSym.TryGetValue(sym, out var k) ? k : GameKey.None;

    /// <summary>GameKey → 当前键盘映射下的 keycode，没有对应键时返回 0</summary>
    public static int ToKeyCode(nint display, GameKey k)
    {
        var sym = ToKeySym(k);
        return sym == 0 ? 0 : Xlib.XKeysymToKeycode(display, sym);
    }

    public static GameKey FromKeyCode(nint display, int keycode)
        => FromKeySym((int)Xlib.XkbKeycodeToKeysym(display, (byte)keycode, 0, 0));
}
