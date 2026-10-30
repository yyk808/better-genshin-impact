using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BetterGenshinImpact.Platform;

public static partial class WpfKeyNames
{
    private static readonly Lazy<Dictionary<int, GameKey>> ByValue = new(() =>
        ByName.Values.GroupBy(v => v.Value).ToDictionary(g => g.Key, g => g.First().Vk));

    /// <summary>
    /// 按 <c>Enum.Parse(typeof(Key), name)</c> 的规则解析 WPF Key 名称（区分大小写；也接受数字形式的枚举值），得到 VK。
    /// 没有对应虚拟键的名称（如 LineFeed）返回 true 且 <paramref name="vk"/> 为 <see cref="GameKey.None"/>
    /// </summary>
    public static bool TryParse(string name, out GameKey vk)
    {
        vk = GameKey.None;
        if (string.IsNullOrWhiteSpace(name)) return false;
        name = name.Trim();
        if (ByName.TryGetValue(name, out var e))
        {
            vk = e.Vk;
            return true;
        }

        if (int.TryParse(name, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            vk = ByValue.Value.TryGetValue(value, out var k) ? k : GameKey.None;
            return true;
        }

        return false;
    }

    /// <summary>VK → WPF Key 名称（与 <c>KeyInterop.KeyFromVirtualKey(vk).ToString()</c> 相同），没有对应键时为 "None"</summary>
    public static string ToName(GameKey vk)
    {
        var i = (int)vk;
        return i is >= 0 and < 256 ? VkToName[i] : "None";
    }

    /// <summary>全部 WPF Key 名称（含别名）</summary>
    public static IEnumerable<string> AllNames => ByName.Keys;
}

/// <summary>
/// 快捷键配置文本（HotKeyConfig 中保存的格式，如 "Ctrl + Shift + F11"、"XButton1"、"&lt; None &gt;"）。
/// 解析与格式化规则与主程序 <c>Model.HotKey</c> 相同，键名为 WPF Key 名称
/// </summary>
/// <param name="Key">主键（VK）</param>
/// <param name="Modifiers">修饰键</param>
/// <param name="MouseButton">鼠标侧键热键：VK_XBUTTON1 / VK_XBUTTON2；键盘热键为 None</param>
public readonly record struct HotkeyText(GameKey Key, KeyModifiers Modifiers = KeyModifiers.None, GameKey MouseButton = GameKey.None)
{
    public const string NoneText = "< None >";

    public bool IsEmpty => Key == GameKey.None && Modifiers == KeyModifiers.None && MouseButton == GameKey.None;

    public bool IsMouseButton => MouseButton is GameKey.VK_XBUTTON1 or GameKey.VK_XBUTTON2;

    /// <summary>作为全局热键注册时的组合；鼠标侧键热键没有对应组合，返回 null</summary>
    public HotkeyGesture? ToGesture() => IsMouseButton || Key == GameKey.None ? null : new HotkeyGesture(Key, Modifiers);

    /// <summary>与 Model.HotKey.FromString 相同：空白或 "&lt; None &gt;" 为空热键；键名无效时抛 <see cref="FormatException"/></summary>
    public static HotkeyText Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text == NoneText)
        {
            return default;
        }

        if (text == "XButton1") return new HotkeyText(GameKey.None, KeyModifiers.None, GameKey.VK_XBUTTON1);
        if (text == "XButton2") return new HotkeyText(GameKey.None, KeyModifiers.None, GameKey.VK_XBUTTON2);

        var key = GameKey.None;
        var mods = KeyModifiers.None;
        var mouse = GameKey.None;
        foreach (var part in text.Split('+'))
        {
            var t = part.Trim();
            switch (t)
            {
                case "Ctrl": mods |= KeyModifiers.Control; break;
                case "Shift": mods |= KeyModifiers.Shift; break;
                case "Alt": mods |= KeyModifiers.Alt; break;
                case "Win": mods |= KeyModifiers.Super; break;
                case "XButton1": mouse = GameKey.VK_XBUTTON1; break;
                case "XButton2": mouse = GameKey.VK_XBUTTON2; break;
                default:
                    if (!WpfKeyNames.TryParse(t, out key))
                        throw new FormatException($"无效的快捷键名称：{t}");
                    break;
            }
        }

        return new HotkeyText(key, mods, mouse);
    }

    /// <summary>与 Model.HotKey.ToString 相同</summary>
    public override string ToString()
    {
        if (IsEmpty) return NoneText;
        if (MouseButton != GameKey.None) return KeyModifierHelper.MouseButtonName(MouseButton);
        var s = "";
        if (Modifiers.HasFlag(KeyModifiers.Control)) s += "Ctrl + ";
        if (Modifiers.HasFlag(KeyModifiers.Shift)) s += "Shift + ";
        if (Modifiers.HasFlag(KeyModifiers.Alt)) s += "Alt + ";
        if (Modifiers.HasFlag(KeyModifiers.Super)) s += "Win + ";
        return s + WpfKeyNames.ToName(Key);
    }
}
