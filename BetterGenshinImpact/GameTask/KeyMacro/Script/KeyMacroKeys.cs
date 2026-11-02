using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Platform;

namespace BetterGenshinImpact.GameTask.KeyMacro.Script;

/// <summary>
/// 按键宏的按键名、鼠标键名与虚拟键的对照，以及界面上显示的易读名称。
/// 名称都不区分大小写；写回脚本时使用 <see cref="GetName"/> 返回的规范名称
/// </summary>
public static class KeyMacroKeys
{
    /// <summary>
    /// 按键名 → 虚拟键（不区分大小写）。同一个虚拟键的第一个名称是规范名称
    /// </summary>
    private static readonly Dictionary<string, GameKey> KeyAliases = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 虚拟键 → 规范名称
    /// </summary>
    private static readonly Dictionary<GameKey, string> CanonicalNames = [];

    /// <summary>
    /// 虚拟键 → 易读名称
    /// </summary>
    private static readonly Dictionary<GameKey, string> DisplayNames = [];

    /// <summary>
    /// 鼠标键的虚拟键名，键盘指令里出现时提示改用鼠标指令
    /// </summary>
    private static readonly HashSet<string> MouseKeyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "lbutton", "rbutton", "mbutton", "xbutton1", "xbutton2",
        "vk_lbutton", "vk_rbutton", "vk_mbutton", "vk_xbutton1", "vk_xbutton2"
    };

    /// <summary>
    /// 鼠标键名 → 鼠标键（不区分大小写）
    /// </summary>
    private static readonly Dictionary<string, KeyMacroMouseButton> ButtonAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["left"] = KeyMacroMouseButton.Left,
        ["right"] = KeyMacroMouseButton.Right,
        ["middle"] = KeyMacroMouseButton.Middle,
        ["x1"] = KeyMacroMouseButton.X1,
        ["xbutton1"] = KeyMacroMouseButton.X1,
        ["x2"] = KeyMacroMouseButton.X2,
        ["xbutton2"] = KeyMacroMouseButton.X2
    };

    /// <summary>
    /// 补全与速查使用的按键名（规范名称），按常用程度排列
    /// </summary>
    public static IReadOnlyList<string> CompletionKeyNames { get; }

    /// <summary>
    /// 补全与速查使用的鼠标键名
    /// </summary>
    public static IReadOnlyList<string> CompletionButtonNames { get; } = ["left", "right", "middle", "x1", "x2"];

    static KeyMacroKeys()
    {
        // 字母、数字、功能键、小键盘按虚拟键编号连续排列
        for (var i = 0; i < 26; i++)
        {
            var name = ((char)('a' + i)).ToString();
            Register((GameKey)(0x41 + i), name.ToUpperInvariant(), name);
        }

        for (var i = 0; i < 10; i++)
        {
            Register((GameKey)(0x30 + i), i.ToString(), i.ToString());
        }

        for (var i = 0; i < 24; i++)
        {
            Register((GameKey)(0x70 + i), $"F{i + 1}", $"f{i + 1}");
        }

        for (var i = 0; i < 10; i++)
        {
            Register((GameKey)(0x60 + i), $"小键盘 {i}", $"numpad{i}");
        }

        // 常用功能键：第一个名称为规范名称，后面是兼容写法
        Register(GameKey.VK_SPACE, "Space", "space");
        Register(GameKey.VK_RETURN, "Enter", "enter", "return");
        Register(GameKey.VK_TAB, "Tab", "tab");
        Register(GameKey.VK_ESCAPE, "Esc", "esc", "escape");
        Register(GameKey.VK_BACK, "Backspace", "backspace", "back");
        Register(GameKey.VK_DELETE, "Delete", "delete", "del");
        Register(GameKey.VK_INSERT, "Insert", "insert", "ins");
        Register(GameKey.VK_HOME, "Home", "home");
        Register(GameKey.VK_END, "End", "end");
        Register(GameKey.VK_PRIOR, "PageUp", "pageup", "pgup", "prior");
        Register(GameKey.VK_NEXT, "PageDown", "pagedown", "pgdn", "next");
        Register(GameKey.VK_UP, "↑", "up");
        Register(GameKey.VK_DOWN, "↓", "down");
        Register(GameKey.VK_LEFT, "←", "left");
        Register(GameKey.VK_RIGHT, "→", "right");
        Register(GameKey.VK_SHIFT, "Shift", "shift");
        Register(GameKey.VK_LSHIFT, "左 Shift", "lshift");
        Register(GameKey.VK_RSHIFT, "右 Shift", "rshift");
        Register(GameKey.VK_CONTROL, "Ctrl", "ctrl", "control");
        Register(GameKey.VK_LCONTROL, "左 Ctrl", "lctrl", "lcontrol");
        Register(GameKey.VK_RCONTROL, "右 Ctrl", "rctrl", "rcontrol");
        Register(GameKey.VK_MENU, "Alt", "alt", "menu");
        Register(GameKey.VK_LMENU, "左 Alt", "lalt", "lmenu");
        Register(GameKey.VK_RMENU, "右 Alt", "ralt", "rmenu");
        Register(GameKey.VK_CAPITAL, "CapsLock", "capslock", "capital");
        Register(GameKey.VK_LWIN, "左 Win", "lwin");
        Register(GameKey.VK_RWIN, "右 Win", "rwin");
        Register(GameKey.VK_APPS, "菜单键", "apps");
        Register(GameKey.VK_PAUSE, "Pause", "pause");
        Register(GameKey.VK_SNAPSHOT, "PrintScreen", "printscreen", "snapshot");
        Register(GameKey.VK_NUMLOCK, "NumLock", "numlock");
        Register(GameKey.VK_SCROLL, "ScrollLock", "scrolllock");
        Register(GameKey.VK_MULTIPLY, "小键盘 *", "multiply");
        Register(GameKey.VK_ADD, "小键盘 +", "add");
        Register(GameKey.VK_SUBTRACT, "小键盘 -", "subtract");
        Register(GameKey.VK_DECIMAL, "小键盘 .", "decimal");
        Register(GameKey.VK_DIVIDE, "小键盘 /", "divide");

        // 符号键只登记易读名称，脚本里使用 oem_* 虚拟键名（通过 TryParseKey 的虚拟键名兜底解析）
        DisplayNames[GameKey.VK_OEM_1] = ";";
        DisplayNames[GameKey.VK_OEM_PLUS] = "=";
        DisplayNames[GameKey.VK_OEM_COMMA] = ",";
        DisplayNames[GameKey.VK_OEM_MINUS] = "-";
        DisplayNames[GameKey.VK_OEM_PERIOD] = ".";
        DisplayNames[GameKey.VK_OEM_2] = "/";
        DisplayNames[GameKey.VK_OEM_3] = "`";
        DisplayNames[GameKey.VK_OEM_4] = "[";
        DisplayNames[GameKey.VK_OEM_5] = "\\";
        DisplayNames[GameKey.VK_OEM_6] = "]";
        DisplayNames[GameKey.VK_OEM_7] = "'";

        // 补全列表：常用功能键在前，其后是字母、数字和功能键
        string[] common =
        [
            "space", "lshift", "lctrl", "lalt", "tab", "esc", "enter", "backspace",
            "up", "down", "left", "right", "shift", "ctrl", "alt", "capslock"
        ];
        CompletionKeyNames = common
            .Concat(Enumerable.Range(0, 26).Select(i => ((char)('a' + i)).ToString()))
            .Concat(Enumerable.Range(0, 10).Select(i => i.ToString()))
            .Concat(Enumerable.Range(1, 12).Select(i => $"f{i}"))
            .Concat(["delete", "insert", "home", "end", "pageup", "pagedown"])
            .Concat(Enumerable.Range(0, 10).Select(i => $"numpad{i}"))
            .ToList();
    }

    /// <summary>
    /// 登记一个按键：易读名称与名称列表，第一个名称为规范名称
    /// </summary>
    private static void Register(GameKey vk, string displayName, params string[] names)
    {
        DisplayNames[vk] = displayName;
        CanonicalNames.TryAdd(vk, names[0]);
        foreach (var name in names)
        {
            KeyAliases.TryAdd(name, vk);
        }
    }

    /// <summary>
    /// 是否为鼠标键的虚拟键名（lbutton、xbutton1 等）
    /// </summary>
    public static bool IsMouseKeyName(string name) => MouseKeyNames.Contains(name.Trim());

    /// <summary>
    /// 解析键盘按键名。先查常用名称，再按 Windows 虚拟键名（可带或不带 vk_ 前缀）解析；鼠标键不算键盘按键
    /// </summary>
    /// <param name="name">按键名</param>
    /// <param name="vk">解析出的虚拟键</param>
    /// <returns>是否解析成功</returns>
    public static bool TryParseKey(string name, out GameKey vk)
    {
        vk = default;
        name = name.Trim();
        if (name.Length == 0 || IsMouseKeyName(name))
        {
            return false;
        }

        if (KeyAliases.TryGetValue(name, out vk))
        {
            return true;
        }

        // 虚拟键名兜底：只接受字母开头、由字母数字下划线组成的名称，避免 Enum.TryParse 把数字字符串当作枚举值
        if (!char.IsAsciiLetter(name[0]) || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
        {
            return false;
        }

        var enumName = name.StartsWith("vk_", StringComparison.OrdinalIgnoreCase) ? name : "VK_" + name;
        // GameKey 比原 Vanara VK 多一个 None（0），不是按键
        return Enum.TryParse(enumName, true, out vk) && vk != GameKey.None && Enum.IsDefined(vk);
    }

    /// <summary>
    /// 虚拟键的规范名称，写回脚本时使用
    /// </summary>
    public static string GetName(GameKey vk)
    {
        if (CanonicalNames.TryGetValue(vk, out var name))
        {
            return name;
        }

        // 同值别名的枚举名与原 Vanara VK 保持一致（GameKey 的 ToString 对这两个值取另一个别名）
        var enumName = vk switch
        {
            GameKey.VK_KANA => "VK_KANA",
            GameKey.VK_KANJI => "VK_KANJI",
            _ => vk.ToString()
        };
        return (enumName.StartsWith("VK_", StringComparison.Ordinal) ? enumName[3..] : enumName).ToLowerInvariant();
    }

    /// <summary>
    /// 虚拟键的易读名称，界面上显示
    /// </summary>
    public static string GetDisplayName(GameKey vk)
    {
        return DisplayNames.TryGetValue(vk, out var name) ? name : GetName(vk).ToUpperInvariant();
    }

    /// <summary>
    /// 解析鼠标键名
    /// </summary>
    /// <param name="name">鼠标键名</param>
    /// <param name="button">解析出的鼠标键</param>
    /// <returns>是否解析成功</returns>
    public static bool TryParseButton(string name, out KeyMacroMouseButton button)
    {
        return ButtonAliases.TryGetValue(name.Trim(), out button);
    }

    /// <summary>
    /// 鼠标键的规范名称，写回脚本时使用
    /// </summary>
    public static string GetButtonName(KeyMacroMouseButton button) => button switch
    {
        KeyMacroMouseButton.Right => "right",
        KeyMacroMouseButton.Middle => "middle",
        KeyMacroMouseButton.X1 => "x1",
        KeyMacroMouseButton.X2 => "x2",
        _ => "left"
    };

    /// <summary>
    /// 鼠标键的易读名称，界面上显示
    /// </summary>
    public static string GetButtonDisplayName(KeyMacroMouseButton button) => button switch
    {
        KeyMacroMouseButton.Right => "右键",
        KeyMacroMouseButton.Middle => "中键",
        KeyMacroMouseButton.X1 => "侧键 1",
        KeyMacroMouseButton.X2 => "侧键 2",
        _ => "左键"
    };
}
