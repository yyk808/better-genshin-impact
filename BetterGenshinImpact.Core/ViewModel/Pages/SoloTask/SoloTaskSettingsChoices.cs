using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFishing;

namespace BetterGenshinImpact.ViewModel.Pages.SoloTask;

/// <summary>下拉项：值 + 显示名</summary>
public sealed record SoloTaskChoice(object Value, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// 独立任务页的下拉选项（WPF TaskSettingsPage 与 Avalonia 独立任务页共用）
/// </summary>
public static class SoloTaskSettingsChoices
{
    /// <summary>需要快速分解圣遗物的最高星级</summary>
    public static List<string> ArtifactSalvageStarList { get; } = ["4", "3", "2", "1"];

    /// <summary>幽境危战战场（从上到下一、二、三）</summary>
    public static List<int> BossNumList { get; } = [1, 2, 3];

    /// <summary>盾奶位角色在队伍中的位置（空 = 关闭）</summary>
    public static List<string> AvatarIndexList { get; } = ["", "1", "2", "3", "4"];

    /// <summary>伤害数字识别模式（与 WPF 下拉相同的顺序与文字）</summary>
    public static IReadOnlyList<SoloTaskChoice> DamageNumberRecognitionModes { get; } =
    [
        new(DamageNumberRecognitionMode.Disabled, "禁用"),
        new(DamageNumberRecognitionMode.Ocr, "OCR识别"),
        new(DamageNumberRecognitionMode.Color, "颜色识别"),
    ];

    /// <summary>钓鱼昼夜策略（显示名取枚举 Description）</summary>
    public static IReadOnlyList<SoloTaskChoice> FishingTimePolicies { get; } =
        Enum.GetValues<FishingTimePolicy>().Select(v => new SoloTaskChoice(v, Description(v))).ToList();

    public static string Description(Enum value) =>
        value.GetType().GetField(value.ToString())?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? value.ToString();
}
