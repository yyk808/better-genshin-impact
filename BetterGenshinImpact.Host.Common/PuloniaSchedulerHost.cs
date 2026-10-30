using BetterGenshinImpact.Pulonia.Services;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Host.Common;

/// <summary>
/// Linux 宿主的任务计划调度：在后台运行 Core 的 <see cref="PuloniaTriggerScheduler"/>（定时触发器），
/// 与 Windows 的 PuloniaTaskTriggerHost 相同的循环。计划快捷键触发尚未接入（不注册热键）
/// </summary>
public sealed class PuloniaSchedulerHost(PuloniaTaskStore store, IPuloniaTaskService tasks, TimeProvider clock,
    ILogger<PuloniaSchedulerHost> logger) : IAsyncDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public PuloniaTriggerScheduler Scheduler { get; } = new(store, tasks, clock, logger);

    /// <summary>启动调度循环（重复调用无效）</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_loop is not null) return;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _loop = Task.Run(() => Scheduler.RunAsync(token), token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? loop;
        lock (_gate)
        {
            _cts?.Cancel();
            loop = _loop;
        }

        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts?.Dispose();
    }
}
