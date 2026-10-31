using System.Collections.ObjectModel;
using System.ComponentModel;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.OneDragon;
using BetterGenshinImpact.Host.Common;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.ViewModel.Pages.OneDragon;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using ILogger = Serilog.ILogger;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// “一条龙”页（对应 WPF 的 OneDragonFlowPage 的主体）：选择 User/OneDragon 下的一条龙配置，开关任务、“从此执行”，
/// 任务项 OneDragonTaskItem 与 WPF 共用；右栏为各任务的详细设置，左栏可新增/删除/复制/排序任务，配置可新增/重命名/删除
/// 任务项 OneDragonTaskItem 与 WPF 共用；各任务的详细设置（秘境、首领、地脉花等）尚未迁移，使用配置文件中的值
/// </summary>
public sealed partial class OneDragonPageViewModel : PageViewModel
{
    private readonly ILogger _log;
    private bool _loading;

    [ObservableProperty] private OneDragonFlowConfig? _selectedConfig;
    [ObservableProperty] private OneDragonTaskItem? _selectedTask;

    public OneDragonPageViewModel(GameHost? host, TaskSettingsPageViewModel tasks, ILogger log)
    {
        _log = log;
        Tasks = tasks;
        Config = host?.Config ?? new AllConfig();
        IsAvailable = host is not null;
        tasks.OneDragonParamProvider = CreateOneDragonParam;
    }

    public override string Title => "一条龙";

    public AllConfig Config { get; }

    public bool IsAvailable { get; }

    /// <summary>独立任务页：状态与启动/停止（一条龙是一个独立任务）</summary>
    public TaskSettingsPageViewModel Tasks { get; }

    public ObservableCollection<OneDragonFlowConfig> ConfigList { get; } = [];

    public ObservableCollection<OneDragonTaskItem> TaskList { get; } = [];

    public List<string> CompletionActionList { get; } = ["无", "关闭游戏", "关闭软件", "关闭游戏和软件", "关机"];

    public string NextTaskText => TaskList.FirstOrDefault(t => t.IsNextTask) is { } t ? $"下次从“{t.Name}”开始执行" : "从头开始执行";

    public override void OnNavigatedTo()
    {
        if (ConfigList.Count == 0)
        {
            InitConfigList();
        }
    }

    /// <summary>读取 User/OneDragon/*.json（与 WPF 相同：选中配置根中的配置名，没有配置时新建“默认配置”）</summary>
    public void InitConfigList()
    {
        _loading = true;
        try
        {
            Directory.CreateDirectory(OneDragonFlowConfigStore.Folder);
            var configs = new List<OneDragonFlowConfig>();
            foreach (var file in Directory.GetFiles(OneDragonFlowConfigStore.Folder, "*.json").Order(StringComparer.Ordinal))
            {
                try
                {
                    if (JsonConvert.DeserializeObject<OneDragonFlowConfig>(File.ReadAllText(file)) is { } c)
                    {
                        configs.Add(c);
                    }
                }
                catch (Exception e)
                {
                    _log.Warning("读取一条龙配置 {File} 失败：{Message}", file, e.Message);
                }
            }

            if (configs.Count == 0)
            {
                configs.Add(CreateDefaultConfig());
            }

            ConfigList.Clear();
            foreach (var c in configs)
            {
                ConfigList.Add(c);
            }

            SelectedConfig = configs.FirstOrDefault(c => c.Name == Config.SelectedOneDragonFlowConfigName) ?? configs[0];
        }
        finally
        {
            _loading = false;
        }

        LoadTaskList();
    }

    /// <summary>新建“默认配置”：内置任务按默认顺序全部启用（与 WPF 新建配置的任务列表相同）</summary>
    private static OneDragonFlowConfig CreateDefaultConfig()
    {
        var config = new OneDragonFlowConfig { Name = "默认配置" };
        foreach (var name in OneDragonTaskItem.BuiltInTaskNames)
        {
            var item = new OneDragonTaskItem(name);
            config.TaskEnabledList[item.Id] = true;
            config.TaskDefinitions[item.Id] = name;
            config.TaskOrder.Add(item.Id);
        }

        return config;
    }

    partial void OnSelectedConfigChanged(OneDragonFlowConfig? value)
    {
        if (value is null || _loading)
        {
            return;
        }

        Config.SelectedOneDragonFlowConfigName = value.Name;
        LoadTaskList();
    }

    private OneDragonFlowConfig? _watched;
    private bool _saving;

    /// <summary>右栏详细设置：自动秘境（每日/培养计划/每周），读写同一份 OneDragonFlowConfig（Core OneDragonDomainSettings）</summary>
    [ObservableProperty] private BetterGenshinImpact.ViewModel.Pages.OneDragon.OneDragonDomainSettings? _domainSettings;

    /// <summary>与 WPF 相同：当前配置的任一属性变化即保存</summary>
    private void WatchSelectedConfig()
    {
        if (ReferenceEquals(_watched, SelectedConfig)) return;
        if (_watched is not null) _watched.PropertyChanged -= OnSelectedConfigPropertyChanged;
        DomainSettings?.Detach();
        _watched = SelectedConfig;
        DomainSettings = SelectedConfig is null ? null : new BetterGenshinImpact.ViewModel.Pages.OneDragon.OneDragonDomainSettings(SelectedConfig, Config.AutoDomainConfig);
        if (_watched is not null) _watched.PropertyChanged += OnSelectedConfigPropertyChanged;
        LeyLineRows = SelectedConfig is null ? [] : BetterGenshinImpact.ViewModel.Pages.OneDragon.OneDragonLeyLineRow.CreateWeek(SelectedConfig);
        LoadSereniteaPot();
    }

    /// <summary>地脉花按星期设置（周一至周日）</summary>
    [ObservableProperty] private IReadOnlyList<BetterGenshinImpact.ViewModel.Pages.OneDragon.OneDragonLeyLineRow> _leyLineRows = [];

    /// <summary>尘歌壶购买日期（编辑中，点“应用”后写入 SecretTreasureObjects）</summary>
    [ObservableProperty] private string _sereniteaPotSchedule = "每天重复";

    /// <summary>尘歌壶可购买商品</summary>
    public ObservableCollection<BetterGenshinImpact.ViewModel.Pages.OneDragon.SereniteaPotItemOption> SereniteaPotItems { get; } = [];

    /// <summary>当前保存的购买设置摘要</summary>
    public string SereniteaPotSummary => SelectedConfig is { SecretTreasureObjects.Count: > 0 } c ? string.Join("、", c.SecretTreasureObjects) : "未设置（每天重复，不购买商品）";

    private void LoadSereniteaPot()
    {
        SereniteaPotItems.Clear();
        var objects = SelectedConfig?.SecretTreasureObjects ?? [];
        SereniteaPotSchedule = objects.FirstOrDefault() ?? "每天重复";
        foreach (var item in BetterGenshinImpact.ViewModel.Pages.OneDragon.SereniteaPotPurchase.Items)
            SereniteaPotItems.Add(new(item, BetterGenshinImpact.ViewModel.Pages.OneDragon.SereniteaPotPurchase.IsSelected(objects, item)));
        OnPropertyChanged(nameof(SereniteaPotSummary));
    }

    /// <summary>应用尘歌壶购买设置（与 WPF 弹窗“确认”相同：日期 + 选中商品）</summary>
    [RelayCommand]
    private void ApplySereniteaPot()
    {
        if (SelectedConfig is null) return;
        SelectedConfig.SecretTreasureObjects = BetterGenshinImpact.ViewModel.Pages.OneDragon.SereniteaPotPurchase.Build(SereniteaPotSchedule,
            SereniteaPotItems.Where(i => i.IsSelected).Select(i => i.Name));
        OnPropertyChanged(nameof(SereniteaPotSummary));
    }

    /// <summary>清空首领讨伐累计成功领奖次数（与 WPF 相同）</summary>
    [RelayCommand]
    private void ResetAutoBossCompletedRunCount()
    {
        if (SelectedConfig is null) return;
        SelectedConfig.AutoBossCompletedRunCount = 0;
        _log.Information("首领讨伐累计成功领奖次数已清空");
    }

    private void OnSelectedConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_saving || _loading || !ReferenceEquals(sender, SelectedConfig)) return;
        _saving = true;
        try { SaveConfig(); }
        finally { _saving = false; }
    }

    private void LoadTaskList()
    {
        WatchSelectedConfig();
        foreach (var item in TaskList)
        {
            item.PropertyChanged -= OnTaskItemChanged;
        }

        TaskList.Clear();
        if (SelectedConfig is null)
        {
            return;
        }

        Config.SelectedOneDragonFlowConfigName = SelectedConfig.Name;
        foreach (var item in OneDragonTaskItem.CreateListFromConfig(SelectedConfig))
        {
            item.PropertyChanged += OnTaskItemChanged;
            TaskList.Add(item);
        }

        OnPropertyChanged(nameof(NextTaskText));
    }

    private void OnTaskItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is OneDragonTaskItem item && e.PropertyName == nameof(OneDragonTaskItem.IsEnabled) && SelectedConfig is { } config)
        {
            config.TaskEnabledList[item.Id] = item.IsEnabled;
            SaveConfig();
        }
    }

    [RelayCommand]
    private void SetTaskAsNext(OneDragonTaskItem? item)
    {
        if (item is null || SelectedConfig is null)
        {
            return;
        }

        SelectedConfig.NextTaskId = item.IsNextTask ? string.Empty : item.Id;
        foreach (var t in TaskList)
        {
            t.IsNextTask = t.Id == SelectedConfig.NextTaskId;
        }

        SaveConfig();
        OnPropertyChanged(nameof(NextTaskText));
    }

    [RelayCommand]
    private void SaveConfig()
    {
        if (SelectedConfig is null)
        {
            return;
        }

        try
        {
            SelectedConfig.TaskOrder = TaskList.Select(t => t.Id).ToList();
            OneDragonFlowConfigStore.Write(SelectedConfig);
        }
        catch (Exception e)
        {
            _log.Warning(e, "保存一条龙配置失败");
            UiHost.Dialogs.Toast(DialogKind.Error, $"保存一条龙配置失败：{e.Message}");
        }
    }

    // ---- 任务列表：新增 / 删除 / 复制 / 排序 / 清除标记（对照 WPF OneDragonFlowViewModel） ----

    /// <summary>可新增的任务名：内置任务 + User/ScriptGroup 下的配置组（按 Index 排序，与 WPF ReadScriptGroup 相同）</summary>
    public List<string> ReadAddableGroupNames()
    {
        var groups = OneDragonTaskItem.BuiltInTaskNames.Select(n => (Name: n, Index: 0)).ToList();
        var dir = Global.Absolute(@"User\ScriptGroup");
        try
        {
            Directory.CreateDirectory(dir);
            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    var g = BetterGenshinImpact.Core.Script.Group.ScriptGroup.FromJson(File.ReadAllText(file));
                    groups.Add((g.Name, g.Index));
                }
                catch (Exception e)
                {
                    _log.Information(e, "读取配置组配置时失败");
                }
            }
        }
        catch (Exception e)
        {
            _log.Information(e, "读取配置组配置时失败");
        }

        return groups.OrderBy(g => g.Index).Select(g => g.Name).ToList();
    }

    /// <summary>把选中的配置组加入任务列表（与 WPF ProcessSelectedGroups 相同的插入位置与提示）</summary>
    public void AddTaskGroups(IReadOnlyList<string> names)
    {
        if (SelectedConfig is null || names.Count == 0) return;
        bool lastBuiltIn = false;
        foreach (var name in names)
        {
            var item = new OneDragonTaskItem(name) { IsEnabled = true };
            item.Id = OneDragonTaskListEditing.NewTaskId(TaskList.Select(t => t.Id));
            item.PropertyChanged += OnTaskItemChanged;
            var index = OneDragonTaskListEditing.InsertIndex(TaskList.Select(t => t.Name).ToList(), name, OneDragonTaskItem.BuiltInTaskNames);
            lastBuiltIn = index >= 0;
            if (index >= 0) TaskList.Insert(index, item);
            else TaskList.Add(item);
        }

        SaveTaskList();
        UiHost.Dialogs.Toast(DialogKind.Success, names.Count == 1 ? OneDragonTaskListEditing.AddedText(lastBuiltIn) : OneDragonTaskListEditing.AddedManyText(names.Count));
    }

    /// <summary>新增任务：FAContentDialog 多选配置组</summary>
    [RelayCommand]
    private async Task AddTask()
    {
        var owner = Services.AvaloniaPickers.MainWindow;
        if (owner is null || SelectedConfig is null) return;
        var boxes = ReadAddableGroupNames().Select(n => new global::Avalonia.Controls.CheckBox { Content = n, Tag = n, Margin = new global::Avalonia.Thickness(0, 2) }).ToList();
        var list = new global::Avalonia.Controls.StackPanel { Name = "AddTaskGroupList" };
        foreach (var b in boxes) list.Children.Add(b);
        var dialog = new FluentAvalonia.UI.Controls.FAContentDialog
        {
            Title = OneDragonTaskListEditing.AddTitle,
            Content = new global::Avalonia.Controls.ScrollViewer { MaxHeight = 320, MinWidth = 300, Content = list },
            PrimaryButtonText = "确认", CloseButtonText = "取消", DefaultButton = FluentAvalonia.UI.Controls.FAContentDialogButton.Primary,
        };
        if (await Services.FluentMessageBox.ShowCustomAsync(dialog, owner) != FluentAvalonia.UI.Controls.FAContentDialogResult.Primary) return;
        var selected = boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Tag!).ToList();
        if (selected.Count > 0)
        {
            AddTaskGroups(selected);
            SelectedTask = null;
        }
    }

    /// <summary>删除任务（FAContentDialog 确认）</summary>
    [RelayCommand]
    private async Task DeleteTask(OneDragonTaskItem? item)
    {
        if (item is null) return;
        var owner = Services.AvaloniaPickers.MainWindow;
        if (owner is null) return;
        if (await Services.FluentMessageBox.ShowAsync(owner, $"确定要删除任务「{item.Name}」吗？", "删除任务", DialogKind.Question, DialogButtons.YesNo) != DialogResult.Yes) return;
        item.PropertyChanged -= OnTaskItemChanged;
        TaskList.Remove(item);
        SaveTaskList();
        UiHost.Dialogs.Toast(DialogKind.Success, $"已删除任务: {item.Name}");
    }

    /// <summary>复制任务（插在原任务之后，与 WPF CopyTask 相同）</summary>
    [RelayCommand]
    private void CopyTask(OneDragonTaskItem? item)
    {
        if (item is null) return;
        var copy = new OneDragonTaskItem(item.Name) { IsEnabled = item.IsEnabled };
        copy.Id = OneDragonTaskListEditing.NewTaskId(TaskList.Select(t => t.Id));
        copy.PropertyChanged += OnTaskItemChanged;
        var index = TaskList.IndexOf(item);
        if (index >= 0) TaskList.Insert(index + 1, copy);
        else TaskList.Add(copy);
        SaveTaskList();
        UiHost.Dialogs.Toast(DialogKind.Success, $"已复制任务: {item.Name}");
    }

    /// <summary>上移（WPF 为拖动排序）</summary>
    [RelayCommand]
    private void MoveTaskUp(OneDragonTaskItem? item) => MoveTask(item, -1);

    /// <summary>下移（WPF 为拖动排序）</summary>
    [RelayCommand]
    private void MoveTaskDown(OneDragonTaskItem? item) => MoveTask(item, 1);

    private void MoveTask(OneDragonTaskItem? item, int delta)
    {
        if (item is null) return;
        var index = TaskList.IndexOf(item);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= TaskList.Count) return;
        TaskList.Move(index, target);
        SaveTaskList();
    }

    /// <summary>清除“从此执行”标记</summary>
    [RelayCommand]
    private void ClearNextTask()
    {
        if (SelectedConfig is null) return;
        SelectedConfig.NextTaskId = string.Empty;
        foreach (var t in TaskList) t.IsNextTask = false;
        SaveConfig();
        OnPropertyChanged(nameof(NextTaskText));
    }

    /// <summary>与 WPF SaveConfig 相同：按任务列表重建定义、启用状态与顺序后保存</summary>
    private void SaveTaskList()
    {
        if (SelectedConfig is null) return;
        _saving = true;
        try
        {
            SelectedConfig.TaskDefinitions.Clear();
            SelectedConfig.TaskEnabledList.Clear();
            foreach (var t in TaskList)
            {
                SelectedConfig.TaskDefinitions[t.Id] = t.Name;
                SelectedConfig.TaskEnabledList[t.Id] = t.IsEnabled;
            }

            SaveConfig();
        }
        finally
        {
            _saving = false;
        }

        OnPropertyChanged(nameof(NextTaskText));
    }

    // ---- 配置：新增 / 重命名 / 删除（对照 WPF，确认用 FAContentDialog） ----

    [RelayCommand]
    private async Task AddConfig()
    {
        var name = await Services.FluentMessageBox.PromptAsync(OneDragonConfigEditing.AddPrompt, OneDragonConfigEditing.AddTitle, "");
        if (string.IsNullOrEmpty(name)) return;
        if (OneDragonConfigEditing.AddConflict(name, ConfigList.Select(c => c.Name)) is { } conflict)
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, conflict);
            return;
        }

        // 与 WPF 相同：新配置沿用当前任务列表（WPF 切换后不重新加载列表，随即按当前列表保存）
        var config = new OneDragonFlowConfig { Name = name };
        foreach (var t in TaskList)
        {
            config.TaskDefinitions[t.Id] = t.Name;
            config.TaskEnabledList[t.Id] = t.IsEnabled;
            config.TaskOrder.Add(t.Id);
        }

        ConfigList.Insert(0, config);
        SelectedConfig = config;
        SaveConfig();
    }

    [RelayCommand]
    private async Task RenameConfig()
    {
        if (SelectedConfig is not { } config)
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, OneDragonConfigEditing.RenameNoSelection);
            return;
        }

        var name = await Services.FluentMessageBox.PromptAsync(OneDragonConfigEditing.RenamePrompt, OneDragonConfigEditing.RenameTitle, config.Name);
        if (string.IsNullOrEmpty(name) || name == config.Name) return;
        if (OneDragonConfigEditing.RenameConflict(name, ConfigList.Select(c => c.Name)) is { } conflict)
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, conflict);
            return;
        }

        _saving = true;
        try
        {
            OneDragonConfigEditing.Rename(config, name);
            Config.SelectedOneDragonFlowConfigName = name;
            // ComboBox 显示名刷新
            var index = ConfigList.IndexOf(config);
            _loading = true;
            ConfigList[index] = config;
            SelectedConfig = config;
            _loading = false;
            UiHost.Dialogs.Toast(DialogKind.Success, OneDragonConfigEditing.RenamedText);
        }
        catch (Exception e)
        {
            _log.Error(e, OneDragonConfigEditing.RenameFailedText);
            UiHost.Dialogs.Toast(DialogKind.Error, OneDragonConfigEditing.RenameFailedText);
        }
        finally
        {
            _loading = false;
            _saving = false;
        }
    }

    [RelayCommand]
    private async Task DeleteConfig()
    {
        if (SelectedConfig is not { } config)
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, OneDragonConfigEditing.DeleteNoSelection);
            return;
        }

        var owner = Services.AvaloniaPickers.MainWindow;
        if (owner is null) return;
        if (await Services.FluentMessageBox.ShowAsync(owner, OneDragonConfigEditing.DeleteConfirmText(config.Name), OneDragonConfigEditing.DeleteTitle,
                DialogKind.Question, DialogButtons.YesNo) != DialogResult.Yes) return;
        try
        {
            OneDragonConfigEditing.DeleteFile(config.Name);
            ConfigList.Remove(config);
            if (ConfigList.Count == 0)
            {
                var fresh = new OneDragonFlowConfig { Name = OneDragonConfigEditing.DefaultName };
                ConfigList.Add(fresh);
                SelectedConfig = fresh;
            }
            else
            {
                SelectedConfig = ConfigList[0];
            }

            SelectedTask = null;
            SaveConfig();
            UiHost.Dialogs.Toast(DialogKind.Success, OneDragonConfigEditing.DeletedText);
        }
        catch (Exception e)
        {
            _log.Error(e, OneDragonConfigEditing.DeleteFailedText);
            UiHost.Dialogs.Toast(DialogKind.Error, OneDragonConfigEditing.DeleteFailedText);
        }
    }

    [RelayCommand]
    private Task Toggle() => Tasks.ToggleAsync(OneDragonTask.TaskId);

    /// <summary>
    /// 生成一条龙参数（与 WPF 的 OneDragonFlowViewModel.CreateOneDragonParam 相同：先保存配置，读取并清除“从此执行”标记）
    /// </summary>
    public object? CreateOneDragonParam()
    {
        if (SelectedConfig is null)
        {
            InitConfigList();
        }

        if (SelectedConfig is null || !TaskList.Any(t => t.IsEnabled))
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, "请先选择任务");
            return null;
        }

        SaveConfig();
        var startTaskId = SelectedConfig.NextTaskId;
        if (!string.IsNullOrEmpty(startTaskId))
        {
            SelectedConfig.NextTaskId = string.Empty;
            foreach (var t in TaskList)
            {
                t.IsNextTask = false;
            }

            SaveConfig();
            OnPropertyChanged(nameof(NextTaskText));
        }

        return new OneDragonParam { ConfigName = SelectedConfig.Name, StartTaskId = string.IsNullOrEmpty(startTaskId) ? null : startTaskId };
    }
}

/// <summary>一条龙页的绑定转换</summary>
public static class OneDragonPageConverters
{
    /// <summary>有独立任务运行时按钮显示“停止”</summary>
    public static readonly global::Avalonia.Data.Converters.IValueConverter RunButtonText =
        new global::Avalonia.Data.Converters.FuncValueConverter<bool, string>(active => active ? "停止" : "启动一条龙");
}
