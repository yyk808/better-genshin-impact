using System.Collections.Generic;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Platform.Windows;

/// <summary>GetAsyncKeyState / GetCursorPos 实现的系统键状态</summary>
public sealed class Win32KeyStateProvider : IKeyStateProvider
{
    public static Win32KeyStateProvider Instance { get; } = new();

    /// <summary>与原 <c>(User32.GetAsyncKeyState(vk) &amp; 0x8000) != 0</c> 完全相同</summary>
    public bool IsKeyDown(GameKey key) => (User32.GetAsyncKeyState((int)key) & 0x8000) != 0;

    public IReadOnlySet<GameKey> GetPressedKeys()
    {
        var set = new HashSet<GameKey>();
        for (var vk = 1; vk < 0xFF; vk++)
        {
            if ((User32.GetAsyncKeyState(vk) & 0x8000) != 0)
            {
                set.Add((GameKey)vk);
            }
        }

        return set;
    }

    public PixelPoint? GetCursorPosition() =>
        User32.GetCursorPos(out var p) ? new PixelPoint(p.X, p.Y) : null;
}
