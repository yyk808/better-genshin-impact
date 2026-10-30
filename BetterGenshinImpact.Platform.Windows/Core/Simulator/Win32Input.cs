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
    /// 抬起系统中所有处于按下状态的键，再抬起鼠标左/中/右键（与原 Simulation.ReleaseAllKey 相同）
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

        SendInput.Mouse.LeftButtonUp();
        SendInput.Mouse.RightButtonUp();
        SendInput.Mouse.MiddleButtonUp();
    }
}
