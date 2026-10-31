using System.Collections.ObjectModel;
using Avalonia.Threading;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Host.Common;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Presentation;
using BetterGenshinImpact.Pulonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// “执行记录”页（对应 WPF PuloniaTaskHistoryPage）：全部计划与一条龙的运行记录、节点详情、停止 / 继续剩余 / 从选中任务开始，以及路线 CD。
/// 记录条目的展示文字与 WPF 版共用 Core 的 <see cref="PuloniaTaskRunItem"/> / <see cref="PuloniaTaskNodeResultItem"/>
/// </summary>
public sealed partial class TaskHistoryPageViewModel : PageViewModel
{
    private readonly IPuloniaTaskService? _tasks;
    private readonly ILogger _log;
    private readonly IReadOnlyDictionary<string, string> _typeNames = new Dictionary<string, string>();
    private readonly SemaphoreSlim _gate = new(1, 1);

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSelectedRun), nameof(CanCancelRun), nameof(CanResumeRun), nameof(CanResumeFromNode))]
    [NotifyCanExecuteChangedFor(nameof(CancelRunCommand), nameof(ResumeRunCommand), nameof(ResumeFromNodeCommand))]
    private HistoryRunRow? _selectedRun;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSelectedNode), nameof(CanResumeFromNode))]
    [NotifyCanExecuteChangedFor(nameof(ResumeFromNodeCommand))]
    private HistoryNodeRow? _selectedNode;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasStatusMessage))] private string _statusMessage = string.Empty;

    public TaskHistoryPageViewModel(GameHost? host, TaskPlanPageViewModel taskPlan, ILogger log)
    {
        _log = log;
        TaskPlan = taskPlan;
        _tasks = host?.Services.GetService<IPuloniaTaskService>();
        StorageDirectory = host?.Services.GetService<PuloniaTaskStore>()?.RootDirectory ?? "";
        if (_tasks is null) return;
        _typeNames = _tasks.Definitions.GroupBy(d => d.TaskType).ToDictionary(g => g.Key, g => g.First().DisplayName);
        _tasks.RunChanged += (_, _) => Dispatcher.UIThread.Post(() => _ = RefreshAsync());
    }

    public override string Title => "执行记录";

    /// <summary>路线 CD 选项卡与任务计划页共用同一份记录</summary>
    public TaskPlanPageViewModel TaskPlan { get; }

    public bool IsAvailable => _tasks is not null;

    public string StorageDirectory { get; }

    public ObservableCollection<HistoryRunRow> Runs { get; } = [];

    public string CountText => $"共 {Runs.Count} 条";

    public bool IsEmpty => Runs.Count == 0;

    public bool HasSelectedRun => SelectedRun is not null;

    public bool HasSelectedNode => SelectedNode is not null;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public bool CanCancelRun => SelectedRun?.Item.CanCancel == true;

    public bool CanResumeRun => SelectedRun?.Item.CanResume == true;

    public bool CanResumeFromNode => SelectedRun?.Item.IsHistorical == true && SelectedNode is not null
        && SelectedNode.Item.Result.TaskType != "genshin.account.prepare";

    public override void OnNavigatedTo()
    {
        _ = RefreshAsync();
        _ = TaskPlan.RefreshCooldownsCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private Task RefreshRunsAsync() => RefreshAsync();

    /// <summary>读取运行记录（与 WPF 相同从统一运行服务读取：进程内队列、当前运行与本地历史），保持原选中</summary>
    public async Task RefreshAsync(string? preferredRunId = null)
    {
        if (_tasks is null) return;
        await _gate.WaitAsync();
        try
        {
            var runs = await _tasks.ListRunsAsync();
            var selectedId = preferredRunId ?? SelectedRun?.Item.RunId;
            var selectedAddress = SelectedNode?.Item.TaskAddress;
            var selectedAttempt = SelectedNode?.Item.Attempt;
            var cached = Runs.Where(r => r.Item.IsHistorical).ToDictionary(r => r.Item.RunId);
            var rows = runs.OrderByDescending(r => r.SubmittedAt)
                .Select(r => r.IsHistorical && cached.TryGetValue(r.RunId, out var c) ? c : new HistoryRunRow(new PuloniaTaskRunItem(r, _typeNames)))
                .ToList();
            Runs.Clear();
            foreach (var row in rows) Runs.Add(row);
            SelectedRun = selectedId is null ? null : Runs.FirstOrDefault(r => r.Item.RunId == selectedId);
            if (SelectedRun is { } current && selectedAddress is not null)
            {
                SelectedNode = current.Nodes.FirstOrDefault(n => n.Item.TaskAddress == selectedAddress && n.Item.Attempt == selectedAttempt) ?? SelectedNode;
            }

            if (_tasks.RecoveryNotice is { } notice) StatusMessage = notice;
            OnPropertyChanged(nameof(CountText));
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (Exception e)
        {
            StatusMessage = "刷新执行记录失败：" + e.Message;
            _log.Warning("读取执行记录失败：{Message}", e.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    partial void OnSelectedRunChanged(HistoryRunRow? value)
    {
        // 与 WPF 相同：默认选中最近一个失败或需要核对的任务，其次是最后一个任务
        var node = value?.Nodes.LastOrDefault(n => n.Item.StatusCategory is PuloniaTaskRunStatusCategory.Failed or PuloniaTaskRunStatusCategory.Attention)
                   ?? value?.Nodes.LastOrDefault();
        SelectedNode = node;
        // 节点列表换数据源时 ListBox 会先清空选择，布局更新后再选中
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(SelectedRun, value)) SelectedNode = node;
        }, DispatcherPriority.Background);
    }

    [RelayCommand(CanExecute = nameof(CanCancelRun))]
    private async Task CancelRunAsync()
    {
        if (_tasks is null || SelectedRun is not { } selected) return;
        try
        {
            StatusMessage = "正在停止，等待当前任务退出……";
            await _tasks.CancelAsync(selected.Item.RunId);
            await RefreshAsync(selected.Item.RunId);
            StatusMessage = "已停止。";
        }
        catch (Exception e)
        {
            StatusMessage = "停止失败：" + e.Message;
        }
    }

    /// <summary>继续剩余：沿用原快照，跳过已确认成功的任务（与 WPF 相同，有结果无法确认的任务时先确认）</summary>
    [RelayCommand(CanExecute = nameof(CanResumeRun))]
    private async Task ResumeRunAsync()
    {
        if (_tasks is null || SelectedRun is not { } selected) return;
        var uncertain = selected.Item.NeedsVerification;
        if (uncertain && await UiHost.Dialogs.ShowAsync("这次运行有结果无法确认的任务，继续会再执行一次这些任务。请先在游戏中核对。\n是否继续？",
                "继续剩余任务", DialogKind.Warning, DialogButtons.YesNo) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            var runId = await _tasks.ResumeAsync(selected.Item.RunId, null, uncertain);
            await RefreshAsync(runId);
            StatusMessage = "已开始续跑：沿用这次运行当时的设置，跳过已经确认完成的任务。";
        }
        catch (Exception e)
        {
            StatusMessage = "继续失败：" + e.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanResumeFromNode))]
    private async Task ResumeFromNodeAsync()
    {
        if (_tasks is null || SelectedRun is not { } selected || SelectedNode is not { } node) return;
        var uncertain = selected.Item.NeedsVerification;
        if (await UiHost.Dialogs.ShowAsync($"将沿用这次运行当时的设置，从“{node.Item.TaskName}”开始重新执行，它和之后的任务都会再执行一次。"
                                           + (uncertain ? "\n这次运行有结果无法确认的任务，请先在游戏中核对。" : "") + "\n是否继续？",
                "从选中任务开始", DialogKind.Warning, DialogButtons.YesNo) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            var runId = await _tasks.ResumeAsync(selected.Item.RunId, node.Item.TaskAddress, uncertain);
            await RefreshAsync(runId);
            StatusMessage = $"已开始续跑：沿用这次运行当时的设置，从“{node.Item.TaskName}”开始。";
        }
        catch (Exception e)
        {
            StatusMessage = "从选中任务开始失败：" + e.Message;
        }
    }
}

/// <summary>一条运行记录（展示文字来自 Core 的 PuloniaTaskRunItem，图标按名称取 Fluent 图标）</summary>
public sealed class HistoryRunRow(PuloniaTaskRunItem item)
{
    private IReadOnlyList<HistoryNodeRow>? _nodes;

    public PuloniaTaskRunItem Item => item;

    public FAIconSource Icon => WpfIcons.Source(item.StatusIconName);

    public string StatusKind => item.StatusKind;

    public bool IsSuccess => StatusKind == "success";

    public bool IsFailed => StatusKind == "failed";

    public bool IsAttention => StatusKind == "attention";

    public bool IsRunning => StatusKind == "running";

    public IReadOnlyList<HistoryNodeRow> Nodes => _nodes ??= item.NodeDetails.Select(n => new HistoryNodeRow(n)).ToList();
}

/// <summary>运行详情中的一次节点尝试</summary>
public sealed class HistoryNodeRow(PuloniaTaskNodeResultItem item)
{
    public PuloniaTaskNodeResultItem Item => item;

    public FAIconSource Icon => WpfIcons.Source(item.StatusIconName);

    public string StatusKind => item.StatusKind;

    public bool IsSuccess => StatusKind == "success";

    public bool IsFailed => StatusKind == "failed";

    public bool IsAttention => StatusKind == "attention";

    public bool IsRunning => StatusKind == "running";
}
