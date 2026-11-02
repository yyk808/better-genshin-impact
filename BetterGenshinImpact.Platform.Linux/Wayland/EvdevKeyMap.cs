namespace BetterGenshinImpact.Platform.Linux.Wayland;

/// <summary>GameKey（Win32 VK）↔ Linux 输入事件码（linux/input-event-codes.h 的 KEY_* / BTN_*）</summary>
public static class EvdevKeyMap
{
    public const int BTN_LEFT = 0x110, BTN_RIGHT = 0x111, BTN_MIDDLE = 0x112, BTN_SIDE = 0x113, BTN_EXTRA = 0x114;

    private static readonly Dictionary<GameKey, int> ToCode = Build();
    private static readonly Dictionary<int, GameKey> FromCode = ToCode.GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.First().Key);

    private static Dictionary<GameKey, int> Build()
    {
        var m = new Dictionary<GameKey, int>
        {
            [GameKey.VK_ESCAPE] = 1, [GameKey.VK_BACK] = 14, [GameKey.VK_TAB] = 15, [GameKey.VK_RETURN] = 28, [GameKey.VK_SPACE] = 57,
            [GameKey.VK_LCONTROL] = 29, [GameKey.VK_LSHIFT] = 42, [GameKey.VK_RSHIFT] = 54, [GameKey.VK_LMENU] = 56, [GameKey.VK_CAPITAL] = 58,
            [GameKey.VK_RCONTROL] = 97, [GameKey.VK_RMENU] = 100, [GameKey.VK_LWIN] = 125, [GameKey.VK_RWIN] = 126, [GameKey.VK_APPS] = 127,
            [GameKey.VK_CONTROL] = 29, [GameKey.VK_SHIFT] = 42, [GameKey.VK_MENU] = 56,
            [GameKey.VK_OEM_MINUS] = 12, [GameKey.VK_OEM_PLUS] = 13, [GameKey.VK_OEM_4] = 26, [GameKey.VK_OEM_6] = 27, [GameKey.VK_OEM_1] = 39,
            [GameKey.VK_OEM_7] = 40, [GameKey.VK_OEM_3] = 41, [GameKey.VK_OEM_5] = 43, [GameKey.VK_OEM_COMMA] = 51, [GameKey.VK_OEM_PERIOD] = 52,
            [GameKey.VK_OEM_2] = 53,
            [GameKey.VK_NUMLOCK] = 69, [GameKey.VK_SCROLL] = 70, [GameKey.VK_MULTIPLY] = 55, [GameKey.VK_SUBTRACT] = 74, [GameKey.VK_ADD] = 78,
            [GameKey.VK_DECIMAL] = 83, [GameKey.VK_DIVIDE] = 98, [GameKey.VK_SNAPSHOT] = 99, [GameKey.VK_PAUSE] = 119,
            [GameKey.VK_HOME] = 102, [GameKey.VK_UP] = 103, [GameKey.VK_PRIOR] = 104, [GameKey.VK_LEFT] = 105, [GameKey.VK_RIGHT] = 106,
            [GameKey.VK_END] = 107, [GameKey.VK_DOWN] = 108, [GameKey.VK_NEXT] = 109, [GameKey.VK_INSERT] = 110, [GameKey.VK_DELETE] = 111,
            [GameKey.VK_LBUTTON] = BTN_LEFT, [GameKey.VK_RBUTTON] = BTN_RIGHT, [GameKey.VK_MBUTTON] = BTN_MIDDLE,
            [GameKey.VK_XBUTTON1] = BTN_SIDE, [GameKey.VK_XBUTTON2] = BTN_EXTRA,
        };
        int[] digits = [11, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        for (var i = 0; i < 10; i++) m[(GameKey)((int)GameKey.VK_0 + i)] = digits[i];
        // QWERTY 行的事件码（与布局无关，按物理位置）
        var letters = new Dictionary<char, int>
        {
            ['Q'] = 16, ['W'] = 17, ['E'] = 18, ['R'] = 19, ['T'] = 20, ['Y'] = 21, ['U'] = 22, ['I'] = 23, ['O'] = 24, ['P'] = 25,
            ['A'] = 30, ['S'] = 31, ['D'] = 32, ['F'] = 33, ['G'] = 34, ['H'] = 35, ['J'] = 36, ['K'] = 37, ['L'] = 38,
            ['Z'] = 44, ['X'] = 45, ['C'] = 46, ['V'] = 47, ['B'] = 48, ['N'] = 49, ['M'] = 50,
        };
        foreach (var (c, code) in letters) m[(GameKey)c] = code;
        int[] f1to10 = [59, 60, 61, 62, 63, 64, 65, 66, 67, 68];
        for (var i = 0; i < 10; i++) m[(GameKey)((int)GameKey.VK_F1 + i)] = f1to10[i];
        m[GameKey.VK_F11] = 87;
        m[GameKey.VK_F12] = 88;
        for (var i = 0; i < 12; i++) m[(GameKey)((int)GameKey.VK_F13 + i)] = 183 + i;
        int[] numpad = [82, 79, 80, 81, 75, 76, 77, 71, 72, 73];
        for (var i = 0; i < 10; i++) m[(GameKey)((int)GameKey.VK_NUMPAD0 + i)] = numpad[i];
        return m;
    }

    /// <summary>事件码；没有对应时为 0</summary>
    public static int ToEvdev(GameKey key) => ToCode.TryGetValue(key, out var c) ? c : 0;

    /// <summary>GameKey；没有对应时为 <see cref="GameKey.None"/>。左右修饰键报告为 VK_LSHIFT / VK_RCONTROL 等</summary>
    public static GameKey FromEvdev(int code) => FromCode.TryGetValue(code, out var k) ? k : GameKey.None;

    /// <summary>可映射的全部事件码（创建 uinput 设备时声明）</summary>
    public static IEnumerable<int> AllCodes => FromCode.Keys;
}
