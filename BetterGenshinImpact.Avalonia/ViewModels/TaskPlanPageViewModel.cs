using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Threading;
using BetterGenshinImpact.Host.Common;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using ILogger = Serilog.ILogger;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// “任务计划”页（对应 WPF PuloniaTaskPlanPage）：计划列表、任务列表（添加 / 删除 / 排序 / 启用）、运行与停止、执行记录、触发方式。
/// 计划模型、存储（User/Pulonia，与 Windows 版共用）、执行与调度都在 Core；本页只做编辑与展示。
/// 共享设置、路线 CD、计划引用、任务参数编辑、计划快捷键触发尚未迁移
/// </summary>
public sealed partial class TaskPlanPageViewModel : PageViewModel
{
    private readonly ILogger _log;
    private readonly PuloniaTaskStore? _store;
    private readonly IPuloniaTaskService? _tasks;
    private readonly PuloniaTaskResourceCatalog? _catalog;
    private readonly PuloniaTaskCreationService? _creation;
    private readonly PuloniaSchedulerHost? _scheduler;
    private bool _loaded;

    [ObservableProperty] private TaskPlanItem? _selectedPlan;
    [ObservableProperty] private TaskPlanNodeRow? _selectedNode;
    [ObservableProperty] private TaskPlanAddOption? _selectedAddOption;
    [ObservableProperty] private TaskPlanResourceOption? _selectedResource;
    [ObservableProperty] private string _newPlanName = "";
    [ObservableProperty] private string _statusMessage = "使用“添加”把任务加入当前计划；所有修改都会自动保存。";
    [ObservableProperty] private string _schedulerStatus = "";
    [ObservableProperty] private int _selectedTab;

    public TaskPlanPageViewModel(GameHost? host, ILogger log)
    {
        _log = log;
        if (host is null) return;
        _store = host.Services.GetService<PuloniaTaskStore>();
        _tasks = host.Services.GetService<IPuloniaTaskService>();
        _catalog = host.Services.GetService<PuloniaTaskResourceCatalog>();
        _creation = host.Services.GetService<PuloniaTaskCreationService>();
        _scheduler = host.Services.GetService<PuloniaSchedulerHost>();
        if (_tasks is not null)
        {
            foreach (var d in _tasks.Definitions.Where(d => d.TaskType != "csharp")
                         .OrderBy(d => d.IsSoloTask).ThenBy(d => d.Group, StringComparer.Ordinal).ThenBy(d => d.Order))
            {
                AddOptions.Add(new TaskPlanAddOption(d));
            }

            _tasks.RunChanged += (_, _) => Dispatcher.UIThread.Post(() => _ = RefreshRunsAsync());
        }

        if (_scheduler is not null)
        {
            _scheduler.Scheduler.Changed += (_, _) => Dispatcher.UIThread.Post(() => SchedulerStatus = _scheduler.Scheduler.Status);
            _scheduler.Start();
        }
    }

    public override string Title => "任务计划";

    public bool IsAvailable => _store is not null && _tasks is not null;

    public ObservableCollection<TaskPlanItem> Plans { get; } = [];

    public ObservableCollection<TaskPlanNodeRow> Nodes { get; } = [];

    public ObservableCollection<TaskPlanAddOption> AddOptions { get; } = [];

    public ObservableCollection<TaskPlanResourceOption> Resources { get; } = [];

    public ObservableCollection<TaskPlanRunRow> Runs { get; } = [];

    public ObservableCollection<string> Triggers { get; } = [];

    public string PlanCountText => Plans.Count.ToString();

    public bool HasSelectedPlan => SelectedPlan is not null;

    public bool HasNoNodes => SelectedPlan is not null && Nodes.Count == 0;

    public bool HasResources => Resources.Count > 0;

    /// <summary>当前计划是否有未结束的运行</summary>
    public bool IsSelectedPlanRunning => SelectedPlan is { } p && Runs.Any(r => r.IsActive && r.PlanId == p.Plan.Id);

    public override void OnNavigatedTo()
    {
        if (!_loaded)
        {
            _ = ReloadAsync();
        }
    }

    [RelayCommand]
    public async Task ReloadAsync()
    {
        if (_store is null) return;
        try
        {
            var selectedId = SelectedPlan?.Plan.Id;
            var listed = await _store.ListPlansWithFailuresAsync();
            Plans.Clear();
            foreach (var plan in listed.Items.Where(p => p.Purpose == PuloniaTaskPlanPurpose.General))
            {
                Plans.Add(new TaskPlanItem(plan));
            }

            _loaded = true;
            OnPropertyChanged(nameof(PlanCountText));
            SelectedPlan = Plans.FirstOrDefault(p => p.Plan.Id == selectedId) ?? Plans.FirstOrDefault();
            StatusMessage = $"已加载 {Plans.Count} 个计划。" + (listed.Failures.Count > 0 ? $" {listed.Failures.Count} 份计划文件无法读取。" : "");
            await RefreshRunsAsync();
        }
        catch (Exception e)
        {
            _log.Error(e, "加载任务计划失败");
            StatusMessage = "加载任务计划失败：" + e.Message;
        }
    }

    partial void OnSelectedPlanChanged(TaskPlanItem? value)
    {
        RebuildNodes();
        RebuildTriggers();
        OnPropertyChanged(nameof(HasSelectedPlan));
        _ = RefreshRunsAsync();
    }

    partial void OnSelectedAddOptionChanged(TaskPlanAddOption? value) => _ = LoadResourcesAsync(value);

    private async Task LoadResourcesAsync(TaskPlanAddOption? option)
    {
        Resources.Clear();
        SelectedResource = null;
        if (option is null || _catalog is null || option.Definition.ResourceBaseDirectory is null && option.Definition.TaskType is not ("pathing" or "javascript"))
        {
            OnPropertyChanged(nameof(HasResources));
            return;
        }

        try
        {
            var index = await _catalog.GetIndexAsync(option.Definition, forceRefresh: true);
            foreach (var r in index.Resources.Where(r => !r.IsRootDirectory).Take(2000))
            {
                Resources.Add(new TaskPlanResourceOption(r));
            }
        }
        catch (Exception e)
        {
            StatusMessage = "读取资源失败：" + e.Message;
        }

        SelectedResource = Resources.FirstOrDefault();
        OnPropertyChanged(nameof(HasResources));
    }

    private void RebuildNodes()
    {
        Nodes.Clear();
        if (SelectedPlan is { } item)
        {
            AddRows(item.Plan.RootTask, 0, true);
        }

        OnPropertyChanged(nameof(HasNoNodes));
    }

    private void AddRows(PuloniaTask parent, int depth, bool parentEnabled)
    {
        foreach (var child in parent.Children)
        {
            Nodes.Add(new TaskPlanNodeRow(this, parent, child, depth, parentEnabled));
            AddRows(child, depth + 1, parentEnabled && child.IsEnabled);
        }
    }

    private void RebuildTriggers()
    {
        Triggers.Clear();
        if (SelectedPlan is not { } item) return;
        foreach (var t in item.Plan.Triggers)
        {
            var kind = t.Kind == PuloniaTaskTriggerKind.Hotkey ? "快捷键（Linux 尚未注册计划快捷键）" : "定时";
            Triggers.Add($"{(t.Enabled ? "启用" : "停用")}　{kind}　{t.DisplayText}");
        }

        if (Triggers.Count == 0)
        {
            Triggers.Add("没有触发方式：只能手动运行。可在 Windows 版的“触发方式”页签添加定时触发，Linux 上照常按时执行。");
        }
    }

    /// <summary>保存当前计划（自动保存）；失败时重新加载，避免界面与文件不一致</summary>
    private async Task SaveAsync(TaskPlanItem item, string message)
    {
        if (_store is null) return;
        try
        {
            item.Plan = await _store.SavePlanAsync(item.Plan);
            item.Refresh();
            StatusMessage = message;
        }
        catch (Exception e)
        {
            _log.Error(e, "保存任务计划失败");
            StatusMessage = "保存失败：" + e.Message;
            await ReloadAsync();
        }
    }

    internal async Task SetEnabledAsync(TaskPlanNodeRow row, bool enabled)
    {
        if (SelectedPlan is not { } item || row.Task.IsEnabled == enabled) return;
        row.Task.IsEnabled = enabled;
        await SaveAsync(item, $"已{(enabled ? "启用" : "停用")}“{row.Name}”。");
        RebuildNodes();
    }

    [RelayCommand]
    private async Task NewPlanAsync()
    {
        if (_store is null) return;
        var name = string.IsNullOrWhiteSpace(NewPlanName) ? "我的任务计划" : NewPlanName.Trim();
        var plan = new PuloniaTaskPlan
        {
            Name = name,
            RootTask = new PuloniaTask { Name = name, TaskType = "group" }
        };
        try
        {
            plan = await _store.SavePlanAsync(plan);
            var order = Plans.Select(p => p.Plan.Id).Append(plan.Id).ToList();
            await _store.SavePlanOrderAsync(order);
            var item = new TaskPlanItem(plan);
            Plans.Add(item);
            OnPropertyChanged(nameof(PlanCountText));
            SelectedPlan = item;
            NewPlanName = "";
            StatusMessage = $"已新建计划“{name}”。";
        }
        catch (Exception e)
        {
            StatusMessage = "新建计划失败：" + e.Message;
        }
    }

    [RelayCommand]
    private async Task DeletePlanAsync()
    {
        if (_store is null || SelectedPlan is not { } item) return;
        try
        {
            await _store.DeletePlanAsync(item.Plan.Id, item.Plan.Revision);
            Plans.Remove(item);
            await _store.SavePlanOrderAsync(Plans.Select(p => p.Plan.Id).ToList());
            OnPropertyChanged(nameof(PlanCountText));
            SelectedPlan = Plans.FirstOrDefault();
            StatusMessage = $"已删除计划“{item.Name}”（执行记录保留）。";
        }
        catch (Exception e)
        {
            StatusMessage = "删除计划失败：" + e.Message;
        }
    }

    [RelayCommand]
    private async Task AddTaskAsync()
    {
        if (_creation is null || SelectedPlan is not { } item || SelectedAddOption is not { } option) return;
        var resource = SelectedResource?.Descriptor;
        if (HasResources && resource is null)
        {
            StatusMessage = "请选择要添加的资源。";
            return;
        }

        try
        {
            var name = resource?.DisplayName ?? option.Definition.DisplayName;
            var task = await _creation.BuildAsync(new PuloniaTaskCreationRequest
            {
                Name = name,
                Definition = option.Definition,
                Resource = resource,
                Overrides = new JObject(),
            });
            // 选中分组时加到分组内，否则加到计划末尾（与 WPF 的“添加”相同）
            var parent = SelectedNode is { Task.TaskType: "group", Task.Source: null } group ? group.Task : item.Plan.RootTask;
            parent.Children.Add(task);
            await SaveAsync(item, $"已添加“{name}”。");
            RebuildNodes();
        }
        catch (Exception e)
        {
            StatusMessage = "添加任务失败：" + e.Message;
        }
    }

    [RelayCommand]
    private async Task AddGroupAsync()
    {
        if (SelectedPlan is not { } item) return;
        item.Plan.RootTask.Children.Add(new PuloniaTask { Name = "新分组", TaskType = "group" });
        await SaveAsync(item, "已添加分组。");
        RebuildNodes();
    }

    [RelayCommand]
    private async Task RemoveTaskAsync()
    {
        if (SelectedPlan is not { } item || SelectedNode is not { } row) return;
        row.Parent.Children.Remove(row.Task);
        await SaveAsync(item, $"已删除“{row.Name}”。");
        RebuildNodes();
    }

    [RelayCommand]
    private async Task MoveTaskAsync(string? direction)
    {
        if (SelectedPlan is not { } item || SelectedNode is not { } row) return;
        var list = row.Parent.Children;
        var i = list.IndexOf(row.Task);
        var j = direction == "up" ? i - 1 : i + 1;
        if (i < 0 || j < 0 || j >= list.Count) return;
        (list[i], list[j]) = (list[j], list[i]);
        await SaveAsync(item, $"已移动“{row.Name}”。");
        RebuildNodes();
        SelectedNode = Nodes.FirstOrDefault(n => n.Task == row.Task);
    }

    /// <summary>提交运行（与 WPF 相同：保存后入队，提交时固定快照，页面入口不限时）</summary>
    [RelayCommand]
    public async Task<string?> RunPlanAsync(TaskPlanItem? item)
    {
        item ??= SelectedPlan;
        if (_tasks is null || item is null) return null;
        SelectedPlan = item;
        try
        {
            var runId = await _tasks.EnqueueAsync(new PuloniaTaskRequest { PlanId = item.Plan.Id, Source = "ui" });
            StatusMessage = $"已提交“{item.Name}”，将按提交顺序执行；之后的修改不影响本次运行。";
            _log.Information("任务计划：已提交 {Plan}（{RunId}）", item.Name, runId);
            await RefreshRunsAsync();
            return runId;
        }
        catch (Exception e)
        {
            StatusMessage = "提交运行失败：" + e.Message;
            UiHost.Dialogs.Toast(DialogKind.Error, StatusMessage);
            return null;
        }
    }

    /// <summary>停止当前计划所有未结束的运行</summary>
    [RelayCommand]
    public async Task StopPlanAsync()
    {
        if (_tasks is null || SelectedPlan is not { } item) return;
        foreach (var run in Runs.Where(r => r.IsActive && r.PlanId == item.Plan.Id).ToList())
        {
            try
            {
                await _tasks.CancelAsync(run.RunId);
                _log.Information("任务计划：已请求停止 {Plan}（{RunId}）", item.Name, run.RunId);
            }
            catch (Exception e)
            {
                StatusMessage = "停止失败：" + e.Message;
            }
        }
    }

    [RelayCommand]
    private async Task RefreshRunsAsync()
    {
        if (_tasks is null) return;
        try
        {
            var runs = await _tasks.ListRunsAsync();
            Runs.Clear();
            foreach (var r in runs.Where(r => SelectedPlan is null || r.PlanId == SelectedPlan.Plan.Id)
                         .OrderByDescending(r => r.SubmittedAt).Take(50))
            {
                Runs.Add(new TaskPlanRunRow(r));
            }

            foreach (var p in Plans)
            {
                p.IsRunning = runs.Any(r => r.PlanId == p.Plan.Id && PuloniaTaskRunStatusText.IsActive(r.Status));
            }

            OnPropertyChanged(nameof(IsSelectedPlanRunning));
        }
        catch (Exception e)
        {
            _log.Warning("读取执行记录失败：{Message}", e.Message);
        }
    }

    /// <summary>等待指定运行结束（无人值守测试用）</summary>
    public async Task<PuloniaTaskRunView?> WaitForRunAsync(string runId, CancellationToken ct)
        => _tasks is null ? null : await _tasks.WaitForCompletionAsync(runId, ct);
}

/// <summary>计划列表中的一项</summary>
public sealed partial class TaskPlanItem(PuloniaTaskPlan plan) : ObservableObject
{
    [ObservableProperty] private bool _isRunning;

    public PuloniaTaskPlan Plan { get; set; } = plan;

    public string Name => Plan.Name;

    public string Summary => $"{CountTasks(Plan.RootTask)} 个任务" + (Plan.Triggers.Count > 0 ? $" · {Plan.Triggers.Count(t => t.Enabled)} 个触发" : "");

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Summary));
    }

    private static int CountTasks(PuloniaTask t) => t.Children.Sum(c => (c.TaskType == "group" && c.Source is null ? 0 : 1) + CountTasks(c));
}

/// <summary>任务列表中的一行（任务树按深度缩进展开）</summary>
public sealed partial class TaskPlanNodeRow : ObservableObject
{
    private readonly TaskPlanPageViewModel _owner;
    private readonly bool _parentEnabled;

    public TaskPlanNodeRow(TaskPlanPageViewModel owner, PuloniaTask parent, PuloniaTask task, int depth, bool parentEnabled)
    {
        _owner = owner;
        Parent = parent;
        Task = task;
        Depth = depth;
        _parentEnabled = parentEnabled;
    }

    public PuloniaTask Parent { get; }

    public PuloniaTask Task { get; }

    public int Depth { get; }

    public Thickness Indent => new(Depth * 20, 0, 0, 0);

    public string Name => Task.Name;

    public string TypeText => PuloniaTaskTypeText.Get(Task);

    public string Detail => Task.Source?.Path ?? Task.Path ?? Task.Resource?.ScopeKey ?? "";

    public double RowOpacity => _parentEnabled && Task.IsEnabled ? 1 : 0.5;

    public bool IsEnabled
    {
        get => Task.IsEnabled;
        set => _ = _owner.SetEnabledAsync(this, value);
    }
}

/// <summary>“添加”可选的任务类型（Core 执行器注册的能力）</summary>
public sealed class TaskPlanAddOption(PuloniaTaskDefinition definition)
{
    public PuloniaTaskDefinition Definition => definition;

    public override string ToString() => definition.IsSoloTask ? $"独立任务：{definition.DisplayName}" : definition.DisplayName;
}

/// <summary>可添加的资源（录制文件、路线、JS 项目）</summary>
public sealed class TaskPlanResourceOption(PuloniaTaskResourceDescriptor descriptor)
{
    public PuloniaTaskResourceDescriptor Descriptor => descriptor;

    public override string ToString() => descriptor.IsDirectory ? $"{descriptor.RelativePath}（目录）" : descriptor.RelativePath;
}

/// <summary>执行记录中的一行</summary>
public sealed class TaskPlanRunRow(PuloniaTaskRunView run)
{
    public string RunId => run.RunId;

    public string PlanId => run.PlanId;

    public bool IsActive => PuloniaTaskRunStatusText.IsActive(run.Status);

    public string StatusText => PuloniaTaskRunStatusText.Get(run.Status);

    public string TimeText => $"{run.SubmittedAt.ToLocalTime():MM-dd HH:mm:ss}" +
                              (run.FinishedAt is { } f && run.StartedAt is { } s ? $"　耗时 {(f - s).TotalSeconds:0.0} 秒" : "");

    public string SourceText => run.Source switch { "ui" => "手动", "cli" => "命令行", "schedule" or "timer" => "定时", "hotkey" => "快捷键", _ => run.Source };

    public string Message => run.Message;
}
