using System;
using System.Collections.Generic;
using System.ComponentModel;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.ViewModel.Pages.View;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.OneDragon;

/// <summary>
/// 一条龙配置的“自动秘境”设置（对照 WPF OneDragonFlowPage 的每日/培养计划/每周三张卡片），直接读写 OneDragonFlowConfig 与 AutoDomainConfig，
/// 存储格式与 WPF 绑定完全相同：跟随培养计划时秘境名为 <see cref="AutoDomainTask.TrainingGuideOption"/>，每周某天取消勾选培养计划时写回空秘境名（与 WPF TrainingPlanCheckConverter 相同）。
/// </summary>
public sealed class OneDragonDomainSettings : ObservableObject
{
    /// <summary>周日/限时奖励序号的选项（与 WPF 一条龙页的 SundayEverySelectedValueList / SundaySelectedValueList 相同）。</summary>
    public static IReadOnlyList<string> RewardIndexChoices { get; } = ["", "1", "2", "3"];

    /// <summary>刷取偏好（与 WPF 的两个 ComboBoxItem 相同，按序号保存）。</summary>
    public static IReadOnlyList<string> RunPreferenceChoices { get; } = ["适量刷取，依靠合成天赋收益补足材料", "充分刷取，直到满足培养计划需求"];

    public OneDragonDomainSettings(OneDragonFlowConfig config, AutoDomainConfig autoDomain)
    {
        Config = config;
        AutoDomain = autoDomain;
        DailySource = new DomainSourceViewModel(() => Config.DomainName, v => Config.DomainName = v);
        Weekly =
        [
            new("默认", "留空则不切换队伍", () => Config.PartyName, v => Config.PartyName = v, () => Config.DomainName, v => Config.DomainName = v,
                () => Config.SundayWeeklySelectedValue, v => Config.SundayWeeklySelectedValue = v),
            Day("周一", () => Config.MondayPartyName, v => Config.MondayPartyName = v, () => Config.MondayDomainName, v => Config.MondayDomainName = v, () => Config.MondaySelectedValue, v => Config.MondaySelectedValue = v),
            Day("周二", () => Config.TuesdayPartyName, v => Config.TuesdayPartyName = v, () => Config.TuesdayDomainName, v => Config.TuesdayDomainName = v, () => Config.TuesdaySelectedValue, v => Config.TuesdaySelectedValue = v),
            Day("周三", () => Config.WednesdayPartyName, v => Config.WednesdayPartyName = v, () => Config.WednesdayDomainName, v => Config.WednesdayDomainName = v, () => Config.WednesdaySelectedValue, v => Config.WednesdaySelectedValue = v),
            Day("周四", () => Config.ThursdayPartyName, v => Config.ThursdayPartyName = v, () => Config.ThursdayDomainName, v => Config.ThursdayDomainName = v, () => Config.ThursdaySelectedValue, v => Config.ThursdaySelectedValue = v),
            Day("周五", () => Config.FridayPartyName, v => Config.FridayPartyName = v, () => Config.FridayDomainName, v => Config.FridayDomainName = v, () => Config.FridaySelectedValue, v => Config.FridaySelectedValue = v),
            Day("周六", () => Config.SaturdayPartyName, v => Config.SaturdayPartyName = v, () => Config.SaturdayDomainName, v => Config.SaturdayDomainName = v, () => Config.SaturdaySelectedValue, v => Config.SaturdaySelectedValue = v),
            Day("周日", () => Config.SundayPartyName, v => Config.SundayPartyName = v, () => Config.SundayDomainName, v => Config.SundayDomainName = v, () => Config.SundaySelectedValue, v => Config.SundaySelectedValue = v),
        ];
        Config.PropertyChanged += OnConfigChanged;
        AutoDomain.PropertyChanged += OnAutoDomainChanged;
    }

    private static OneDragonWeeklyDomainRow Day(string label, Func<string> party, Action<string> setParty, Func<string> domain, Action<string> setDomain, Func<string> value, Action<string> setValue) =>
        new(label, "留空使用默认队伍", party, setParty, domain, setDomain, value, setValue);

    /// <summary>一条龙配置。</summary>
    public OneDragonFlowConfig Config { get; }

    /// <summary>全局自动秘境配置（培养计划刷取设置与独立任务页共用）。</summary>
    public AutoDomainConfig AutoDomain { get; }

    /// <summary>每日刷取：与“每周刷取”互斥（WPF 用反向绑定 WeeklyDomainEnabled）。</summary>
    public bool DailyEnabled
    {
        get => !Config.WeeklyDomainEnabled;
        set => Config.WeeklyDomainEnabled = !value;
    }

    /// <summary>每周刷取开关。</summary>
    public bool WeeklyEnabled
    {
        get => Config.WeeklyDomainEnabled;
        set => Config.WeeklyDomainEnabled = value;
    }

    /// <summary>每日秘境来源（跟随培养计划 / 指定秘境）。</summary>
    public DomainSourceViewModel DailySource { get; }

    /// <summary>每日指定秘境的选择项；忽略选择框写回的 null。</summary>
    public string? DailyManualDomain
    {
        get => DailySource.IsTrainingPlan ? null : Config.DomainName;
        set
        {
            if (value is not null) Config.DomainName = value;
        }
    }

    /// <summary>培养计划备选秘境；忽略 null。</summary>
    public string? FallbackDomain
    {
        get => AutoDomain.TrainingGuideFallbackDomainName;
        set
        {
            if (value is not null) AutoDomain.TrainingGuideFallbackDomainName = value;
        }
    }

    /// <summary>每周表格：默认行 + 周一至周日。</summary>
    public IReadOnlyList<OneDragonWeeklyDomainRow> Weekly { get; }

    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(OneDragonFlowConfig.WeeklyDomainEnabled):
                OnPropertyChanged(nameof(DailyEnabled));
                OnPropertyChanged(nameof(WeeklyEnabled));
                break;
            case nameof(OneDragonFlowConfig.DomainName):
                DailySource.Refresh();
                OnPropertyChanged(nameof(DailyManualDomain));
                break;
        }

        foreach (var row in Weekly) row.Refresh();
    }

    private void OnAutoDomainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AutoDomainConfig.TrainingGuideFallbackDomainName)) OnPropertyChanged(nameof(FallbackDomain));
    }

    /// <summary>切换配置时解除订阅。</summary>
    public void Detach()
    {
        Config.PropertyChanged -= OnConfigChanged;
        AutoDomain.PropertyChanged -= OnAutoDomainChanged;
    }
}

/// <summary>每周秘境表格的一行（默认或某一天）：队伍、是否跟随培养计划、秘境、奖励序号。</summary>
public sealed class OneDragonWeeklyDomainRow(string label, string partyPlaceholder,
    Func<string> party, Action<string> setParty, Func<string> domain, Action<string> setDomain, Func<string> getValue, Action<string> setValue) : ObservableObject
{
    public string Label => label;

    public string PartyPlaceholder => partyPlaceholder;

    public string PartyName
    {
        get => party();
        set => setParty(value ?? string.Empty);
    }

    /// <summary>勾选写入培养计划选项，取消勾选写回空秘境名（WPF TrainingPlanCheckConverter，ClearWhenUnchecked）。</summary>
    public bool IsTrainingPlan
    {
        get => DomainSourceViewModel.IsTrainingPlanValue(domain());
        set
        {
            if (value == IsTrainingPlan) return;
            setDomain(value ? AutoDomainTask.TrainingGuideOption : string.Empty);
        }
    }

    /// <summary>未跟随培养计划时显示的秘境选择；忽略 null。</summary>
    public string? DomainName
    {
        get => IsTrainingPlan ? null : domain();
        set
        {
            if (value is not null) setDomain(value);
        }
    }

    /// <summary>奖励序号（周日或限时）。</summary>
    public string SelectedValue
    {
        get => getValue();
        set => setValue(value ?? string.Empty);
    }

    /// <summary>配置变化后刷新绑定。</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(PartyName));
        OnPropertyChanged(nameof(IsTrainingPlan));
        OnPropertyChanged(nameof(DomainName));
        OnPropertyChanged(nameof(SelectedValue));
    }
}
