using System;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Script;

/// <summary>
/// 任务开始前等待脚本仓库自动更新结束的宿主入口。
/// Windows 主程序启动时接到 ScriptRepoUpdater.Instance.WaitForAutoUpdateAsync（首次调用时才创建单例，与迁移前相同）；
/// 未设置时视为没有正在进行的自动更新。
/// </summary>
public static class ScriptAutoUpdateGate
{
    public static Func<CancellationToken, Task> WaitForAutoUpdate { get; set; } = static _ => Task.CompletedTask;

    public static Task WaitForAutoUpdateAsync(CancellationToken ct) => WaitForAutoUpdate(ct);
}
