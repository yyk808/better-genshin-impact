using BetterGenshinImpact.GameTask;

namespace BetterGenshinImpact.Host.Common;

/// <summary>
/// Linux 宿主不屏蔽桌面通知（Windows 为 WindowsNotificationSuppressionService 的专注助手开关）
/// </summary>
public sealed class NoopSoloTaskNotificationSuppressor : ISoloTaskNotificationSuppressor
{
    public static readonly NoopSoloTaskNotificationSuppressor Instance = new();

    public IDisposable? TrySuppressForSoloTask() => null;
}
