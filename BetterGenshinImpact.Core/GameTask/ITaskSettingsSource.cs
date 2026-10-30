namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 任务设置页上的、脚本调用独立任务时需要的设置与策略选择。Windows 由 TaskSettingsPageViewModel 实现
/// </summary>
public interface ITaskSettingsSource
{
    /// <summary>伐木轮数</summary>
    int AutoWoodRoundNum { get; }

    /// <summary>伐木每日上限</summary>
    int AutoWoodDailyMaxCount { get; }

    /// <summary>钓鱼时是否在关键帧保存截图</summary>
    bool SaveScreenshotOnKeyTick { get; }

    /// <summary>读取七圣召唤策略内容，返回 true 表示失败（与原方法相同）</summary>
    bool GetTcgStrategy(out string content);

    /// <summary>按自动战斗配置的策略名取策略路径，返回 true 表示失败</summary>
    bool GetFightStrategy(out string path);

    /// <summary>按策略名取策略路径，返回 true 表示失败</summary>
    bool GetFightStrategy(string strategyName, out string path);
}
