namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>运行状态的中文显示文本（执行记录列表使用，Windows 与 Linux 界面共用）</summary>
public static class PuloniaTaskRunStatusText
{
    public static string Get(PuloniaTaskRunStatus status) => status switch
    {
        PuloniaTaskRunStatus.Queued => "排队中",
        PuloniaTaskRunStatus.Running => "运行中",
        PuloniaTaskRunStatus.Cancelling => "停止中",
        PuloniaTaskRunStatus.Succeeded => "已完成",
        PuloniaTaskRunStatus.Failed => "已失败",
        PuloniaTaskRunStatus.Cancelled => "已停止",
        PuloniaTaskRunStatus.TimedOut => "已超时",
        PuloniaTaskRunStatus.Interrupted => "已中断",
        PuloniaTaskRunStatus.NeedsAttention => "待处理",
        PuloniaTaskRunStatus.Expired => "已过期（未执行）",
        _ => status.ToString()
    };

    /// <summary>排队、运行或停止中（尚未结束）</summary>
    public static bool IsActive(PuloniaTaskRunStatus status)
        => status is PuloniaTaskRunStatus.Queued or PuloniaTaskRunStatus.Running or PuloniaTaskRunStatus.Cancelling;
}
