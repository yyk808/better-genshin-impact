using BetterGenshinImpact.GameTask.Runtime;

namespace BetterGenshinImpact.Service;

/// <summary>触发器调度器上报遮罩性能指标的接口（WPF 主程序中为 OverlayMetricsService）</summary>
public interface IOverlayMetricsSink
{
    bool IsEnabled { get; }

    void RecordSkippedTick();

    void RecordDispatcherTick(double processingCostMs, double captureCostMs, double triggerCostMs);

    /// <summary>设置要采样的游戏窗口（null 为解绑）</summary>
    void SetGameWindow(IGameWindow? window);
}

/// <summary>不采集指标时使用的空实现</summary>
public sealed class NullOverlayMetricsSink : IOverlayMetricsSink
{
    public static readonly NullOverlayMetricsSink Instance = new();

    public bool IsEnabled => false;

    public void RecordSkippedTick()
    {
    }

    public void RecordDispatcherTick(double processingCostMs, double captureCostMs, double triggerCostMs)
    {
    }

    public void SetGameWindow(IGameWindow? window)
    {
    }
}
