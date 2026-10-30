using Fischless.WindowsInput;
using System;
using BetterGenshinImpact.GameTask.Common;

namespace BetterGenshinImpact.Core.Simulator;

public class Simulation
{
    /// <summary>
    /// 与 Win32 前台输入通道共享同一个实例（见 Platform.Windows 中的 <see cref="Win32Input"/>）
    /// </summary>
    public static InputSimulator SendInput => Win32Input.SendInput;

    public static MouseEventSimulator MouseEvent { get; } = new();

    public static PostMessageSimulator PostMessage(IntPtr hWnd)
    {
        return new PostMessageSimulator(hWnd);
    }

    public static void ReleaseAllKey() => Win32Input.ReleaseAllKeys(TaskControl.Logger);

    public static bool IsKeyDown(GameKey key) => Win32Input.IsKeyDown(key);
}
