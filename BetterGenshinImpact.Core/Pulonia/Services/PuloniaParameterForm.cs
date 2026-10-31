using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>任务参数字段的编辑方式</summary>
public enum PuloniaParameterFieldKind
{
    Boolean,
    Integer,
    Number,
    Text,
    Choice,
    StringList,
    Json,
}

/// <summary>按能力参数 Schema 描述的一个顶层参数（不含共享设置覆盖的参数）</summary>
public sealed record PuloniaParameterField(
    string Name,
    string DisplayName,
    string Description,
    PuloniaParameterFieldKind Kind,
    IReadOnlyList<string> Choices,
    JToken? Value,
    bool Required,
    bool IsOverridden)
{
    /// <summary>当前值的文本形式（布尔以外的编辑框使用）</summary>
    public string Text => Value switch
    {
        null or { Type: JTokenType.Null } => "",
        JArray array when Kind == PuloniaParameterFieldKind.StringList => string.Join("\n", array.Values<string>()),
        JValue v when Kind != PuloniaParameterFieldKind.Json => Convert.ToString(v.Value, CultureInfo.InvariantCulture) ?? "",
        _ => Value.ToString(Formatting.None),
    };
}

/// <summary>
/// 任务参数的界面无关编辑逻辑：按能力 Schema 列出顶层参数并给出有效值（默认值、共享设置与任务参数合并），
/// 把输入文本解析为 JSON 值，合并后按 Schema 校验再写入任务参数。Avalonia 任务计划页使用；
/// WPF 版的参数卡片另有秘境、战斗策略、树脂等专属编辑器
/// </summary>
public static class PuloniaParameterForm
{
    /// <summary>列出可编辑的顶层参数</summary>
    public static IReadOnlyList<PuloniaParameterField> Describe(PuloniaTaskDefinition definition, PuloniaTask task)
    {
        if (definition.ParameterSchema["properties"] is not JObject properties)
            return [];
        var required = new HashSet<string>(
            (definition.ParameterSchema["required"] as JArray ?? new JArray()).Values<string>().OfType<string>(), StringComparer.Ordinal);
        var sharedRoots = definition.SettingBindings.Values.Select(path => path.Split('.')[0]).ToHashSet(StringComparer.Ordinal);
        var effective = Effective(definition, task);
        var fields = new List<PuloniaParameterField>();
        foreach (var property in properties.Properties().Where(p => !sharedRoots.Contains(p.Name)))
        {
            var schema = property.Value as JObject ?? new JObject();
            var (kind, choices) = KindOf(schema);
            var displayKey = System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name);
            fields.Add(new PuloniaParameterField(property.Name,
                schema.Value<string>("title") ?? PuloniaTaskFieldNames.GetDisplayName(displayKey),
                schema.Value<string>("description") ?? "",
                kind, choices, effective[property.Name]?.DeepClone(), required.Contains(property.Name),
                task.Parameters.ContainsKey(property.Name)));
        }

        return fields;
    }

    /// <summary>有效参数（与运行准备相同的层级：默认值 → 共享设置默认 → 任务参数）</summary>
    public static JObject Effective(PuloniaTaskDefinition definition, PuloniaTask task)
        => PuloniaParameterResolver.Resolve(task, definition, []).Parameters;

    private static (PuloniaParameterFieldKind, IReadOnlyList<string>) KindOf(JObject schema)
    {
        if (schema["enum"] is JArray values && values.All(v => v.Type == JTokenType.String))
            return (PuloniaParameterFieldKind.Choice, values.Values<string>().OfType<string>().ToList());
        var type = TypeOf(schema);
        return type switch
        {
            "boolean" => (PuloniaParameterFieldKind.Boolean, []),
            "integer" => (PuloniaParameterFieldKind.Integer, []),
            "number" => (PuloniaParameterFieldKind.Number, []),
            "string" => (PuloniaParameterFieldKind.Text, []),
            "array" when schema["items"] is JObject items && TypeOf(items) == "string" => (PuloniaParameterFieldKind.StringList, []),
            _ => (PuloniaParameterFieldKind.Json, []),
        };
    }

    /// <summary>Schema 的 type 可为字符串或数组（如 ["string","null"]），取第一个非 null 类型</summary>
    private static string? TypeOf(JObject schema)
        => schema["type"] is JArray types ? types.Values<string>().FirstOrDefault(t => t != "null") : schema.Value<string>("type");

    /// <summary>把输入解析为参数值；无法解析时抛出带字段名的 <see cref="FormatException"/></summary>
    public static JToken Parse(PuloniaParameterField field, string? text, bool? boolean = null)
    {
        text ??= "";
        switch (field.Kind)
        {
            case PuloniaParameterFieldKind.Boolean:
                return new JValue(boolean ?? (bool.TryParse(text, out var b) && b));
            case PuloniaParameterFieldKind.Integer:
                return long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)
                    ? new JValue(l)
                    : throw new FormatException($"“{field.DisplayName}”必须是整数。");
            case PuloniaParameterFieldKind.Number:
                return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d)
                    ? new JValue(d)
                    : throw new FormatException($"“{field.DisplayName}”必须是有效数字。");
            case PuloniaParameterFieldKind.StringList:
                return new JArray(text.Split('\n').Select(s => s.TrimEnd('\r')).Where(s => s.Length > 0));
            case PuloniaParameterFieldKind.Json:
                try
                {
                    return JToken.Parse(text);
                }
                catch (JsonException e)
                {
                    throw new FormatException($"“{field.DisplayName}”不是有效的 JSON：{e.Message}");
                }
            default:
                return new JValue(text);
        }
    }

    /// <summary>
    /// 把变化写入任务参数：先与有效参数合并并按 Schema 校验，全部通过才写入（只写变化的顶层字段）。
    /// 返回实际写入的字段数
    /// </summary>
    public static int Apply(PuloniaTaskDefinition definition, PuloniaTask task, JObject changes)
    {
        var effective = Effective(definition, task);
        var actual = new JObject();
        foreach (var field in changes.Properties())
        {
            if (!JToken.DeepEquals(effective[field.Name], field.Value))
                actual[field.Name] = field.Value.DeepClone();
        }

        if (actual.Count == 0)
            return 0;
        foreach (var field in actual.Properties())
            effective[field.Name] = field.Value.DeepClone();
        PuloniaTaskValidator.ValidateParameters(effective, definition.ParameterSchema, task.Name);
        foreach (var field in actual.Properties())
            task.Parameters[field.Name] = field.Value.DeepClone();
        return actual.Count;
    }
}
