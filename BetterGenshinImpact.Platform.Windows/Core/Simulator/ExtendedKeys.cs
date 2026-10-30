using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Simulator;

/// <summary>
/// 按真实键盘的行为判断按键是否为扩展键（SendInput 的 KEYEVENTF_EXTENDEDKEY、按键消息 lParam 的第 24 位）。
/// <para>
/// 不使用 Fischless 的 InputBuilder.IsExtendedKey：它把左 Alt 以及泛指的 Alt / Ctrl 也算作扩展键，
/// 发出去会被识别成右 Alt / 右 Ctrl。
/// </para>
/// 参考：https://learn.microsoft.com/windows/win32/inputdev/about-keyboard-input#extended-key-flag
/// </summary>
public static class ExtendedKeys
{
    public static bool IsExtended(GameKey key) => key switch
    {
        // 右侧 Ctrl / Alt；左侧和泛指的 Ctrl / Alt 不是扩展键
        GameKey.VK_RCONTROL or GameKey.VK_RMENU => true,

        // 小键盘左侧的编辑区和方向键
        GameKey.VK_INSERT or GameKey.VK_DELETE
            or GameKey.VK_HOME or GameKey.VK_END
            or GameKey.VK_PRIOR or GameKey.VK_NEXT
            or GameKey.VK_LEFT or GameKey.VK_UP
            or GameKey.VK_RIGHT or GameKey.VK_DOWN => true,

        // 小键盘 / 与 NumLock，Break（Ctrl+Pause）与 PrintScreen
        GameKey.VK_DIVIDE or GameKey.VK_NUMLOCK
            or GameKey.VK_CANCEL or GameKey.VK_SNAPSHOT => true,

        // 左右 Win 与菜单键
        GameKey.VK_LWIN or GameKey.VK_RWIN or GameKey.VK_APPS => true,

        _ => false,
    };
}
