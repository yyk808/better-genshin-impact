using System.Collections.ObjectModel;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFight.Strategy;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation;
using BetterGenshinImpact.GameTask.AutoMusicGame;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;
using BetterGenshinImpact.GameTask.AutoWood;
using BetterGenshinImpact.GameTask.GetGridIcons;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.GameTask.SoloTask.Execution;
using BetterGenshinImpact.GameTask.UseRedeemCode;
using BetterGenshinImpact.Host.Common;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using ILogger = Serilog.ILogger;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// “独立任务”页（对应 WPF 的 TaskSettingsPage）：按 Core 的 SoloTaskRegistry 列出独立任务卡片，
/// 参数由 Core 的 SoloTaskParamFactory 按配置与页面输入生成（与 WPF 同一份逻辑），
/// 启动/停止走 Core 的 SoloTaskExecutionService → TaskRunner（任务信号量、触发器暂停、取消后释放所有按键）
/// </summary>
public sealed partial class TaskSettingsPageViewModel : PageViewModel, ISoloTaskParamInputs
{
    private readonly GameHost? _host;
    private readonly ILogger _log;
    private readonly SoloTaskRegistry? _registry;
    private readonly SoloTaskExecutionService? _executor;
    private readonly Dictionary<string, SoloTaskCardViewModel> _cards = new();

    [ObservableProperty] private int _autoWoodRoundNum;
    [ObservableProperty] private int _autoWoodDailyMaxCount = 2000;
    [ObservableProperty] private int _autoDomainRoundNum;
    [ObservableProperty] private bool _saveScreenshotOnKeyTick;
    [ObservableProperty] private InventoryCountComparisonTarget _inventoryCountComparisonTarget = InventoryCountComparisonTarget.CharacterDevelopmentItems;
    [ObservableProperty] private string _redeemCodesText = string.Empty;
    [ObservableProperty] private string _statusText = "空闲";
    [ObservableProperty] private bool _isTaskActive;

    public TaskSettingsPageViewModel(GameHost? host, ILogger log)
    {
        _host = host;
        _log = log;
        Config = host?.Config ?? new AllConfig();
        IsAvailable = host is not null;
        if (host is not null)
        {
            _registry = host.Services.GetRequiredService<SoloTaskRegistry>();
            _executor = host.Services.GetRequiredService<SoloTaskExecutionService>();
            _executor.StatusChanged += (_, e) => Dispatcher.UIThread.Post(() => ApplyStatus(e.Status));
            _executor.RunCompleted += (_, e) => Dispatcher.UIThread.Post(() => OnRunCompleted(e));
        }

        // 与 WPF 独立任务页相同的卡片顺序与说明；页面上没有的独立任务（热键、一条龙中使用）按注册表分组列在后面
        var descriptors = _registry?.GetByScope(SoloTaskScopes.Standalone).OrderBy(d => d.Order).ToList() ?? [];
        var pageCards = PageTasks
            .Select(t => descriptors.FirstOrDefault(d => d.TaskId == t.TaskId) is { } d ? new SoloTaskCardViewModel(this, d, t.Title, t.Description) : null)
            .OfType<SoloTaskCardViewModel>()
            .ToList();
        if (pageCards.Count > 0)
        {
            Groups.Add(new SoloTaskGroupViewModel("", pageCards));
        }

        foreach (var group in descriptors.Where(d => PageTasks.All(t => t.TaskId != d.TaskId)).GroupBy(d => d.Group))
        {
            Groups.Add(new SoloTaskGroupViewModel($"{group.Key}（WPF 版在热键或一条龙中使用）",
                group.Select(d => new SoloTaskCardViewModel(this, d, d.DisplayName, d.Description)).ToList()));
        }

        foreach (var card in Groups.SelectMany(g => g.Tasks))
        {
            _cards[card.TaskId] = card;
        }

        FightStrategies = LoadStrategyNames(@"User\AutoFight", [CombatStrategyResolver.AutoSelectName]);
        TcgStrategies = LoadStrategyNames(@"User\AutoGeniusInvokation", []);
    }

    public override string Title => "独立任务";

    /// <summary>WPF 独立任务页（TaskSettingsPage.xaml）上的卡片：顺序、标题与说明</summary>
    private static readonly (string TaskId, string Title, string Description)[] PageTasks =
    [
        ("auto_genius_invokation", "自动七圣召唤", "全自动打牌"),
        ("auto_wood", "自动伐木", "装备「王树瑞佑」，通过循环重启游戏刷新并收集木材"),
        ("auto_fight", "自动战斗", "自动执行选择的战斗策略"),
        ("auto_domain", "自动秘境", "基于钟离的自动循环刷本"),
        ("auto_boss", "自动首领讨伐", "自动传送、战斗并领取奖励"),
        ("auto_stygian_onslaught", "自动幽境危战", "自动传送并进入幽境危战"),
        ("auto_fishing", "全自动钓鱼（单个鱼塘）", "不要携带跟宠！在出现钓鱼F按钮的位置启动本任务"),
        ("auto_ley_line_outcrop", "自动地脉花", "自动定位并刷取地脉花"),
        ("auto_music_game", "【乐曲】 演奏单个乐曲", "进入演奏界面使用，下落模式必须选择垂落模式"),
        ("auto_album", "【专辑】 全自动完成整个专辑", "进入专辑界面使用，自动演奏未完成乐曲"),
        ("auto_cook", "自动烹饪", "在手动烹饪界面运行，自动识别并点击结束烹饪"),
        ("auto_combo_build", "自动连招（实验）", ""),
        ("auto_artifact_salvage", "自动分解圣遗物", "指定匹配表达式逐一筛选分解，支持5星圣遗物"),
        ("redeem_code", "自动使用兑换码", "自动使用输入的兑换码"),
        ("get_grid_icons", "截取物品图标（开发者）", "须要打开设置-启用保存截图功能"),
        ("grid_icons_accuracy_test", "测试识别效果", "请先将游戏界面切换至待测试分类界面"),
        ("inventory_count_comparison", "数量 OCR 测试", "将数量OCR结果截图保存至本地"),
    ];

    public AllConfig Config { get; }

    /// <summary>平台初始化失败时为 false（只显示说明）</summary>
    public bool IsAvailable { get; }

    public ObservableCollection<SoloTaskGroupViewModel> Groups { get; } = [];

    public List<string> FightStrategies { get; }

    public List<string> TcgStrategies { get; }

    public InventoryCountComparisonTarget[] InventoryTargets { get; } = Enum.GetValues<InventoryCountComparisonTarget>();

    /// <summary>启动或停止独立任务；与 WPF 的单实例行为相同：空闲则启动，运行中（同一任务）则停止</summary>
    public async Task ToggleAsync(string taskId)
    {
        if (_executor is null || _registry is null)
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, "平台服务未初始化，无法运行独立任务");
            return;
        }

        var status = _executor.GetStatus();
        if (status.IsActive)
        {
            if (status.TaskId != taskId)
            {
                UiHost.Dialogs.Toast(DialogKind.Warning, $"当前正在运行“{_registry.GetDisplayName(status.TaskId)}”，请先停止");
                return;
            }

            await StopAsync();
            return;
        }

        var definition = CreateDefinition(taskId);
        if (definition is null)
        {
            return;
        }

        var request = definition.CreateRequest();
        var response = await _executor.StartAsync(request);
        if (!response.Accepted)
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, response.ErrorMessage ?? "启动失败");
            return;
        }

        ApplyStatus(response.Status);
    }

    [RelayCommand]
    private Task Stop() => StopAsync();

    /// <summary>停止当前运行的独立任务（取消令牌 → 任务退出 → TaskRunner 释放所有按键）</summary>
    public async Task StopAsync()
    {
        var status = _executor?.GetStatus();
        if (_executor is null || status is not { IsActive: true, RunId: { } runId })
        {
            return;
        }

        var response = await _executor.StopAsync(new SoloTaskStopRequest { RunId = runId });
        ApplyStatus(response.Status);
    }

    /// <summary>按页面配置生成启动定义（参数序列化为 JSON，与 WPF 的 SoloTaskButtonController.CreateDefinition 相同）</summary>
    public SoloTaskStartDefinition? CreateDefinition(string taskId)
    {
        if (_registry is null)
        {
            return null;
        }

        object? param;
        try
        {
            param = SoloTaskParamFactory.Create(taskId, Config, _registry, this);
        }
        catch (Exception e)
        {
            _log.Warning(e, "生成独立任务 {TaskId} 的参数失败", taskId);
            UiHost.Dialogs.Toast(DialogKind.Error, $"参数无效：{e.Message}");
            return null;
        }

        if (param is null)
        {
            return null;
        }

        // 执行层从配置服务读取配置：先把界面上的修改写入文件
        _host?.Services.GetService<IConfigService>()?.Flush();
        return new SoloTaskStartDefinition
        {
            TaskId = taskId,
            Parameters = JObject.Parse(_registry.Get(taskId).SerializeParam(param))
        };
    }

    private void ApplyStatus(SoloTaskStatusSnapshot status)
    {
        IsTaskActive = status.IsActive;
        StatusText = status.State switch
        {
            SoloTaskRunState.Starting => $"正在启动：{_registry?.GetDisplayName(status.TaskId)}",
            SoloTaskRunState.Running => $"运行中：{_registry?.GetDisplayName(status.TaskId)}",
            SoloTaskRunState.Stopping => $"正在停止：{_registry?.GetDisplayName(status.TaskId)}",
            _ => status.ErrorMessage is { } err ? $"空闲（上次失败：{err}）" : "空闲"
        };
        foreach (var card in _cards.Values)
        {
            card.Update(status);
        }
    }

    private void OnRunCompleted(SoloTaskRunCompletedEventArgs e)
    {
        var name = _registry?.GetDisplayName(e.TaskId);
        var message = e.Result.ErrorMessage ?? e.Result.TaskResult?.Message;
        _log.Information("独立任务“{Name}”结束：{Outcome}{Message}", name, e.Result.Outcome,
            string.IsNullOrEmpty(message) ? "" : $"（{message}）");
        ApplyStatus(_executor?.GetStatus() ?? SoloTaskStatusSnapshot.Idle());
    }

    private static List<string> LoadStrategyNames(string relativeDir, List<string> fixedNames)
    {
        var dir = Global.Absolute(relativeDir);
        var names = new List<string>(fixedNames);
        if (Directory.Exists(dir))
        {
            names.AddRange(Directory.EnumerateFiles(dir, "*.txt", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(dir, f)[..^4])
                .Order(StringComparer.Ordinal));
        }

        return names;
    }

    // ISoloTaskParamInputs：策略检查失败用轻提示（与 WPF 相同的文案）
    public bool GetTcgStrategy(out string content) =>
        TaskStrategyLookup.GetTcgStrategy(Config.AutoGeniusInvokationConfig.StrategyName, Notify, out content);

    public bool GetFightStrategy(out string path) => GetFightStrategy(Config.AutoFightConfig.StrategyName, out path);

    public bool GetFightStrategy(string strategyName, out string path) =>
        TaskStrategyLookup.GetFightStrategy(strategyName, Notify, out path);

    /// <summary>兑换码在卡片上的多行文本框中输入（WPF 为弹出输入框）</summary>
    public List<string>? PromptForRedeemCodes()
    {
        var codes = SoloTaskParamFactory.ParseRedeemCodes(RedeemCodesText);
        if (codes.Count == 0)
        {
            Notify(DialogKind.Warning, "没有有效的兑换码");
            return null;
        }

        return codes;
    }

    private static void Notify(DialogKind kind, string message) => UiHost.Dispatcher.Invoke(() => UiHost.Dialogs.Toast(kind, message));
}

/// <summary>一组独立任务（对应 WPF 页面上的分组标题）</summary>
public sealed class SoloTaskGroupViewModel(string name, List<SoloTaskCardViewModel> tasks)
{
    public string Name => name;

    public bool HasName => !string.IsNullOrEmpty(name);

    public List<SoloTaskCardViewModel> Tasks => tasks;
}

/// <summary>一个独立任务卡片：标题、说明、启动/停止按钮和该任务的关键参数</summary>
public sealed partial class SoloTaskCardViewModel : ObservableObject
{
    private readonly TaskSettingsPageViewModel _page;
    private readonly SoloTaskDescriptor _descriptor;

    [ObservableProperty] private string _buttonText = "启动";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _canInteract = true;

    public SoloTaskCardViewModel(TaskSettingsPageViewModel page, SoloTaskDescriptor descriptor, string title, string description)
    {
        _page = page;
        _descriptor = descriptor;
        DisplayName = title;
        Description = description;
    }

    public TaskSettingsPageViewModel Page => _page;

    public string TaskId => _descriptor.TaskId;

    public string DisplayName { get; }

    public string Description { get; }

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    // 各任务的参数区（其余配置项尚未迁移，使用配置文件中的值）
    public bool IsWood => TaskId == AutoWoodTask.TaskId;
    public bool IsFight => TaskId == AutoFightEntryTask.TaskId;
    public bool IsDomain => TaskId == AutoDomainTask.TaskId;
    public bool IsBoss => TaskId == AutoBossTask.TaskId;
    public bool IsStygian => TaskId == AutoStygianOnslaughtTask.TaskId;
    public bool IsFishing => TaskId == AutoFishingTask.TaskId;
    public bool IsTcg => TaskId == AutoGeniusInvokationTask.TaskId;
    public bool IsMusic => TaskId == AutoMusicGameTask.TaskId;
    public bool IsRedeem => TaskId == UseRedemptionCodeTask.TaskId;
    public bool IsInventory => _descriptor.ParamType == typeof(InventoryCountComparisonParam);
    public bool IsSalvage => TaskId == AutoArtifactSalvageTask.TaskId;

    [RelayCommand]
    private Task Toggle() => _page.ToggleAsync(TaskId);

    public void Update(SoloTaskStatusSnapshot status)
    {
        var mine = status.IsActive && status.TaskId == TaskId;
        IsRunning = mine;
        CanInteract = !status.IsActive || (mine && status.State != SoloTaskRunState.Stopping);
        ButtonText = !mine ? "启动" : status.State switch
        {
            SoloTaskRunState.Starting => "启动中…",
            SoloTaskRunState.Stopping => "停止中…",
            _ => "停止"
        };
    }
}
