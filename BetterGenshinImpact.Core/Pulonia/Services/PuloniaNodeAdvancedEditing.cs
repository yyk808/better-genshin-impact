using System;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 任务步骤“高级设置”（对应 WPF 一条龙步骤配置的“高级配置与有效来源”）：本步骤参数、共享设置、执行策略以 JSON 编辑，
/// 另显示有效参数（任务默认值叠加本步骤参数）。写回时整体解析并按计划校验，任一项无效都不改动任务
/// </summary>
public static class PuloniaNodeAdvancedEditing
{
    public static string FormatParameters(PuloniaTask task) => task.Parameters.ToString(Formatting.Indented);

    public static string FormatSettings(PuloniaTask task) => task.Settings.ToString(Formatting.Indented);

    public static string FormatPolicy(PuloniaTask task) => JObject.FromObject(task.Policy).ToString(Formatting.Indented);

    /// <summary>引用来源（计划或目录引用）的 JSON；没有来源时为空文本</summary>
    public static string FormatSource(PuloniaTask task) =>
        task.Source is null ? string.Empty : JObject.FromObject(task.Source).ToString(Formatting.Indented);

    /// <summary>读取引用来源 JSON；空文本表示清除来源。格式错误抛出 <see cref="JsonException"/></summary>
    public static PuloniaTaskSource? ReadSource(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : PuloniaTaskJson.Read<PuloniaTaskSource>(json);

    /// <summary>可以直接编辑“资源位置”：非分组且没有仓库资源引用（仓库资源由来源定位）</summary>
    public static bool CanEditPath(PuloniaTask task) => task.TaskType != "group" && task.Resource is null;

    /// <summary>有效参数；未注册的任务类型只有本步骤参数</summary>
    public static string FormatEffective(PuloniaTaskDefinition? definition, PuloniaTask task) =>
        (definition is null ? task.Parameters : PuloniaParameterForm.Effective(definition, task)).ToString(Formatting.Indented);

    /// <summary>解析 JSON 对象；空白视为空对象</summary>
    public static JObject ParseObject(string? text, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return new JObject();
        JToken token;
        try { token = JToken.Parse(text); }
        catch (JsonException e) { throw new FormatException($"{label}不是有效的 JSON：{e.Message}"); }
        return token as JObject ?? throw new FormatException($"{label}必须是 JSON 对象。");
    }

    /// <summary>写回参数、共享设置与执行策略并校验计划；失败时恢复原值并抛出 <see cref="FormatException"/> 或校验异常</summary>
    public static void Apply(PuloniaTaskPlan plan, PuloniaTask task, string? parameters, string? settings, string? policy)
        => Apply(plan, task, parameters, settings, policy, FormatSource(task), task.Path);

    /// <summary>
    /// 同时写回引用来源与资源位置（WPF 一条龙步骤配置的“引用来源”“资源位置”）；任一项无效时全部恢复原值。
    /// 资源位置只在 <see cref="CanEditPath"/> 时写入
    /// </summary>
    public static void Apply(PuloniaTaskPlan plan, PuloniaTask task, string? parameters, string? settings, string? policy,
        string? source, string? path)
    {
        PuloniaTaskSource? src;
        try { src = ReadSource(source); }
        catch (JsonException e) { throw new FormatException("引用来源无效：" + e.Message); }
        var sourceBackup = (task.Source, task.Path);
        try
        {
            task.Source = src;
            if (CanEditPath(task) && path != task.Path) task.Path = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
            ApplyCore(plan, task, parameters, settings, policy);
        }
        catch
        {
            (task.Source, task.Path) = sourceBackup;
            throw;
        }
    }

    private static void ApplyCore(PuloniaTaskPlan plan, PuloniaTask task, string? parameters, string? settings, string? policy)
    {
        var p = ParseObject(parameters, "任务参数");
        var s = ParseObject(settings, "共享设置");
        PuloniaTaskPolicy po;
        try { po = ParseObject(policy, "执行策略").ToObject<PuloniaTaskPolicy>() ?? new PuloniaTaskPolicy(); }
        catch (JsonException e) { throw new FormatException("执行策略无效：" + e.Message); }

        var backup = (task.Parameters, task.Settings, task.Policy);
        try
        {
            (task.Parameters, task.Settings, task.Policy) = (p, s, po);
            PuloniaTaskValidator.ValidatePlan(plan);
        }
        catch
        {
            (task.Parameters, task.Settings, task.Policy) = backup;
            throw;
        }
    }
}
