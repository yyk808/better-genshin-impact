using Avalonia.Controls;
using Avalonia.Media;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// 任务资源更新（对应 WPF PuloniaPlanEditingSession.Resources）：检查任务资源更新、更新此任务资源、
/// 使用本机仓库更新所有计划、一键同步并更新。规则在 Core <see cref="PuloniaResourceUpdateRules"/>，
/// 资源读取与固定在 Core <see cref="PuloniaTaskResourceVersionService"/>；磁盘变化不会自动改写任务使用版本。
/// </summary>
public sealed partial class TaskPlanPageViewModel
{
    /// <summary>一次检查得到的节点状态</summary>
    private sealed record ResourceCheck(string Key, PuloniaTaskResourceVersionState? State, string? Error);

    private PuloniaTaskResourceVersionService? _resourceVersions;

    /// <summary>串行化检查与更新（资源版本服务的缓存不是线程安全的）</summary>
    private readonly SemaphoreSlim _resourceGate = new(1, 1);

    /// <summary>(计划 ID, 任务 ID) → 最近一次检查结果</summary>
    private readonly Dictionary<(string Plan, string Task), ResourceCheck> _resourceChecks = new();

    private PuloniaTaskResourceVersionService? ResourceVersions
        => _resourceVersions ??= _catalog is null ? null : new PuloniaTaskResourceVersionService(_catalog);

    /// <summary>选中行可以“更新此任务资源”</summary>
    public bool CanUpdateSelectedResource => SelectedNode is { } row && PuloniaResourceUpdateRules.CanUpdate(row.Task);

    /// <summary>行所在的计划 ID（本页选中计划）</summary>
    private string? CurrentPlanId => SelectedPlan?.Plan.Id;

    /// <summary>本页与其他用途的全部计划（本页计划用界面中的实例，便于撤销与刷新）</summary>
    private async Task<List<(PuloniaTaskPlan Plan, TaskPlanItem? Item)>> ResourcePlansAsync()
    {
        var result = Plans.Select(p => (p.Plan, (TaskPlanItem?)p)).ToList();
        if (_store is null) return result;
        var listed = await _store.ListPlansWithFailuresAsync();
        foreach (var plan in listed.Items)
            if (result.All(r => r.Plan.Id != plan.Id)) result.Add((plan, null));
        return result;
    }

    private static IEnumerable<PuloniaTask> Descendants(PuloniaTask task)
    {
        foreach (var child in task.Children)
        {
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    /// <summary>读取一批计划中全部可更新节点的当前状态（每个仓库只打开一次快照）</summary>
    private async Task CheckPlansAsync(IReadOnlyList<PuloniaTaskPlan> plans, CancellationToken ct = default)
    {
        if (ResourceVersions is not { } service || _tasks is null) return;
        var nodes = plans.SelectMany(p => Descendants(p.RootTask).Where(PuloniaResourceUpdateRules.CanUpdate).Select(t => (Plan: p, Task: t))).ToArray();
        // 服务读取节点副本，后台 IO 期间界面编辑不会改变本轮读取的任务
        var snapshots = nodes.Select(n => PuloniaResourceUpdateRules.Snapshot(n.Task)).ToArray();
        var results = await service.ReadCurrentStatesAsync(snapshots, _tasks.Definitions, ct);
        foreach (var plan in plans)
            foreach (var key in _resourceChecks.Keys.Where(k => k.Plan == plan.Id).ToArray()) _resourceChecks.Remove(key);
        for (var i = 0; i < nodes.Length; i++)
        {
            var r = results[i];
            _resourceChecks[(nodes[i].Plan.Id, nodes[i].Task.Id)] = new ResourceCheck(PuloniaResourceUpdateRules.CheckKey(snapshots[i]), r.State,
                r.State is null ? PuloniaResourceUpdateRules.CheckErrorText(snapshots[i], r.Error!) : null);
        }
    }

    /// <summary>节点最近一次检查结果；路径、来源或使用版本已变化（键不同）时视为未检查</summary>
    private ResourceCheck? GetCheck(string? planId, PuloniaTask task)
        => planId is not null && _resourceChecks.TryGetValue((planId, task.Id), out var check) && check.Key == PuloniaResourceUpdateRules.CheckKey(task)
            ? check : null;

    private bool HasUpdate(string planId, PuloniaTask task)
        => GetCheck(planId, task) is { State: { } s } && PuloniaResourceUpdateRules.HasUpdate(task, s.CurrentContentVersion);

    /// <summary>计划中可更新的资源数（不展开引用计划）</summary>
    private int CountUpdates(PuloniaTaskPlan plan) => Descendants(plan.RootTask).Count(t => HasUpdate(plan.Id, t));

    /// <summary>行的资源标签与提示（文字规则与 WPF 任务树相同）</summary>
    internal (string? Badge, string? Tip) DescribeResource(PuloniaTask task)
    {
        if (task.Source is { Kind: "plan", PlanId: { } planId })
        {
            var target = Plans.FirstOrDefault(p => p.Plan.Id == planId)?.Plan ?? _allPlans.GetValueOrDefault(planId);
            var count = target is null ? 0 : CountUpdates(target);
            return count > 0 ? (PuloniaResourceUpdateRules.BadgeText(task, null, count, true, null, null),
                PuloniaResourceUpdateRules.BadgeToolTip(null, count, true, null, null)) : (null, null);
        }
        if (GetCheck(CurrentPlanId, task) is not { } check) return (null, null);
        var hasUpdate = check.State is { } s && PuloniaResourceUpdateRules.HasUpdate(task, s.CurrentContentVersion);
        if (check.Error is null && !hasUpdate) return (null, null);
        return (PuloniaResourceUpdateRules.BadgeText(task, check.Error, 0, hasUpdate, check.State?.ApprovedDeclaredVersion, check.State?.CurrentDeclaredVersion),
            PuloniaResourceUpdateRules.BadgeToolTip(check.Error, 0, hasUpdate, check.State?.ApprovedDeclaredVersion, check.State?.CurrentDeclaredVersion)
                .Replace("可右键更新此任务资源，或在计划卡片上更新整个计划", "可用“更新资源”更新此任务资源，或在计划列表上方的同步菜单中更新全部计划"));
    }

    private void RefreshResourceBadges()
    {
        foreach (var row in Nodes) row.RefreshResource();
    }

    /// <summary>检查任务资源更新：只检查本机仓库与任务使用版本，不访问远端</summary>
    [RelayCommand]
    public Task CheckResourceUpdatesAsync() => CheckResourcesAsync(true);

    /// <param name="report">是否在状态栏报告结果（页面加载时的自动检查不覆盖“已加载”提示）</param>
    private async Task CheckResourcesAsync(bool report)
    {
        if (ResourceVersions is null) return;
        await _resourceGate.WaitAsync();
        try
        {
            if (report) StatusMessage = "正在检查本机仓库与任务使用版本…";
            var plans = (await ResourcePlansAsync()).Select(p => p.Plan).ToList();
            await CheckPlansAsync(plans);
            var updates = plans.Sum(CountUpdates);
            var errors = _resourceChecks.Values.Count(c => c.Error is not null);
            if (report) StatusMessage = updates == 0 && errors == 0 ? "任务资源已是本机仓库当前版本。"
                : $"{updates} 项任务资源可更新" + (errors > 0 ? $"，{errors} 项检查失败" : "") + "。";
        }
        catch (Exception e)
        {
            _log.Warning(e, "检查任务资源更新失败");
            if (report) StatusMessage = "检查任务资源更新失败：" + e.Message;
        }
        finally
        {
            _resourceGate.Release();
            RefreshResourceBadges();
        }
    }

    /// <summary>更新此任务资源：重新读取本机仓库，用户确认后更新任务使用版本（之后的运行生效，已有执行记录不变）</summary>
    [RelayCommand]
    public async Task UpdateSelectedResourceAsync()
    {
        if (SelectedPlan is not { } item || SelectedNode is not { } row || !PuloniaResourceUpdateRules.CanUpdate(row.Task)
            || ResourceVersions is not { } service || _tasks is null) return;
        var task = row.Task;
        try
        {
            string newVersion;
            global::BetterGenshinImpact.Core.Script.Repositories.ScriptResourceReference? reference;
            var key = PuloniaResourceUpdateRules.CheckKey(task);
            var snapshot = PuloniaResourceUpdateRules.Snapshot(task);
            await _resourceGate.WaitAsync();
            try
            {
                var reviewed = await service.ReadCurrentStateAsync(snapshot, _tasks.Definitions);
                newVersion = reviewed.CurrentContentVersion ?? throw new InvalidOperationException("当前节点没有可确认的资源。");
                if (PuloniaResourceUpdateRules.ApprovedVersion(task) == newVersion)
                {
                    StatusMessage = $"“{task.Name}”的资源版本没有变化。";
                    return;
                }
                LastResourceDialogText = $"资源“{task.Name}”已经变化。\n\n{PuloniaResourceUpdateRules.FormatVersionChange(reviewed)}\n\n确认后只影响之后的运行，已有执行记录不变。";
                if (!await ConfirmAsync("更新任务资源", LastResourceDialogText)) return;
                if (newVersion != await service.ReadCurrentVersionAsync(snapshot, _tasks.Definitions) || key != PuloniaResourceUpdateRules.CheckKey(task))
                    throw new InvalidOperationException("确认期间资源再次变化，请重新检查并确认。");
                reference = await service.PrepareUpdateAsync(snapshot, reviewed);
            }
            finally
            {
                _resourceGate.Release();
            }
            if (key != PuloniaResourceUpdateRules.CheckKey(task)) throw new InvalidOperationException("提取期间任务配置变化，请重新确认。");
            PuloniaResourceUpdateRules.ApplyVersion(task, newVersion, reference);
            await SaveAsync(item, $"已更新“{task.Name}”的任务使用版本。");
            await CheckResourceUpdatesAsync();
            StatusMessage = $"已更新“{task.Name}”的任务使用版本。";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException
                                      or LibGit2Sharp.LibGit2SharpException or System.Text.Json.JsonException or Newtonsoft.Json.JsonException)
        {
            StatusMessage = "更新任务资源失败：" + e.Message;
            await ShowInfoAsync("无法更新任务资源", StatusMessage);
        }
    }

    /// <summary>使用本机仓库更新所有计划（普通计划与一条龙配置），确认后逐项更新，失败项保持原版本</summary>
    [RelayCommand]
    public async Task UpdateAllResourcesAsync()
    {
        try
        {
            await UpdateResourceBatchAsync(true, true);
        }
        catch (Exception e)
        {
            StatusMessage = "更新所有任务计划失败：" + e.Message;
            await ShowInfoAsync("无法更新所有任务计划", StatusMessage);
        }
    }

    /// <summary>一键同步并更新：刷新计划实际引用的仓库，再用本机内容更新全部计划中的可用资源</summary>
    [RelayCommand]
    public async Task SyncAndUpdateResourcesAsync()
    {
        if (_catalog is null) return;
        var plans = await ResourcePlansAsync();
        var repositoryIds = PuloniaResourceUpdateRules.ReferencedRepositoryIds(plans.SelectMany(p => Descendants(p.Plan.RootTask)));
        LastResourceDialogText = $"将同步或刷新 {repositoryIds.Length} 个任务实际引用的仓库，然后更新全部 Pulonia 任务计划中的可用资源。\n"
                                 + "同步失败但本机副本仍可用时，将继续使用本机内容；失败资源保持原版本。";
        if (!await ConfirmAsync("一键同步并更新", LastResourceDialogText)) return;
        var synced = 0;
        var syncFailures = new List<string>();
        try
        {
            for (var i = 0; i < repositoryIds.Length; i++)
            {
                var id = repositoryIds[i];
                try
                {
                    var repository = await _catalog.Repositories.GetRepositoryAsync(id);
                    if (repository.IsRemote)
                        // 远端仓库的 Git 同步（ScriptRepoUpdater）仍在 Windows 版，Linux 版先使用本机副本
                        throw new InvalidOperationException("Linux 版暂不能从远端同步仓库（Git 同步尚未迁移）");
                    StatusMessage = $"正在刷新本地仓库（{i + 1}/{repositoryIds.Length}）：{repository.Name}";
                    await _catalog.Repositories.RefreshLocalRepositoryAsync(id);
                    synced++;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException
                                              or ArgumentException or LibGit2Sharp.LibGit2SharpException)
                {
                    // 同步失败不删除或替换已有副本；后续检查会自行判断本机内容是否仍可读取
                    string fallback;
                    try
                    {
                        using var local = await _catalog.Repositories.OpenCurrentAsync(id);
                        fallback = "；本机副本仍可用，已继续检查，但它可能不是远端最新版本";
                    }
                    catch (Exception le) when (le is IOException or UnauthorizedAccessException or InvalidOperationException
                                                   or ArgumentException or LibGit2Sharp.LibGit2SharpException)
                    {
                        fallback = "；没有可用的本机副本，对应资源将跳过";
                    }
                    syncFailures.Add(id + "：" + e.Message + fallback);
                }
            }

            StatusMessage = "正在检查本机仓库与任务使用版本…";
            var (updated, failures) = await UpdateResourceBatchAsync(false, false);
            var summary = $"仓库同步/刷新成功 {synced} 个、失败 {syncFailures.Count} 个；任务资源更新成功 {updated} 项、失败 {failures.Count} 项。";
            StatusMessage = summary;
            var reasons = syncFailures.Select(r => "仓库：" + r).Concat(failures.Select(r => "资源：" + r)).ToArray();
            if (reasons.Length > 0)
                await ShowInfoAsync("一键同步并更新已完成", summary + "\n\n" + string.Join("\n", reasons.Take(10).Select(r => "• " + r)) + (reasons.Length > 10 ? "\n…" : ""));
        }
        catch (Exception e)
        {
            StatusMessage = "一键同步并更新失败：" + e.Message;
            await ShowInfoAsync("无法完成一键同步并更新", StatusMessage);
        }
    }

    /// <summary>逐项固定可用资源并按计划保存；单项失败不阻止其他资源（与 WPF UpdateResourceBatchAsync 相同）</summary>
    private async Task<(int Updated, List<string> Failures)> UpdateResourceBatchAsync(bool requestConfirmation, bool displayFailures)
    {
        if (ResourceVersions is not { } service || _store is null) return (0, []);
        var plans = await ResourcePlansAsync();
        await _resourceGate.WaitAsync();
        List<(PuloniaTaskPlan Plan, TaskPlanItem? Item, PuloniaTask Task)> updates;
        List<string> failures;
        try
        {
            await CheckPlansAsync(plans.Select(p => p.Plan).ToList());
            updates = plans.SelectMany(p => Descendants(p.Plan.RootTask).Where(t => HasUpdate(p.Plan.Id, t)).Select(t => (p.Plan, p.Item, t))).ToList();
            failures = plans.SelectMany(p => Descendants(p.Plan.RootTask).Where(t => GetCheck(p.Plan.Id, t) is { Error: not null })
                .Select(t => $"{p.Plan.Name} / {t.Name}：{GetCheck(p.Plan.Id, t)!.Error}")).ToList();
        }
        finally
        {
            _resourceGate.Release();
        }
        RefreshResourceBadges();
        if (updates.Count == 0)
        {
            StatusMessage = failures.Count == 0 ? "全部 Pulonia 任务计划的任务资源已是本机仓库当前版本。"
                : $"全部 Pulonia 任务计划没有可更新资源，{failures.Count} 项检查失败。";
            return (0, failures);
        }
        if (requestConfirmation)
        {
            LastResourceDialogText = $"更新全部 Pulonia 任务计划中的 {updates.Count} 项任务资源？\n"
                + string.Join("\n", updates.Take(12).Select(u => "• " + u.Plan.Name + " / " + u.Task.Name))
                + (updates.Count > 12 ? "\n…" : "")
                + (failures.Count > 0 ? $"\n另有 {failures.Count} 项检查失败，将保持原版本。" : "")
                + "\n成功项会保存新的任务使用版本；参数、开关与已有执行记录保持不变。";
            if (!await ConfirmAsync("更新任务资源", LastResourceDialogText)) return (0, failures);
        }

        var updated = 0;
        await _resourceGate.WaitAsync();
        try
        {
            var processed = 0;
            foreach (var group in updates.GroupBy(u => u.Plan.Id))
            {
                var changed = 0;
                foreach (var (plan, _, task) in group)
                {
                    StatusMessage = $"正在更新任务资源（{++processed}/{updates.Count}）：{task.Name}";
                    var state = GetCheck(plan.Id, task)?.State;
                    if (state?.CurrentContentVersion is not { } version)
                    {
                        failures.Add($"{plan.Name} / {task.Name}：资源检查结果不可用");
                        continue;
                    }
                    try
                    {
                        var snapshot = PuloniaResourceUpdateRules.Snapshot(task);
                        var reference = await service.PrepareUpdateAsync(snapshot, state);
                        PuloniaResourceUpdateRules.ApplyVersion(task, version, reference);
                        changed++;
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException
                                                  or ArgumentException or LibGit2Sharp.LibGit2SharpException
                                                  or System.Text.Json.JsonException or Newtonsoft.Json.JsonException)
                    {
                        failures.Add($"{plan.Name} / {task.Name}：{e.Message}");
                    }
                }
                if (changed == 0) continue;
                var first = group.First();
                try
                {
                    if (first.Item is { } item)
                    {
                        RecordUndo(item);
                        item.Plan = await _store.SavePlanAsync(item.Plan);
                        _allPlans[item.Plan.Id] = item.Plan;
                        RememberSaved(item);
                        item.Refresh();
                    }
                    else
                    {
                        _allPlans[first.Plan.Id] = await _store.SavePlanAsync(first.Plan);
                    }
                    updated += changed;
                }
                catch (Exception e)
                {
                    _log.Error(e, "保存任务计划失败");
                    failures.Add($"{first.Plan.Name}：计划保存失败：{e.Message}");
                }
            }
        }
        finally
        {
            _resourceGate.Release();
        }
        RebuildNodes();
        var summary = failures.Count == 0 ? $"已更新全部 Pulonia 任务计划中的 {updated} 项任务资源。"
            : $"已更新 {updated} 项任务资源，{failures.Count} 项失败并保持原版本。";
        StatusMessage = summary;
        if (failures.Count > 0 && displayFailures)
            await ShowInfoAsync("部分任务资源未更新", summary + "\n\n" + string.Join("\n", failures.Take(10).Select(f => "• " + f)) + (failures.Count > 10 ? "\n…" : ""));
        return (updated, failures);
    }

    /// <summary>最近一次资源确认对话框的文字（自测核对）</summary>
    public string? LastResourceDialogText { get; private set; }

    /// <summary>自测：为 true 时确认对话框直接按“是”处理，不弹出</summary>
    internal bool AutoConfirmResourceDialogs { get; set; }

    private async Task<bool> ConfirmAsync(string title, string text)
    {
        if (AutoConfirmResourceDialogs) return true;
        var owner = (global::Avalonia.Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var dialog = new FAContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 480 },
            PrimaryButtonText = "是", CloseButtonText = "否", DefaultButton = FAContentDialogButton.Close,
        };
        return await Services.FluentMessageBox.ShowCustomAsync(dialog, owner) == FAContentDialogResult.Primary;
    }

    private async Task ShowInfoAsync(string title, string text)
    {
        if (AutoConfirmResourceDialogs)
        {
            LastResourceDialogText = text;
            return;
        }
        var owner = (global::Avalonia.Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var dialog = new FAContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 480 },
            CloseButtonText = "确定",
        };
        await Services.FluentMessageBox.ShowCustomAsync(dialog, owner);
    }
}
