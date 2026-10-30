namespace BetterGenshinImpact.Core.Input;

/// <summary>
/// 不产生任何输入的后端：前台通道忽略操作并限频告警，没有后台通道。
/// 用于宿主尚未设置输入后端时（无界面 / 测试）
/// </summary>
public sealed class UnsupportedInputBackend(string reason) : IInputBackend
{
    public InputBackendKind Kind => InputBackendKind.None;

    public IInputChannel Foreground { get; } = new UnsupportedInputChannel(reason);

    public IInputChannel? Background => null;

    public void ReleaseAll() => Foreground.ReleaseAll();

    public void Dispose()
    {
    }
}
