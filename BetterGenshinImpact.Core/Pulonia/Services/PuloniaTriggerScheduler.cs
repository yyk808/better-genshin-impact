using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 计划触发器的快捷键注册（由宿主实现：Windows 为应用统一热键模型，其它平台可不提供）。
/// </summary>
public interface IPuloniaTriggerHotkeys
{
    /// <summary>热键冲突或注册错误；为空表示全部注册成功。非空时调度循环按计划轮询周期重试注册</summary>
    string Error { get; }

    /// <summary>按计划中的启用热键触发器重建注册；配置未变且没有错误时可以直接返回</summary>
    Task RefreshAsync(IReadOnlyList<PuloniaTaskPlan> plans, CancellationToken ct);
}

/// <summary>
/// 本实例的计划调度循环（主实例与子实例都运行，各自只处理本实例负责的定时）：每秒检查一次定时触发器，计划变化（本进程保存或文件修改标记变化）时重新读档并刷新热键注册。
/// Windows 的 <c>PuloniaTaskTriggerHost</c> 与 Linux 宿主共用
/// </summary>
public sealed class PuloniaTriggerScheduler(PuloniaTaskStore store, IPuloniaTaskService tasks, TimeProvider clock,
    ILogger logger, IPuloniaTriggerHotkeys? hotkeys = null, bool registersHotkeys = true)
{
    /// <summary>检查计划文件修改标记、重试失败热键注册的周期。</summary>
    private static readonly TimeSpan PlanPollInterval = TimeSpan.FromSeconds(30);

    /// <summary>可供界面读取的最后一次触发器反馈，不暴露服务内部游标。</summary>
    public IReadOnlyList<PuloniaTaskTriggerStatus> Statuses { get; private set; } = [];

    /// <summary>调度与平台入口的可见状态。</summary>
    public string Status { get; set; } = "调度器尚未启动。";

    /// <summary>平台状态或游标变化通知；订阅者负责切换 UI 线程。</summary>
    public event EventHandler? Changed;

    /// <summary>通知状态变化（宿主修改 <see cref="Status"/> 后调用）</summary>
    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private string HotkeyError => hotkeys?.Error ?? "";

    private Task RefreshHotkeysAsync(IReadOnlyList<PuloniaTaskPlan> plans, CancellationToken ct)
        => hotkeys?.RefreshAsync(plans, ct) ?? Task.CompletedTask;

    /// <summary>运行调度循环直到取消；读取失败时停本次自动动作而不是继续使用过期配置。</summary>
    public async Task RunAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        IReadOnlyList<PuloniaTaskPlan> plans = [];
        // 本次读档中损坏、被跳过的计划：调度检查保留它们的游标，状态栏提示用户修复。
        IReadOnlyList<string> unreadablePlanIds = [];
        var planLoadError = "";
        var version = -1L;
        var planStamp = "";
        var lastPoll = DateTimeOffset.MinValue;
        try
        {
            do
            {
                try
                {
                    var now = clock.GetUtcNow();
                    var reload = version != store.PlanChangeVersion;
                    if (!reload && now - lastPoll >= PlanPollInterval)
                    {
                        // 子实例保存的计划不改变本进程的版本号：定期比较计划文件修改标记，变化才重读。
                        lastPoll = now;
                        // 上次有计划文件读取失败（可能只是被其他程序暂时独占）时，按这一周期重试，不必等到文件再被修改。
                        reload = store.GetPlanFilesStamp() != planStamp || planLoadError.Length > 0;
                        // 注册冲突可能已由用户在快捷键页解除，热键注册失败时随这一周期重试。
                        if (!reload && HotkeyError.Length > 0 && registersHotkeys)
                            await RefreshHotkeysAsync(plans, token).ConfigureAwait(false);
                    }
                    if (reload)
                    {
                        // 先捕获版本和修改标记，读档期间的新保存由下一轮再次加载，不能被误标已读取。
                        version = store.PlanChangeVersion;
                        planStamp = store.GetPlanFilesStamp();
                        // 一份坏计划只跳过它自己，其余计划照常调度；修复后最迟在下一个检查周期重新读取。
                        var listed = await store.ListPlansWithFailuresAsync(token).ConfigureAwait(false);
                        plans = listed.Items;
                        unreadablePlanIds = listed.Failures.Select(item => item.Id).OfType<string>().ToArray();
                        var loadError = listed.Failures.Count == 0
                            ? ""
                            : $" 有 {listed.Failures.Count} 份计划文件无法读取，已跳过其触发器：" + string.Join("；", listed.Failures);
                        // 周期重试时只在失败内容变化后记日志，避免同一坏文件反复刷屏。
                        if (loadError != planLoadError)
                            foreach (var failure in listed.Failures)
                                logger.LogWarning("Pulonia 调度跳过无法读取的计划文件 {Path}：{Message}", failure.Path, failure.Message);
                        planLoadError = loadError;
                        lastPoll = now;
                        if (registersHotkeys)
                            await RefreshHotkeysAsync(plans, token).ConfigureAwait(false);
                    }
                    // 任务服务只处理本实例负责的定时触发器
                    await tasks.CheckTriggersAsync(plans, unreadablePlanIds, token).ConfigureAwait(false);
                    Statuses = tasks.GetTriggerStatuses();
                    Status = (registersHotkeys
                                 ? "本实例调度运行中：检查本实例负责的定时，并注册计划热键（仅在程序运行时生效）。"
                                 : "本实例调度运行中：检查执行实例包含本实例的定时；计划热键只在主实例生效。")
                             + planLoadError + HotkeyError;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Status = "调度检查失败，未继续自动触发：" + ex.Message;
                    logger.LogError(ex, "Pulonia 调度检查失败");
                    // 下一轮重新读档，不能用读档失败之前的缓存触发。
                    version = -1;
                }
                Changed?.Invoke(this, EventArgs.Empty);
            } while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // 宿主停止或界面关闭
        }
    }
}
