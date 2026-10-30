using System;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 独立任务运行期间屏蔽系统通知。Windows 实现为 WindowsNotificationSuppressionService
/// </summary>
public interface ISoloTaskNotificationSuppressor
{
    /// <summary>尝试为独立任务屏蔽通知，返回恢复用的范围；未启用或失败时返回 null</summary>
    IDisposable? TrySuppressForSoloTask();
}
