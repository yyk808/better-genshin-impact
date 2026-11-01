using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 触发方式编辑的界面无关逻辑：选项文字、可选目标与账号、数值校验、定时预览。
/// WPF 触发器编辑器与 Avalonia 任务计划页共用
/// </summary>
public static class PuloniaTriggerEditing
{
    /// <summary>服务器时区选项</summary>
    public static IReadOnlyDictionary<PuloniaTaskServerTimeZone, string> ServerTimeZones { get; } = new Dictionary<PuloniaTaskServerTimeZone, string>
    {
        [PuloniaTaskServerTimeZone.UtcPlus8] = "UTC+8｜天空岛/世界树",
        [PuloniaTaskServerTimeZone.Europe] = "UTC+1｜欧服",
        [PuloniaTaskServerTimeZone.America] = "UTC-5｜美服"
    };

    /// <summary>忙碌策略选项</summary>
    public static IReadOnlyDictionary<PuloniaTaskBusyPolicy, string> BusyPolicies { get; } = new Dictionary<PuloniaTaskBusyPolicy, string>
    {
        [PuloniaTaskBusyPolicy.Skip] = "同计划已忙碌：跳过 / 合并（推荐）",
        [PuloniaTaskBusyPolicy.QueueOnce] = "同计划运行后：再排一次",
        [PuloniaTaskBusyPolicy.StopCurrent] = "停止当前任务，清理后优先执行"
    };

    /// <summary>一条龙配置的触发器使用计划绑定的账号</summary>
    public static bool UsesPlanAccountBinding(PuloniaTaskPlan plan) => plan.Purpose == PuloniaTaskPlanPurpose.OneDragon;

    /// <summary>可选的触发目标：整个计划或其中一个任务（按深度缩进）</summary>
    public static Dictionary<string, string> BuildTargets(PuloniaTaskPlan plan)
    {
        var targets = new Dictionary<string, string> { [""] = "整个计划" };
        AddTargets(plan.RootTask, targets, 0);
        return targets;
    }

    private static void AddTargets(PuloniaTask task, Dictionary<string, string> targets, int depth)
    {
        if (depth > 0) targets[task.Id] = new string('　', depth - 1) + task.Name;
        foreach (var child in task.Children) AddTargets(child, targets, depth + 1);
    }

    /// <summary>可选的账号参数资料</summary>
    public static Dictionary<string, string> BuildAccounts(PuloniaTaskPlan plan)
    {
        var accounts = new Dictionary<string, string>
        {
            // 上游统筹设计删除了触发器的账号参数资料：运行账户只由计划的运行账户设置和实例当前账户决定
            [""] = "运行账户由计划设置决定"
        };
        return accounts;
    }

    /// <summary>解析整数输入，超出范围时抛出带字段名的 <see cref="FormatException"/></summary>
    public static int ParseNumber(string value, string label, int min, int max)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || result < min || result > max)
            throw new FormatException($"{label}必须是 {min}—{max} 的整数。");
        return result;
    }

    /// <summary>允许延迟启动（分钟）</summary>
    public static int ParseWindowMinutes(string value) => ParseNumber(value, "允许延迟启动（分钟）", 1, 10080);

    /// <summary>定时触发接下来的执行时间（所选服务器时区）；表达式无效时抛出异常</summary>
    public static string PreviewSchedule(PuloniaTaskTrigger trigger, DateTimeOffset afterUtc, int count = 5)
    {
        var utcOffset = PuloniaTaskSchedule.GetUtcOffset(trigger.ServerTimeZone);
        var cron = new PuloniaCronExpression(trigger.Cron);
        var lines = new List<string>();
        var after = afterUtc;
        for (var index = 0; index < count; index++)
        {
            var next = cron.Next(after, utcOffset);
            if (next is null) break;
            lines.Add(next.Value.ToOffset(utcOffset).ToString("yyyy-MM-dd ddd HH:mm zzz", CultureInfo.GetCultureInfo("zh-CN")));
            after = next.Value;
        }

        return lines.Count > 0 ? "接下来执行（所选时区）：\n" + string.Join("\n", lines) : "未来八年没有可执行时间。";
    }

    /// <summary>新增或替换触发器后的计划副本（整体校验通过才返回），调用方确认后再写回并保存</summary>
    public static PuloniaTaskPlan WithTrigger(PuloniaTaskPlan plan, PuloniaTaskTrigger trigger)
    {
        var candidate = PuloniaTaskJson.ClonePlan(plan);
        var index = candidate.Triggers.FindIndex(item => item.Id == trigger.Id);
        if (index >= 0) candidate.Triggers[index] = trigger;
        else candidate.Triggers.Add(trigger);
        PuloniaTaskValidator.ValidatePlan(candidate);
        return candidate;
    }
}
