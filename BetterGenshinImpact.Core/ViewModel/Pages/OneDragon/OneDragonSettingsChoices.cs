using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.OneDragon;

/// <summary>一条龙配置页的选项列表（原 WPF OneDragonFlowViewModel / OneDragonFlowPage 中的常量），WPF 与 Avalonia 共用。</summary>
public static class OneDragonChoices
{
    /// <summary>合成树脂的合成台地区。</summary>
    public static IReadOnlyList<string> CraftingBenchCountries { get; } = ["枫丹", "稻妻", "璃月", "蒙德"];

    /// <summary>领取奖励的冒险者协会地区。</summary>
    public static IReadOnlyList<string> AdventurersGuildCountries { get; } = ["挪德卡莱", "枫丹", "稻妻", "璃月", "蒙德"];

    /// <summary>尘歌壶进壶方式。</summary>
    public static IReadOnlyList<string> SereniteaPotTpTypes { get; } = ["地图传送", "尘歌壶道具"];

    /// <summary>地脉花类型。</summary>
    public static IReadOnlyList<string> LeyLineOutcropTypes { get; } = ["启示之花", "藏金之花"];

    /// <summary>地脉花类型（首项为空：使用独立任务默认设置）。</summary>
    public static IReadOnlyList<string> LeyLineOutcropTypesWithEmpty { get; } = ["", .. LeyLineOutcropTypes];

    /// <summary>地脉花国家（首项为空：使用独立任务默认设置）。</summary>
    public static IReadOnlyList<string> LeyLineOutcropCountriesWithEmpty { get; } = ["", .. BetterGenshinImpact.ViewModel.Pages.Pulonia.PuloniaOneDragonSettingChoices.LeyLineOutcropCountries];
}

/// <summary>
/// 尘歌壶购买设置：SecretTreasureObjects 第一个元素为购买日期（每天重复/星期一…星期日），其后为商品名（原 WPF OneDragonFlowPage 代码后置中的逻辑）。
/// </summary>
public static class SereniteaPotPurchase
{
    /// <summary>购买日期选项。</summary>
    public static IReadOnlyList<string> Schedules { get; } = ["每天重复", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六", "星期日"];

    /// <summary>可购买的商品（顺序与 WPF 复选框相同）。</summary>
    public static IReadOnlyList<string> Items { get; } = ["布匹", "须臾树脂", "大英雄的经验", "流浪者的经验", "精锻用魔矿", "摩拉", "祝圣精华", "祝圣油膏"];

    /// <summary>打开设置时：列表为空则补上“每天重复”（与 WPF 相同，会修改传入列表），返回日期。</summary>
    public static string EnsureSchedule(List<string> objects)
    {
        if (!objects.Any()) objects.Add("每天重复");
        return objects[0];
    }

    /// <summary>商品是否已选。</summary>
    public static bool IsSelected(IReadOnlyList<string> objects, string item) => objects.Contains(item);

    /// <summary>确认时生成新的列表：日期（按 / 拆分，与 WPF 相同）+ 选中的商品（按 <see cref="Items"/> 顺序）。</summary>
    public static List<string> Build(string schedule, IEnumerable<string> selectedItems)
    {
        var selected = selectedItems.ToHashSet(StringComparer.Ordinal);
        return schedule.Split('/').Concat(Items.Where(selected.Contains)).ToList();
    }
}

/// <summary>可勾选的尘歌壶商品。</summary>
public sealed partial class SereniteaPotItemOption(string name, bool isSelected) : ObservableObject
{
    public string Name => name;

    [ObservableProperty] private bool _isSelected = isSelected;
}

/// <summary>地脉花按星期设置的一行：是否运行、类型、国家（直接读写 OneDragonFlowConfig 的 LeyLineRun*/LeyLine*Type/LeyLine*Country）。</summary>
public sealed class OneDragonLeyLineRow(string label, Func<bool> run, Action<bool> setRun, Func<string> type, Action<string> setType, Func<string> country, Action<string> setCountry) : ObservableObject
{
    public string Label => label;

    public bool Run
    {
        get => run();
        set => setRun(value);
    }

    public string? Type
    {
        get => type();
        set
        {
            if (value is not null) setType(value);
        }
    }

    public string? Country
    {
        get => country();
        set
        {
            if (value is not null) setCountry(value);
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Run));
        OnPropertyChanged(nameof(Type));
        OnPropertyChanged(nameof(Country));
    }

    /// <summary>周一至周日 7 行。</summary>
    public static IReadOnlyList<OneDragonLeyLineRow> CreateWeek(OneDragonFlowConfig c) =>
    [
        new("周一", () => c.LeyLineRunMonday, v => c.LeyLineRunMonday = v, () => c.LeyLineMondayType, v => c.LeyLineMondayType = v, () => c.LeyLineMondayCountry, v => c.LeyLineMondayCountry = v),
        new("周二", () => c.LeyLineRunTuesday, v => c.LeyLineRunTuesday = v, () => c.LeyLineTuesdayType, v => c.LeyLineTuesdayType = v, () => c.LeyLineTuesdayCountry, v => c.LeyLineTuesdayCountry = v),
        new("周三", () => c.LeyLineRunWednesday, v => c.LeyLineRunWednesday = v, () => c.LeyLineWednesdayType, v => c.LeyLineWednesdayType = v, () => c.LeyLineWednesdayCountry, v => c.LeyLineWednesdayCountry = v),
        new("周四", () => c.LeyLineRunThursday, v => c.LeyLineRunThursday = v, () => c.LeyLineThursdayType, v => c.LeyLineThursdayType = v, () => c.LeyLineThursdayCountry, v => c.LeyLineThursdayCountry = v),
        new("周五", () => c.LeyLineRunFriday, v => c.LeyLineRunFriday = v, () => c.LeyLineFridayType, v => c.LeyLineFridayType = v, () => c.LeyLineFridayCountry, v => c.LeyLineFridayCountry = v),
        new("周六", () => c.LeyLineRunSaturday, v => c.LeyLineRunSaturday = v, () => c.LeyLineSaturdayType, v => c.LeyLineSaturdayType = v, () => c.LeyLineSaturdayCountry, v => c.LeyLineSaturdayCountry = v),
        new("周日", () => c.LeyLineRunSunday, v => c.LeyLineRunSunday = v, () => c.LeyLineSundayType, v => c.LeyLineSundayType = v, () => c.LeyLineSundayCountry, v => c.LeyLineSundayCountry = v),
    ];
}
