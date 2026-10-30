using System;

namespace BetterGenshinImpact.Platform;

/// <summary>热键修饰键</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,

    /// <summary>Win / Super / Meta</summary>
    Super = 8,
}

/// <summary>一个全局热键组合</summary>
public readonly record struct HotkeyGesture(GameKey Key, KeyModifiers Modifiers = KeyModifiers.None)
{
    public override string ToString()
    {
        var mods = "";
        if (Modifiers.HasFlag(KeyModifiers.Super)) mods += "Win + ";
        if (Modifiers.HasFlag(KeyModifiers.Control)) mods += "Ctrl + ";
        if (Modifiers.HasFlag(KeyModifiers.Shift)) mods += "Shift + ";
        if (Modifiers.HasFlag(KeyModifiers.Alt)) mods += "Alt + ";
        return mods + Key;
    }

    /// <summary>
    /// 解析 "Ctrl + Shift + VK_X"、"ctrl+shift+x"、"F9" 等写法。键名可以是 GameKey 成员名（VK_*），
    /// 也可以省略 VK_ 前缀
    /// </summary>
    public static HotkeyGesture Parse(string s)
    {
        var mods = KeyModifiers.None;
        var key = GameKey.None;
        foreach (var raw in s.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= KeyModifiers.Control; break;
                case "shift": mods |= KeyModifiers.Shift; break;
                case "alt": mods |= KeyModifiers.Alt; break;
                case "win": case "super": case "meta": mods |= KeyModifiers.Super; break;
                default: key = GameKeys.Parse(raw); break;
            }
        }

        return new HotkeyGesture(key, mods);
    }
}

/// <summary>
/// 全局热键服务：Windows RegisterHotKey / X11 XGrabKey / 以后 Wayland GlobalShortcuts portal。
/// <para>
/// 语义：注册期间该组合键会被“吞掉”，前台程序（包括游戏）收不到；回调在实现内部的线程上触发，调用方自行切回 UI 线程。
/// </para>
/// </summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>注册热键；被占用等失败时抛异常（Windows 为 HotkeyHook 的原异常，X11 为 <see cref="InvalidOperationException"/>）。返回对象 Dispose 即注销</summary>
    /// <param name="hotkey">组合键</param>
    /// <param name="onPressed">按下回调</param>
    /// <param name="noRepeat">按住不放时不重复触发（Windows MOD_NOREPEAT；X11 按 KeyRelease 判断）</param>
    IDisposable Register(HotkeyGesture hotkey, Action<HotkeyGesture> onPressed, bool noRepeat = false);
}
