using System;
using System.Windows.Forms;

namespace BetterGenshinImpact.Model;

/// <summary>Windows 键鼠钩子的按住状态查询，供 <see cref="BetterGenshinImpact.GameTask.HotkeyHoldState"/> 使用</summary>
public static class HoldHotkeyQuery
{
    /// <summary>按配置文本判断对应的按住型键鼠钩子是否按下（原 QuickTeleportTrigger.IsHotkeyPressed）</summary>
    public static bool IsHotkeyPressed(string hotkey)
    {
        if (HotKey.IsMouseButton(hotkey))
        {
            if (MouseHook.AllMouseHooks.TryGetValue((MouseButtons)Enum.Parse(typeof(MouseButtons), hotkey), out var mouseHook))
            {
                if (mouseHook.IsPressed)
                {
                    return true;
                }
            }
        }
        else
        {
            if (KeyboardHook.AllKeyboardHooks.TryGetValue((Keys)Enum.Parse(typeof(Keys), hotkey), out var keyboardHook))
            {
                if (keyboardHook.IsPressed)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
