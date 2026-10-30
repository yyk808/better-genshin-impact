using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFight.Strategy;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;
using BetterGenshinImpact.GameTask.AutoWood;
using BetterGenshinImpact.GameTask.GetGridIcons;
using BetterGenshinImpact.GameTask.UseRedeemCode;
using BetterGenshinImpact.Platform;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BetterGenshinImpact.GameTask.SoloTask.Execution;

/// <summary>
/// 独立任务页上由页面维护（不在 AllConfig 中）的临时输入，以及需要界面交互的输入。
/// Windows 由 TaskSettingsPageViewModel 实现，Avalonia 由其独立任务页实现
/// </summary>
public interface ISoloTaskParamInputs : ITaskSettingsSource
{
    /// <summary>自动秘境轮数（0 表示用配置/直到体力耗尽）</summary>
    int AutoDomainRoundNum { get; }

    /// <summary>背包数量对比的目标分类</summary>
    InventoryCountComparisonTarget InventoryCountComparisonTarget { get; }

    /// <summary>向用户索取兑换码；取消或无有效内容时返回 null</summary>
    List<string>? PromptForRedeemCodes();
}

/// <summary>
/// 按独立任务页的配置与临时输入创建任务参数（原 TaskSettingsPageViewModel.CreateSoloTaskParam，抽到 Core 供两个宿主共用）。
/// 一条龙的参数由一条龙页面维护，不在这里创建
/// </summary>
public static class SoloTaskParamFactory
{
    /// <summary>
    /// 创建任务参数
    /// </summary>
    /// <param name="taskId">任务标识</param>
    /// <param name="config">当前配置</param>
    /// <param name="registry">独立任务注册表（其余任务用无参构造按全局配置给出参数）</param>
    /// <param name="inputs">页面临时输入</param>
    /// <returns>参数对象；策略未配置或用户取消输入时返回 null</returns>
    public static object? Create(string taskId, AllConfig config, SoloTaskRegistry registry, ISoloTaskParamInputs inputs)
    {
        switch (taskId)
        {
            case AutoGeniusInvokationTask.TaskId:
                return inputs.GetTcgStrategy(out var strategyContent) ? null : new GeniusInvokationTaskParam(strategyContent);
            case AutoWoodTask.TaskId:
                return new WoodTaskParam(inputs.AutoWoodRoundNum, inputs.AutoWoodDailyMaxCount);
            case AutoFightEntryTask.TaskId:
                return inputs.GetFightStrategy(out var fightStrategyPath)
                    ? null
                    : new AutoFightParam(fightStrategyPath, config.AutoFightConfig);
            case AutoDomainTask.TaskId:
                return inputs.GetFightStrategy(out var domainStrategyPath)
                    ? null
                    : new AutoDomainParam(inputs.AutoDomainRoundNum, domainStrategyPath);
            case AutoBossTask.TaskId:
                return inputs.GetFightStrategy(config.AutoBossConfig.StrategyName, out var bossStrategyPath)
                    ? null
                    : new AutoBossParam(bossStrategyPath);
            case AutoStygianOnslaughtTask.TaskId:
                // 无参构造读取全局幽境危战配置，再用点击时选中的策略路径覆盖
                return inputs.GetFightStrategy(config.AutoStygianOnslaughtConfig.StrategyName, out var stygianStrategyPath)
                    ? null
                    : new AutoStygianOnslaughtParam(stygianStrategyPath);
            case AutoFishingTask.TaskId:
                return AutoFishingTaskParam.BuildFromConfig(config.AutoFishingConfig, inputs.SaveScreenshotOnKeyTick);
            case AutoArtifactSalvageTask.TaskId:
                var salvageConfig = config.AutoArtifactSalvageConfig;
                return new AutoArtifactSalvageTaskParam(
                    int.Parse(salvageConfig.MaxArtifactStar),
                    salvageConfig.JavaScript,
                    salvageConfig.ArtifactSetFilter,
                    salvageConfig.MaxNumToCheck,
                    salvageConfig.RecognitionFailurePolicy);
            case UseRedemptionCodeTask.TaskId:
                var codes = inputs.PromptForRedeemCodes();
                return codes is null ? null : new UseRedemptionCodeParam { Codes = codes };
            case GetGridIconsTask.TaskId:
                return new GetGridIconsParam
                {
                    GridName = config.GetGridIconsConfig.GridName,
                    StarAsSuffix = config.GetGridIconsConfig.StarAsSuffix,
                    MaxNumToGet = config.GetGridIconsConfig.MaxNumToGet
                };
            case GridIconsAccuracyTestTask.TaskId:
                return new GridIconsAccuracyTestParam
                {
                    GridName = config.GetGridIconsConfig.GridName,
                    MaxNumToTest = config.GetGridIconsConfig.MaxNumToGet
                };
            case InventoryCountComparisonTask.TaskId:
                return new InventoryCountComparisonParam { Target = inputs.InventoryCountComparisonTarget };
            default:
                // 地脉花、音游、烹饪等任务的无参构造已按全局配置给出参数
                return registry.Get(taskId).CreateDefaultParam();
        }
    }

    /// <summary>
    /// 把多行文本拆成兑换码列表（去掉空行与首尾空白）
    /// </summary>
    public static List<string> ParseRedeemCodes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(code => code.Trim())
            .Where(code => !string.IsNullOrEmpty(code))
            .ToList();
    }
}

/// <summary>
/// 读取七圣召唤策略、解析战斗策略路径（原 TaskSettingsPageViewModel 中的实现）。
/// 返回 true 表示失败，失败原因经 notify 提示（与原方法的语义一致）
/// </summary>
public static class TaskStrategyLookup
{
    /// <summary>读取七圣召唤策略文件内容</summary>
    public static bool GetTcgStrategy(string? strategyName, Action<DialogKind, string> notify, out string content)
    {
        content = string.Empty;
        if (string.IsNullOrEmpty(strategyName))
        {
            notify(DialogKind.Warning, "请先选择策略");
            return true;
        }

        var path = Global.Absolute(@"User\AutoGeniusInvokation\" + strategyName + ".txt");

        if (!File.Exists(path))
        {
            notify(DialogKind.Error, "策略文件不存在");
            return true;
        }

        content = File.ReadAllText(path);
        return false;
    }

    /// <summary>按策略名取战斗策略路径；固定策略不对应文件，名称、前缀和路径统一由解析器检查</summary>
    public static bool GetFightStrategy(string? strategyName, Action<DialogKind, string> notify, out string path)
    {
        if (string.IsNullOrEmpty(strategyName))
        {
            notify(DialogKind.Warning, "请先在下拉列表配置中选择战斗策略！");
            path = string.Empty;
            return true;
        }

        var strategy = CombatStrategyResolver.Parse(strategyName);
        path = strategy.Path;
        if (!strategy.Exists)
        {
            notify(DialogKind.Error, "当前选择的自动战斗策略文件不存在");
            return true;
        }

        return false;
    }
}
