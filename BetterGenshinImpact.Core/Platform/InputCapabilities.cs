namespace BetterGenshinImpact.Platform;

/// <summary>输入通道的能力描述</summary>
/// <param name="RequiresForeground">是否只对前台窗口生效（SendInput / XTest 为 true，PostMessage / 网页版为 false）</param>
/// <param name="SupportsRelativeMotion">是否支持相对移动（视角控制）</param>
/// <param name="SupportsKeyStateQuery">IsKeyDown 是否查询真实（系统）键状态；为 false 时只反映本通道自己按下的键</param>
/// <param name="AbsoluteMoveAffectsCamera">
/// 绝对移动是否会被游戏当成视角转动。Wine 会把 XTest/SendInput 的绝对移动转成不带 MOUSE_MOVE_ABSOLUTE 的 WM_INPUT 相对增量，
/// 原神会据此转动视角；所以为 true 时，视角锁定的场景里不能用绝对移动
/// </param>
public sealed record InputCapabilities(
    bool RequiresForeground = true,
    bool SupportsRelativeMotion = true,
    bool SupportsKeyStateQuery = false,
    bool AbsoluteMoveAffectsCamera = false)
{
    public static InputCapabilities Default { get; } = new();
}
