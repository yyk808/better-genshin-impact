using System.ComponentModel;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.Common.Reward;
using BetterGenshinImpact.ViewModel.Pages.View;
using BetterGenshinImpact.ViewModel.Resin;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// 独立任务页“首领讨伐”卡片的首领选择、树脂使用与按材料数量提前结束（对照 WPF TaskSettingsPage 的首领讨伐区域，直接读写 config.json 的 autoBossConfig）。
/// </summary>
public sealed partial class SoloBossSettingsViewModel : ObservableObject
{
    public SoloBossSettingsViewModel(AllConfig config, IResinSettingsDialogs? dialogs)
    {
        Config = config.AutoBossConfig;
        // 树脂使用整体替换配置中的 ResinUsage（与 WPF BossResin 相同的绑定方式）
        BossResin = ResinUsageConfigBinding.Create("自动首领讨伐", AutoBossTask.ResinCapability, Config,
            Config.EnsureResinUsage, value => Config.ResinUsage = value, dialogs);
        // 材料目标直接读写配置集合，候选物品随首领刷新（候选清单在 Core，与 WPF 共用）
        TargetItems = new TargetItemListEditorViewModel(Config.TargetItems);
        RefreshTargetItemChoices();
        TargetItems.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TargetItemListEditorViewModel.IsTargetModeEnabled)) OnPropertyChanged(nameof(IsRewardRecognitionEditable));
        };
        Config.PropertyChanged += OnConfigChanged;
    }

    public AutoBossConfig Config { get; }

    public ResinUsageEntryViewModel BossResin { get; }

    /// <summary>按材料数量提前结束的材料目标列表</summary>
    public TargetItemListEditorViewModel TargetItems { get; }

    /// <summary>可讨伐的首领（与任务计划首领编辑器相同的数据）</summary>
    public IReadOnlyList<string> BossNames => DedicatedTaskSettingsChoices.BossNames;

    /// <summary>首领下拉框的选中项；忽略列表中没有当前值时选择框写回的 null</summary>
    public string? BossNameChoice
    {
        get => string.IsNullOrEmpty(Config.BossName) ? null : Config.BossName;
        set
        {
            if (value is not null) Config.BossName = value;
        }
    }

    /// <summary>与 WPF 相同：按材料数量提前结束时奖励识别必须开启，开关不可改</summary>
    public bool IsRewardRecognitionEditable => !TargetItems.IsTargetModeEnabled;

    private void RefreshTargetItemChoices() => TargetItems.ApplyChoices(TargetItemCatalog.Instance.GetBossTargetChoices(Config.BossName));

    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AutoBossConfig.BossName))
        {
            RefreshTargetItemChoices();
            OnPropertyChanged(nameof(BossNameChoice));
        }
    }
}
