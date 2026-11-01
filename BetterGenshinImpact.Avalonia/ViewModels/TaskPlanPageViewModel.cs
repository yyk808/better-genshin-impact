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
/// 任务设置（名称、启用、分组次数、按 Schema 生成的参数）与定时触发的新增/编辑/启停/删除也在本页，规则来自 Core
/// （PuloniaParameterForm、PuloniaTriggerEditing）。共享设置、路线 CD、计划引用尚未迁移
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
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasNodeEditor))] private TaskNodeEditor? _nodeEditor;
    private readonly IServiceProvider? _services;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasTriggerEditor))] private TaskTriggerEditor? _triggerEditor;
    [ObservableProperty] private TaskPlanTriggerRow? _selectedTrigger;

    /// <param name="host">宿主</param>
    /// <param name="log">日志</param>
    /// <param name="purpose">本页编辑的计划用途：任务计划页为普通计划；新一条龙页为一条龙配置（对应 WPF PuloniaOneDragonPage）</param>
    /// <param name="generalPlans">新一条龙页引用普通计划时的来源（任务计划页实例）</param>
    public TaskPlanPageViewModel(GameHost? host, ILogger log, PuloniaTaskPlanPurpose purpose = PuloniaTaskPlanPurpose.General,
        TaskPlanPageViewModel? generalPlans = null)
    {
        _log = log;
        Purpose = purpose;
        _generalPlans = generalPlans;
        if (IsOneDragon) _statusMessage = "修改后立即生效并自动保存。";
        if (host is null) return;
        _services = host.Services;
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
            _tasks.LedgerChanged += (_, _) => Dispatcher.UIThread.Post(() => _ = RefreshCooldownsAsync());
        }

        if (_scheduler is not null)
        {
            _scheduler.Scheduler.Changed += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                SchedulerStatus = _scheduler.Scheduler.Status;
                OnPropertyChanged(nameof(HotkeyStatus));
            });
            // 调度器（定时 / 快捷键触发）覆盖全部计划，只由任务计划页启动一次
            if (!IsOneDragon) _scheduler.Start();
        }
    }

    public override string Title => IsOneDragon ? "新一条龙" : "任务计划";

    /// <summary>本页编辑的计划用途</summary>
    public PuloniaTaskPlanPurpose Purpose { get; }

    /// <summary>新一条龙页：平面清单（只显示根节点下一层）、默认任务、插入到选中行之后</summary>
    public bool IsOneDragon => Purpose == PuloniaTaskPlanPurpose.OneDragon;

    private readonly TaskPlanPageViewModel? _generalPlans;

    /// <summary>其他用途计划的 ID（保存计划顺序时保留，与 WPF 的全部文档顺序一致）</summary>
    private List<string> _otherPlanIds = [];

    /// <summary>当前配置最近一次（或进行中的）运行的状态（与 WPF 新一条龙底部状态相同）</summary>
    [ObservableProperty] private string _executionStatusText = "尚未执行";

    [ObservableProperty] private bool _hasActiveRun;

    public bool IsAvailable => _store is not null && _tasks is not null;

    public ObservableCollection<TaskPlanItem> Plans { get; } = [];

    public ObservableCollection<TaskPlanNodeRow> Nodes { get; } = [];

    public ObservableCollection<TaskPlanAddOption> AddOptions { get; } = [];

    public ObservableCollection<TaskPlanResourceOption> Resources { get; } = [];

    public ObservableCollection<TaskPlanRunRow> Runs { get; } = [];

    public ObservableCollection<TaskPlanTriggerRow> Triggers { get; } = [];

    public bool HasNoTriggers => SelectedPlan is not null && Triggers.Count == 0;

    public bool HasNodeEditor => NodeEditor is not null;

    public bool HasTriggerEditor => TriggerEditor is not null;

    public string PlanCountText => Plans.Count.ToString();

    public bool HasSelectedPlan => SelectedPlan is not null;

    public bool HasNoNodes => SelectedPlan is not null && Nodes.Count == 0;

    public bool HasResources => Resources.Count > 0;

    /// <summary>当前计划是否有未结束的运行</summary>
    private PuloniaTaskRunView? _latestRun;

    public bool IsSelectedPlanRunning => SelectedPlan is { } p && Runs.Any(r => r.IsActive && r.PlanId == p.Plan.Id);

    public override void OnNavigatedTo()
    {
        if (!_loaded)
        {
            _ = ReloadAsync();
        }

        // 任务计划页可能新增或删除了计划，刷新新一条龙的引用候选
        OnPropertyChanged(nameof(ReferencePlans));
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
            foreach (var plan in listed.Items.Where(p => p.Purpose == Purpose))
            {
                Plans.Add(new TaskPlanItem(plan));
            }

            _otherPlanIds = listed.Items.Where(p => p.Purpose != Purpose).Select(p => p.Id).ToList();
            // 新一条龙引用普通任务计划：任务计划页尚未打开时先读取
            if (_generalPlans is { _loaded: false } general) await general.ReloadAsync();
            // 新一条龙页首次进入且没有配置时，与 WPF 相同建立默认配置
            if (IsOneDragon && Plans.Count == 0 && listed.Failures.Count == 0)
            {
                _loaded = true;
                await NewPlanAsync();
            }

            _loaded = true;
            OnPropertyChanged(nameof(PlanCountText));
            SelectedPlan = Plans.FirstOrDefault(p => p.Plan.Id == selectedId) ?? Plans.FirstOrDefault();
            StatusMessage = $"已加载 {Plans.Count} 个计划。" + (listed.Failures.Count > 0 ? $" {listed.Failures.Count} 份计划文件无法读取。" : "");
            await ReloadProfilesAsync();
            await RefreshRunsAsync();
            await RefreshCooldownsAsync();
        }
        catch (Exception e)
        {
            _log.Error(e, "加载任务计划失败");
            StatusMessage = "加载任务计划失败：" + e.Message;
        }
    }

    partial void OnSelectedPlanChanged(TaskPlanItem? value)
    {
        if (value is not null && !_lastSaved.ContainsKey(value.Plan.Id)) RememberSaved(value);
        OnPropertyChanged(nameof(UndoCount));
        TriggerEditor = null;
        ReferenceTarget = null;
        if (CooldownsForPlanOnly) _ = RefreshCooldownsAsync();
        OnPropertyChanged(nameof(PlanProfile));
        OnPropertyChanged(nameof(ReferencePlans));
        RebuildNodes();
        // 新一条龙：切换配置后选中第一行，右侧显示其设置（与 WPF 相同）
        if (IsOneDragon) SelectedNode = Nodes.FirstOrDefault();
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
        // 重建后保持原选中行（启用开关、排序等不丢失右侧设置），新增任务时改选新行
        var keepId = SelectedNode?.Task.Id;
        Nodes.Clear();
        if (SelectedPlan is { } item)
        {
            if (IsOneDragon)
            {
                foreach (var child in item.Plan.RootTask.Children)
                {
                    var row = new TaskPlanNodeRow(this, item.Plan.RootTask, child, 0, true) { Chain = [item.Plan.RootTask, child] };
                    row.RefreshCooldown();
                    row.UpdateStatus(_latestRun, item.Plan.RootTask.Id);
                    Nodes.Add(row);
                }
            }
            else
            {
                AddRows([item.Plan.RootTask], 0, true);
            }
        }

        RefreshPlanCooldownSummary();

        OnPropertyChanged(nameof(HasNoNodes));
        var reselect = _pendingSelectTaskId ?? (SelectedNode is null || !Nodes.Contains(SelectedNode) ? keepId : null);
        _pendingSelectTaskId = null;
        if (reselect is not null)
        {
            SelectedNode = Nodes.FirstOrDefault(n => n.Task.Id == reselect);
        }
    }

    private void AddRows(List<PuloniaTask> ancestors, int depth, bool parentEnabled)
    {
        foreach (var child in ancestors[^1].Children)
        {
            List<PuloniaTask> chain = [.. ancestors, child];
            var row = new TaskPlanNodeRow(this, ancestors[^1], child, depth, parentEnabled) { Chain = chain };
            row.RefreshCooldown();
            Nodes.Add(row);
            AddRows(chain, depth + 1, parentEnabled && child.IsEnabled);
        }
    }

    private void RebuildTriggers()
    {
        var selectedId = SelectedTrigger?.Trigger.Id;
        Triggers.Clear();
        if (SelectedPlan is { } item)
        {
            foreach (var t in item.Plan.Triggers)
            {
                Triggers.Add(new TaskPlanTriggerRow(this, t));
            }
        }

        SelectedTrigger = Triggers.FirstOrDefault(t => t.Trigger.Id == selectedId);
        OnPropertyChanged(nameof(HasNoTriggers));
    }

    partial void OnSelectedNodeChanged(TaskPlanNodeRow? value)
    {
        var definition = value is null || _tasks is null ? null : PuloniaParameterResolver.FindDefinition(_tasks.Definitions.ToList(), value.Task);
        NodeEditor = value is null ? null : new TaskNodeEditor(value, definition, _services, SelectedPlan?.Plan.Id);
    }

    partial void OnSelectedTriggerChanged(TaskPlanTriggerRow? value)
    {
        if (value is not null)
        {
            TriggerEditor = new TaskTriggerEditor(value.Trigger, false);
        }
    }

    // —— 任务设置 ——

    /// <summary>应用任务设置：名称、启用、分组执行次数（1 次不保存）、参数（合并后按 Schema 校验）。全部校验通过才写入并保存</summary>
    [RelayCommand]
    public async Task ApplyNodeSettingsAsync()
    {
        if (SelectedPlan is not { } item || NodeEditor is not { } editor) return;
        var task = editor.Row.Task;
        try
        {
            var name = editor.Name.Trim();
            if (name.Length == 0) throw new FormatException("名称不能为空。");
            int? repeat = task.RepeatCount;
            if (editor.IsGroup)
            {
                var r = PuloniaTriggerEditing.ParseNumber(editor.RepeatText.Trim(), "执行次数", 1, PuloniaTaskValidator.MaxTreeNodes);
                repeat = r == 1 ? null : r;
            }

            var changes = editor.CollectChanges();
            var backup = (JObject)task.Parameters.DeepClone();
            var (oldName, oldEnabled, oldRepeat) = (task.Name, task.IsEnabled, task.RepeatCount);
            int written;
            try
            {
                written = editor.Definition is null ? 0 : PuloniaParameterForm.Apply(editor.Definition, task, changes);
                task.Name = name;
                task.IsEnabled = editor.IsEnabled;
                task.RepeatCount = repeat;
                PuloniaTaskValidator.ValidatePlan(item.Plan);
            }
            catch
            {
                // 校验失败时恢复，界面保留输入
                (task.Parameters, task.Name, task.IsEnabled, task.RepeatCount) = (backup, oldName, oldEnabled, oldRepeat);
                throw;
            }

            await SaveAsync(item, $"已保存“{name}”的设置（{written} 个参数有修改）。");
            RebuildNodes();
            SelectedNode = Nodes.FirstOrDefault(n => n.Task.Id == task.Id);
        }
        catch (Exception e) when (e is FormatException or PuloniaTaskValidationException or InvalidOperationException)
        {
            StatusMessage = "设置无效：" + e.Message;
        }
    }

    // —— 触发方式 ——

    /// <summary>新增定时触发草稿（默认关闭、每日 4 点，与 WPF 相同）</summary>
    [RelayCommand]
    public void AddScheduleTrigger()
    {
        if (SelectedPlan is null) return;
        SelectedTrigger = null;
        TriggerEditor = new TaskTriggerEditor(new PuloniaTaskTrigger(), true);
    }

    /// <summary>新增快捷键触发草稿（全局热键、关闭；键位默认 Ctrl + Shift + F8——WPF 默认的 Ctrl + Alt + F8 在 Linux 上是切换虚拟终端）</summary>
    [RelayCommand]
    public void AddHotkeyTrigger()
    {
        if (SelectedPlan is null) return;
        SelectedTrigger = null;
        TriggerEditor = new TaskTriggerEditor(new PuloniaTaskTrigger { Kind = PuloniaTaskTriggerKind.Hotkey, Hotkey = "Ctrl + Shift + F8" }, true);
    }

    /// <summary>计划快捷键的注册状态（Linux 宿主）</summary>
    public string HotkeyStatus => _scheduler?.Hotkeys is { } h
        ? (h.Registered.Count > 0 ? "已注册：" + string.Join("；", h.Registered) : "没有已注册的计划快捷键") + (h.Error.Length > 0 ? "。" + h.Error.Trim() : "")
        : "";

    /// <summary>保存定时触发：校验整个计划后写入并保存，保存成功后调度服务读取新配置</summary>
    [RelayCommand]
    public async Task SaveTriggerAsync()
    {
        if (SelectedPlan is not { } item || TriggerEditor is not { } editor) return;
        try
        {
            var trigger = editor.Build();
            trigger.ActivatedAtUtc = DateTimeOffset.UtcNow;
            item.Plan.Triggers = PuloniaTriggerEditing.WithTrigger(item.Plan, trigger).Triggers;
            await SaveAsync(item, trigger.Enabled ? "触发器已保存，稍后开始生效。" : "触发器已保存；使用列表中的开关启用。");
            RebuildTriggers();
            SelectedTrigger = Triggers.FirstOrDefault(t => t.Trigger.Id == trigger.Id);
            // 调度循环约 1 秒内读取新计划并刷新快捷键注册
            _ = Task.Delay(2000).ContinueWith(_ => Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(HotkeyStatus))));
        }
        catch (Exception e) when (e is FormatException or PuloniaTaskValidationException or ArgumentException or InvalidOperationException)
        {
            StatusMessage = "触发配置无效：" + e.Message;
        }
    }

    internal async Task SetTriggerEnabledAsync(TaskPlanTriggerRow row, bool enabled)
    {
        if (SelectedPlan is not { } item || row.Trigger.Enabled == enabled) return;
        var trigger = row.Trigger.Clone();
        trigger.Enabled = enabled;
        trigger.ActivatedAtUtc = DateTimeOffset.UtcNow;
        try
        {
            item.Plan.Triggers = PuloniaTriggerEditing.WithTrigger(item.Plan, trigger).Triggers;
            await SaveAsync(item, $"触发器“{trigger.DisplayText}”已{(enabled ? "启用" : "停用")}。");
        }
        catch (Exception e) when (e is FormatException or PuloniaTaskValidationException or ArgumentException)
        {
            StatusMessage = "无法切换触发器状态：" + e.Message;
        }

        RebuildTriggers();
    }

    [RelayCommand]
    public async Task DeleteTriggerAsync(TaskPlanTriggerRow? row)
    {
        row ??= SelectedTrigger;
        if (SelectedPlan is not { } item || row is null) return;
        item.Plan.Triggers.RemoveAll(t => t.Id == row.Trigger.Id);
        await SaveAsync(item, $"已删除触发器“{row.Text}”（执行记录保留）。");
        if (TriggerEditor?.Id == row.Trigger.Id) TriggerEditor = null;
        RebuildTriggers();
    }

    /// <summary>保存当前计划（自动保存）；失败时重新加载，避免界面与文件不一致</summary>
    private async Task SaveAsync(TaskPlanItem item, string message)
    {
        if (_store is null) return;
        try
        {
            RecordUndo(item);
            item.Plan = await _store.SavePlanAsync(item.Plan);
            _allPlans[item.Plan.Id] = item.Plan;
            RememberSaved(item);
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
        var name = string.IsNullOrWhiteSpace(NewPlanName) ? IsOneDragon ? "新一条龙配置" : "我的任务计划" : NewPlanName.Trim();
        PuloniaTaskPlan plan;
        if (IsOneDragon)
        {
            // 与 WPF 相同：重名时追加序号，默认清单为注册表声明了一条龙顺序的独立任务
            var baseName = name;
            for (var suffix = 2; Plans.Any(p => p.Name == name); suffix++) name = baseName + " " + suffix;
            plan = PuloniaOneDragonDefaults.CreatePlan(name, _tasks?.Definitions ?? []);
        }
        else
        {
            plan = new PuloniaTaskPlan
            {
                Name = name,
                RootTask = new PuloniaTask { Name = name, TaskType = "group" }
            };
        }

        try
        {
            plan = await _store.SavePlanAsync(plan);
            await _store.SavePlanOrderAsync(PlanOrder(Plans.Select(p => p.Plan.Id).Append(plan.Id)));
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

    /// <summary>计划顺序文件包含全部计划：普通计划在前，一条龙配置在后（与 WPF 编辑会话相同）</summary>
    private List<string> PlanOrder(IEnumerable<string> ids) =>
        IsOneDragon ? [.. _otherPlanIds, .. ids] : [.. ids, .. _otherPlanIds];

    /// <summary>重命名当前计划（新一条龙“重命名配置”），保持计划与任务身份</summary>
    [RelayCommand]
    private async Task RenamePlanAsync()
    {
        if (SelectedPlan is not { } item) return;
        var name = (await Services.FluentMessageBox.PromptAsync(IsOneDragon ? "请输入配置名称。" : "请输入计划名称。",
            IsOneDragon ? "重命名一条龙配置" : "重命名任务计划", item.Name))?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name == item.Name) return;
        item.Plan.Name = name;
        await SaveAsync(item, $"已重命名为“{name}”。");
        item.Refresh();
        var selected = SelectedPlan;
        // ComboBox 显示名称需要重新取值
        SelectedPlan = null;
        SelectedPlan = selected;
    }

    [RelayCommand]
    private async Task DeletePlanAsync()
    {
        if (_store is null || SelectedPlan is not { } item) return;
        // 与 WPF 相同：仍被其他计划引用时不删除
        if (Plans.FirstOrDefault(other => other != item && ReferencedPlanIds(other.Plan.RootTask).Contains(item.Plan.Id)) is { } referrer)
        {
            StatusMessage = $"计划“{referrer.Name}”仍引用此计划，请先移除引用后再删除。";
            return;
        }

        try
        {
            await _store.DeletePlanAsync(item.Plan.Id, item.Plan.Revision);
            Plans.Remove(item);
            await _store.SavePlanOrderAsync(PlanOrder(Plans.Select(p => p.Plan.Id)));
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
            InsertTask(item, task);
            await SaveAsync(item, $"已添加“{name}”。");
            RebuildNodes();
        }
        catch (Exception e)
        {
            StatusMessage = "添加任务失败：" + e.Message;
        }
    }

    /// <summary>
    /// 插入新任务：任务计划页选中分组时加到分组内，否则加到计划末尾（与 WPF 的“添加”相同）；
    /// 新一条龙始终插入根清单、选中行之后（与 WPF InsertOneDragonTask 相同）
    /// </summary>
    private void InsertTask(TaskPlanItem item, PuloniaTask task)
    {
        var root = item.Plan.RootTask;
        if (IsOneDragon)
        {
            var index = SelectedNode is { } selected && ReferenceEquals(selected.Parent, root) ? root.Children.IndexOf(selected.Task) + 1 : root.Children.Count;
            root.Children.Insert(index, task);
            _pendingSelectTaskId = task.Id;
            return;
        }

        var parent = SelectedNode is { Task.TaskType: "group", Task.Source: null } group ? group.Task : root;
        parent.Children.Add(task);
    }

    private string? _pendingSelectTaskId;

    /// <summary>可被当前计划引用的其他计划（引用选择框）；新一条龙引用普通任务计划</summary>
    public IReadOnlyList<TaskPlanItem> ReferencePlans => _generalPlans is { } general ? general.Plans.ToList() : Plans.Where(p => p != SelectedPlan).ToList();

    [ObservableProperty] private TaskPlanItem? _referenceTarget;

    /// <summary>
    /// 新增对其他计划的引用节点（与 WPF “引用已有任务计划”相同）：不复制目标计划内容，目标之后的修改同步生效；
    /// 两个计划互相引用（含间接）时拒绝。
    /// </summary>
    [RelayCommand]
    private async Task AddPlanReferenceAsync()
    {
        if (SelectedPlan is not { } item) return;
        if (ReferenceTarget is not { } target || target == item)
        {
            StatusMessage = "请选择要引用的其他计划。";
            return;
        }

        if (WouldCreateReferenceCycle(item.Plan.Id, target.Plan.Id))
        {
            StatusMessage = $"不能引用“{target.Name}”：两个计划会互相引用。";
            return;
        }

        InsertTask(item, new PuloniaTask
        {
            Name = IsOneDragon ? target.Name : "引用：" + target.Name,
            TaskType = "group",
            Source = new PuloniaTaskSource { Kind = "plan", PlanId = target.Plan.Id },
        });
        await SaveAsync(item, $"已引用计划“{target.Name}”。");
        RebuildNodes();
    }

    /// <summary>引用后能否从目标计划经引用链回到当前计划</summary>
    internal bool WouldCreateReferenceCycle(string sourcePlanId, string targetPlanId)
    {
        var plans = Plans.Concat(_generalPlans?.Plans ?? []).ToDictionary(p => p.Plan.Id, p => p.Plan);
        var visited = new HashSet<string>();
        var pending = new Stack<string>([targetPlanId]);
        while (pending.TryPop(out var id))
        {
            if (id == sourcePlanId) return true;
            if (!visited.Add(id) || !plans.TryGetValue(id, out var plan)) continue;
            foreach (var referenced in ReferencedPlanIds(plan.RootTask)) pending.Push(referenced);
        }

        return false;
    }

    internal static IEnumerable<string> ReferencedPlanIds(PuloniaTask task)
    {
        if (task.Source is { Kind: "plan", PlanId: { Length: > 0 } id }) yield return id;
        foreach (var child in task.Children)
        foreach (var nested in ReferencedPlanIds(child))
            yield return nested;
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
        SelectedNode = Nodes.FirstOrDefault(n => n.Task.Id == row.Task.Id);
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

    /// <summary>路线 CD 记录（账本中地图追踪路线的完成记录，对照 WPF 路线 CD 总览的记录列表）</summary>
    public ObservableCollection<TaskPlanCooldownRow> Cooldowns { get; } = [];

    public bool HasNoCooldowns => Cooldowns.Count == 0;

    [RelayCommand]
    private async Task RefreshCooldownsAsync()
    {
        if (_tasks is null) return;
        try
        {
            var ledger = await _tasks.ListLedgerAsync();
            _cooldownIndex = new PuloniaCollectionCooldownIndex(ledger);
            await LoadCooldownContextAsync();
            RefreshCooldownBadges();
            var planFilter = CooldownsForPlanOnly && SelectedPlan is { } plan ? CreatePlanCooldownFilter(plan.Plan) : null;
            Cooldowns.Clear();
            foreach (var entry in _cooldownIndex.Entries
                         .Where(e => e.EffectKey.StartsWith(PuloniaCollectionCooldown.EffectKeyPrefix, StringComparison.Ordinal))
                         .Where(e => planFilter is null || planFilter(e))
                         .OrderBy(e => e.NextEligibleAt ?? DateTimeOffset.MaxValue))
            {
                Cooldowns.Add(new TaskPlanCooldownRow(entry, DescribeScope(entry.ScopeKey)));
            }

            OnPropertyChanged(nameof(HasNoCooldowns));
        }
        catch (Exception e)
        {
            StatusMessage = "读取路线 CD 记录失败：" + e.Message;
        }
    }

    /// <summary>清除一条路线 CD 记录（与 WPF 相同调用 ClearCollectionCooldownAsync，下次运行不再按 CD 跳过）</summary>
    [RelayCommand]
    private async Task ClearCooldownAsync(TaskPlanCooldownRow? row)
    {
        if (_tasks is null || row is null) return;
        try
        {
            var count = await _tasks.ClearCollectionCooldownAsync([new PuloniaCollectionCooldownKey(row.Entry.ScopeKey, row.Entry.EffectKey)]);
            StatusMessage = $"已清除 {count} 条路线 CD 记录。";
            await RefreshCooldownsAsync();
        }
        catch (Exception e)
        {
            StatusMessage = "清除路线 CD 记录失败：" + e.Message;
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

            // 当前配置的运行状态：进行中的运行优先，否则为最近一次（与 WPF 新一条龙相同）
            var mine = runs.Where(r => r.PlanId == SelectedPlan?.Plan.Id).OrderByDescending(r => r.SubmittedAt).ToList();
            var active = mine.FirstOrDefault(r => r.Status is PuloniaTaskRunStatus.Running or PuloniaTaskRunStatus.Cancelling)
                         ?? mine.FirstOrDefault(r => r.Status == PuloniaTaskRunStatus.Queued);
            _latestRun = active ?? mine.FirstOrDefault();
            HasActiveRun = active is not null;
            ExecutionStatusText = _latestRun is { } latest
                ? (latest.ExecutionTarget is { } target ? $"[{target.DisplayName}] " : "") + PuloniaTaskRunStatusText.Get(latest.Status) + " · " + latest.Message
                : "尚未执行";
            if (SelectedPlan is { } current)
            {
                foreach (var row in Nodes) row.UpdateStatus(_latestRun, current.Plan.RootTask.Id);
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

    /// <summary>计划根到本节点（含）的模型链，用于解析共享设置与路线 CD</summary>
    public IReadOnlyList<PuloniaTask> Chain { get; init; } = [];

    /// <summary>节点自身有共享设置或挂了方案</summary>
    public bool HasOwnSettings => Task.Settings.Count > 0 || Task.ProfileId is not null;

    [ObservableProperty] private string? _cooldownBadge;

    [ObservableProperty] private string? _cooldownTip;

    public void RefreshCooldown() => (CooldownBadge, CooldownTip) = _owner.DescribeCooldown(Chain.Count > 0 ? Chain : [Task]);

    public Thickness Indent => new(Depth * 20, 0, 0, 0);

    public string Name => Task.Name;

    public string TypeText => PuloniaTaskTypeText.Get(Task);

    public string Detail => Task.Source is { Kind: "plan" } ? "引用计划（目标修改同步生效）" : Task.Source?.Path ?? Task.Path ?? Task.Resource?.ScopeKey ?? "";

    public double RowOpacity => _parentEnabled && Task.IsEnabled ? 1 : 0.5;

    public bool IsEnabled
    {
        get => Task.IsEnabled;
        set => _ = _owner.SetEnabledAsync(this, value);
    }

    /// <summary>新一条龙清单行的最近运行状态（规则在 Core PuloniaTaskRunClassifier，与 WPF 相同）</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StatusBrush))] private PuloniaTaskSubtreeStatus _statusKind;

    [ObservableProperty] private string _statusText = "尚未执行";

    public void UpdateStatus(PuloniaTaskRunView? run, string rootTaskId)
    {
        StatusKind = PuloniaTaskRunClassifier.ClassifySubtree(run, rootTaskId, Task.Id);
        StatusText = PuloniaTaskRunClassifier.SubtreeStatusText(StatusKind);
    }

    /// <summary>状态圆点颜色（WPF：未执行灰、执行中蓝、完成绿、失败红、待核验橙）</summary>
    public global::Avalonia.Media.IBrush StatusBrush => global::Avalonia.Media.Brush.Parse(StatusKind switch
    {
        PuloniaTaskSubtreeStatus.Running => "#3E9EFF",
        PuloniaTaskSubtreeStatus.Completed => "#46A758",
        PuloniaTaskSubtreeStatus.Failed => "#E5484D",
        PuloniaTaskSubtreeStatus.NeedsVerification => "#F5A524",
        _ => "#8A8A8A",
    });
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

/// <summary>路线 CD 记录行</summary>
public sealed class TaskPlanCooldownRow(PuloniaTaskLedgerEntry entry, string scope)
{
    public PuloniaTaskLedgerEntry Entry => entry;

    public string Route => entry.EffectKey[PuloniaCollectionCooldown.EffectKeyPrefix.Length..];

    public string Scope => scope;

    public string Detail => $"{Scope} · 完成于 {entry.OccurredAt.ToLocalTime():MM-dd HH:mm}"
        // 记录中的下次可执行时间按记录时的规则；当前规则下的状态见任务树标记
        + (entry.NextEligibleAt is { } next ? (next > DateTimeOffset.Now ? $" · 记录时 CD 至 {next.ToLocalTime():MM-dd HH:mm}" : " · 已过记录时的 CD") : "");
}
