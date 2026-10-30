using System;

namespace BetterGenshinImpact.Platform;

/// <summary>GameKey 辅助方法</summary>
public static class GameKeys
{
    /// <summary>是否为鼠标键（VK_LBUTTON、VK_RBUTTON、VK_MBUTTON、VK_XBUTTON1、VK_XBUTTON2）</summary>
    public static bool IsMouseButton(this GameKey key) =>
        key is GameKey.VK_LBUTTON or GameKey.VK_RBUTTON or GameKey.VK_MBUTTON or GameKey.VK_XBUTTON1 or GameKey.VK_XBUTTON2;

    /// <summary>解析键名：GameKey 成员名（VK_E），或省略 VK_ 前缀（E、F9、esc），单个字母/数字</summary>
    public static GameKey Parse(string s)
    {
        if (TryParse(s, out var key))
        {
            return key;
        }

        throw new ArgumentException($"无法识别的键名：{s}", nameof(s));
    }

    public static bool TryParse(string s, out GameKey key)
    {
        s = s.Trim();
        if (Enum.TryParse(s, true, out key) && Enum.IsDefined(key))
        {
            return true;
        }

        if (Enum.TryParse("VK_" + s, true, out key) && Enum.IsDefined(key))
        {
            return true;
        }

        switch (s.ToLowerInvariant())
        {
            case "esc": key = GameKey.VK_ESCAPE; return true;
            case "enter": key = GameKey.VK_RETURN; return true;
            case "backspace": key = GameKey.VK_BACK; return true;
            case "ctrl": key = GameKey.VK_CONTROL; return true;
            case "alt": key = GameKey.VK_MENU; return true;
            case "lalt": key = GameKey.VK_LMENU; return true;
            case "ralt": key = GameKey.VK_RMENU; return true;
            case "lctrl": key = GameKey.VK_LCONTROL; return true;
            case "rctrl": key = GameKey.VK_RCONTROL; return true;
            case "pageup": key = GameKey.VK_PRIOR; return true;
            case "pagedown": key = GameKey.VK_NEXT; return true;
        }

        key = GameKey.None;
        return false;
    }
}
