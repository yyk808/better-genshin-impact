using BetterGenshinImpact.Platform;

namespace BetterGenshinImpact.Core.Input;

/// <summary>
/// 不产生任何输入的通道：每种操作限频告警一次后忽略。
/// 用于平台没有某一路通道的情况（例如 Linux 原生没有后台通道）
/// </summary>
public sealed class UnsupportedInputChannel(string reason) : InputChannelBase
{
    public string Reason { get; } = reason;

    public override InputCapabilities Capabilities { get; } = new(RequiresForeground: false, SupportsRelativeMotion: false);

    protected override void OnKeyDown(GameKey key) => WarnUnsupported($"{Reason}：按下 {key}");

    protected override void OnKeyUp(GameKey key) => WarnUnsupported($"{Reason}：抬起 {key}");

    protected override void OnMouseButton(InputMouseButton button, bool down) => WarnUnsupported($"{Reason}：鼠标 {button}");

    protected override void OnMoveBy(int dx, int dy) => WarnUnsupported($"{Reason}：相对移动");

    protected override void OnMoveTo(double absX, double absY) => WarnUnsupported($"{Reason}：绝对移动");

    protected override void OnMoveToPixel(int screenX, int screenY) => WarnUnsupported($"{Reason}：绝对移动");

    protected override void OnScroll(int clicks) => WarnUnsupported($"{Reason}：滚轮");

    /// <summary>没有真实输入，按下记录也就不应保留</summary>
    public override bool IsKeyDown(GameKey key) => false;
}
