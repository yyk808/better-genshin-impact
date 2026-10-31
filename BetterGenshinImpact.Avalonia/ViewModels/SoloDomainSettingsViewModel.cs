using System.ComponentModel;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.Common.Reward;
using BetterGenshinImpact.ViewModel.Pages.View;
using BetterGenshinImpact.ViewModel.Resin;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// 独立任务页“自动秘境”卡片的配置项（对照 WPF TaskSettingsPage 的自动秘境区域，直接读写 config.json 的 autoDomainConfig）。
/// </summary>
public sealed partial class SoloDomainSettingsViewModel : ObservableObject
{
    public SoloDomainSettingsViewModel(AllConfig config, IResinSettingsDialogs? dialogs)
    {
        Config = config.AutoDomainConfig;
        Salvage = config.AutoArtifactSalvageConfig;
        DomainSource = new DomainSourceViewModel(() => Config.DomainName, value => Config.DomainName = value);
        // 树脂使用整体替换配置中的 ResinUsage（与 WPF 相同的绑定方式）
        DomainResin = ResinUsageConfigBinding.Create("自动秘境", AutoDomainTask.ResinCapability, Config,
            Config.EnsureResinUsage, value => Config.ResinUsage = value, dialogs);
        // 按材料数量提前结束：候选物品随秘境刷新（与 WPF 独立任务页相同）
        TargetItems = new TargetItemListEditorViewModel(Config.TargetItems);
        RefreshTargetItemChoices();
        Config.PropertyChanged += OnConfigChanged;
    }

    /// <summary>按材料数量提前结束的材料目标列表</summary>
    public TargetItemListEditorViewModel TargetItems { get; }

    private void RefreshTargetItemChoices()
    {
        TargetItemCatalog.Instance.TryGetDomainItems(Config.DomainName, out var items);
        TargetItems.ApplyChoices(items);
    }

    public AutoDomainConfig Config { get; }

    public AutoArtifactSalvageConfig Salvage { get; }

    /// <summary>秘境来源：跟随培养计划或指定秘境，读写 DomainName</summary>
    public DomainSourceViewModel DomainSource { get; }

    public ResinUsageEntryViewModel DomainResin { get; }

    public IReadOnlyList<string> RunPreferences { get; } = ["适量刷取，依靠合成天赋收益补足材料", "充分刷取，直到满足培养计划需求"];

    public static IReadOnlyList<string> ArtifactStarChoices { get; } = ["4", "3", "2", "1"];

    /// <summary>合成收益预留率只在适量刷取时可编辑</summary>
    public bool IsReservePercentEditable => Config.TrainingGuideRunPreference == 0;

    /// <summary>指定秘境的下拉框选中项；忽略列表中没有当前值时选择框写回的 null</summary>
    public string? ManualDomainChoice
    {
        get => DomainSource.IsTrainingPlan ? null : Config.DomainName;
        set
        {
            if (value is not null) Config.DomainName = value;
        }
    }

    /// <summary>备选秘境的下拉框选中项（首项为空表示不刷）</summary>
    public string? FallbackDomainChoice
    {
        get => Config.TrainingGuideFallbackDomainName;
        set
        {
            if (value is not null) Config.TrainingGuideFallbackDomainName = value;
        }
    }

    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AutoDomainConfig.DomainName):
                DomainSource.Refresh();
                RefreshTargetItemChoices();
                OnPropertyChanged(nameof(ManualDomainChoice));
                break;
            case nameof(AutoDomainConfig.TrainingGuideFallbackDomainName):
                OnPropertyChanged(nameof(FallbackDomainChoice));
                break;
            case nameof(AutoDomainConfig.TrainingGuideRunPreference):
                OnPropertyChanged(nameof(IsReservePercentEditable));
                break;
        }
    }
}
