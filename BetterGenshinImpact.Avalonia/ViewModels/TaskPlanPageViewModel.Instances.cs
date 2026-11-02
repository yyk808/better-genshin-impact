using System.Collections.ObjectModel;
using Avalonia.Threading;
using BetterGenshinImpact.Pulonia.Remote;
using BetterGenshinImpact.Service.Instance;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>执行实例选项：本实例（Endpoint 为 null）或在线的子实例</summary>
public sealed record TaskPlanRunTarget(string Label, InstanceEndpoint? Endpoint);

/// <summary>
/// 任务计划页：执行实例（与 WPF 的“运行到”相同）。根实例有在线子实例（另一 Unix 会话里的 BetterGI，通常配独立显示和 Wine 前缀）时显示，
/// 选子实例时把已保存的计划整次下发（Pulonia 一步下发），由子实例按自己的队列执行并回报状态。
/// </summary>
public sealed partial class TaskPlanPageViewModel
{
    private InstanceService? _instances;
    private PuloniaRunDispatcher? _dispatcher;

    /// <summary>执行实例选项</summary>
    public ObservableCollection<TaskPlanRunTarget> RunTargets { get; } = [new("本实例", null)];

    [ObservableProperty] private TaskPlanRunTarget? _selectedRunTarget;

    /// <summary>有在线子实例时显示执行实例选择</summary>
    public bool ShowRunTargets => RunTargets.Count > 1;

    /// <summary>构造时调用：订阅子实例上下线</summary>
    private void AttachInstances(IServiceProvider services)
    {
        _instances = services.GetService<InstanceService>();
        _dispatcher = services.GetService<PuloniaRunDispatcher>();
        _selectedRunTarget = RunTargets[0];
        if (_instances is null || !_instances.Context.IsRoot) return;
        _instances.ConnectedInstancesChanged += (_, _) => Dispatcher.UIThread.Post(RefreshRunTargets);
        if (_dispatcher is not null) _dispatcher.Changed += (_, _) => Dispatcher.UIThread.Post(() => _ = RefreshRunsAsync());
        RefreshRunTargets();
    }

    /// <summary>按在线子实例重建选项，尽量保持原选择</summary>
    public void RefreshRunTargets()
    {
        if (_instances is null) return;
        var previous = SelectedRunTarget?.Endpoint?.ProcessId;
        var children = _instances.GetConnectedChildInstances()
            .Where(e => e.InstanceType == BetterGiInstanceType.ChildSession)
            .Select(e => new TaskPlanRunTarget($"{PuloniaRunDispatcher.Describe(e)}（进程 {e.ProcessId}）", e))
            .ToList();
        while (RunTargets.Count > 1) RunTargets.RemoveAt(1);
        foreach (var c in children) RunTargets.Add(c);
        SelectedRunTarget = RunTargets.FirstOrDefault(t => t.Endpoint?.ProcessId == previous) ?? RunTargets[0];
        OnPropertyChanged(nameof(ShowRunTargets));
    }

    /// <summary>下发到子实例：子实例读取共享计划、核对修订号并入队后返回运行 ID</summary>
    private async Task<string?> SubmitToChildAsync(TaskPlanItem item, InstanceEndpoint target)
    {
        if (_dispatcher is null) throw new InvalidOperationException("当前实例不支持把计划下发到子实例。");
        var runId = await _dispatcher.SubmitAsync(target, item.Plan.Id, item.Plan.Revision);
        StatusMessage = $"已把“{item.Name}”下发到{PuloniaRunDispatcher.Describe(target)}，由它按自己的队列执行。";
        _log.Information("任务计划：已下发 {Plan} 到 {Target}（{RunId}）", item.Name, PuloniaRunDispatcher.Describe(target), runId);
        return runId;
    }

    /// <summary>本实例下发出去、仍在跟踪的运行</summary>
    public IReadOnlyList<PuloniaDispatchedRun> DispatchedRuns => _dispatcher?.ListRuns() ?? [];

    /// <summary>当前计划最近一次下发到子实例的运行</summary>
    public PuloniaDispatchedRun? LatestDispatchedRun => SelectedPlan is { } p
        ? DispatchedRuns.Where(r => r.PlanId == p.Plan.Id).OrderByDescending(r => r.SubmittedAt).FirstOrDefault()
        : null;
}
