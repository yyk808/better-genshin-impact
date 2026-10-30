using System;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 按住型热键（如快速传送的“按住时生效”）当前是否按下。由宿主的键鼠钩子实现，未设置时视为未按下
/// </summary>
public static class HotkeyHoldState
{
    /// <summary>参数为热键配置文本（键名或鼠标侧键名）</summary>
    public static Func<string, bool>? Query { get; set; }

    public static bool IsPressed(string hotkey) => Query?.Invoke(hotkey) ?? false;
}
