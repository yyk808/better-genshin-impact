using Fischless.WindowsInput;
using Microsoft.Extensions.Logging;
using System;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Simulator;

/// <summary>
/// 进程内共享的 Win32 前台输入（SendInput）与系统键状态查询。
/// 原先在主程序的 Simulation 里；Simulation 现在转发到这里，两者使用同一个 <see cref="InputSimulator"/> 实例
/// </summary>
public static class Win32Input
{
    public static InputSimulator SendInput { get; } = new();

    /// <summary>GetAsyncKeyState 最高位为 1 表示按下</summary>
    public static bool IsKeyDown(GameKey key) => (User32.GetAsyncKeyState((int)key) & 0x8000) != 0;

    /// <summary>
    /// 抬起系统中所有处于按下状态的键，再按需抬起鼠标左/中/右键（仅在按下时；与上游 #3894 一致）
    /// </summary>
    public static void ReleaseAllKeys(ILogger? logger)
    {
        foreach (GameKey key in Enum.GetValues(typeof(GameKey)))
        {
            if (key == GameKey.None)
            {
                continue;
            }

            if (IsKeyDown(key))
            {
                logger?.LogDebug($"解除{key}的按下状态.");
                SendInput.Keyboard.KeyUp((User32.VK)key);
            }
        }

        // 鼠标键同样只在确实处于按下状态时才抬起（上游 a9e08fc0 / #3894）。
        // 无条件发送抬起会产生一条没有对应按下的 WM_*BUTTONUP，DefWindowProc 会把它转成 WM_CONTEXTMENU，
        // 在游戏/程序未处于前台时表现为在光标处“右键一下”弹出菜单。
        ReleaseMouseButtonIfPressed(GameKey.VK_LBUTTON, () => SendInput.Mouse.LeftButtonUp(), logger);
        ReleaseMouseButtonIfPressed(GameKey.VK_RBUTTON, () => SendInput.Mouse.RightButtonUp(), logger);
        ReleaseMouseButtonIfPressed(GameKey.VK_MBUTTON, () => SendInput.Mouse.MiddleButtonUp(), logger);
    }

    /// <summary>
    /// 鼠标键处于按下状态时才发送抬起，避免产生脱离按下事件的孤立抬起消息。
    /// </summary>
    private static void ReleaseMouseButtonIfPressed(GameKey mouseButton, Action release, ILogger? logger)
    {
        if (!IsKeyDown(mouseButton))
        {
            return;
        }

        logger?.LogDebug($"解除{mouseButton}的按下状态.");
        release();
    }
}
