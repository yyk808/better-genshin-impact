namespace BetterGenshinImpact.Service.Instance.MessageHandlers;

/// <summary>
/// Linux：没有桌面分身的相对鼠标转发（Windows 上根实例把原始鼠标移动转发给子会话里的游戏）。
/// 每个 Linux 实例有自己的显示与输入：子实例连接后照常订阅（协议与 Windows 相同），根实例应答“未订阅”且从不转发，收到的批次不处理。
/// 接口与 WPF 版相同，供共用的实例服务调用。
/// </summary>
internal sealed class RelativeMouseMessageHandler
{
    internal bool HandleBatch(InstanceConnection connection, ulong firstSequence, IReadOnlyList<RelativeMouseSample> samples) => false;

    internal void HandleResult(InstanceConnection connection, RelativeMouseResult result) { }

    internal InstanceIpcEnvelope HandleSubscribe(InstanceConnection connection, InstanceIpcEnvelope request)
        => InstanceIpcEnvelope.Response(request, new RelativeMouseState { IsSubscribed = false });

    internal InstanceIpcEnvelope HandleUnsubscribe(InstanceConnection connection, InstanceIpcEnvelope request)
        => InstanceIpcEnvelope.Response(request, new RelativeMouseState { IsSubscribed = false });

    internal void RemoveTarget(int windowsSessionId) { }

    internal bool IsGameMouseModeEnabled => false;

    internal void SetGameMouseModeEnabled(bool enabled)
    {
        if (enabled) throw new InvalidOperationException("Linux 版没有桌面分身鼠标模式。");
    }

    internal void Stop() { }
}
