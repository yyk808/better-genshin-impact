using BetterGenshinImpact.GameTask;

namespace BetterGenshinImpact.Host.Common;

/// <summary>
/// 不屏蔽桌面通知的占位实现。Linux 宿主默认改用
/// <see cref="BetterGenshinImpact.Platform.Linux.Desktop.LinuxNotificationSuppressionService"/>。
/// </summary>
public sealed class NoopSoloTaskNotificationSuppressor : ISoloTaskNotificationSuppressor
{
    public static readonly NoopSoloTaskNotificationSuppressor Instance = new();

    public IDisposable? TrySuppressForSoloTask() => null;
}
