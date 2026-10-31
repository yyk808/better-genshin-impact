using System.Collections.ObjectModel;
using System.ComponentModel;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.OneDragon;
using BetterGenshinImpact.Host.Common;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using ILogger = Serilog.ILogger;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// “一条龙”页（对应 WPF 的 OneDragonFlowPage 的主体）：选择 User/OneDragon 下的一条龙配置，开关任务、“从此执行”，
/// 选择完成后操作，启动/停止。执行走 Core 的 OneDragonTask（经 SoloTaskExecutionService），
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
