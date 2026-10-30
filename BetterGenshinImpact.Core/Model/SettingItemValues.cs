using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace BetterGenshinImpact.Model;

/// <summary>
/// JS 脚本自定义设置（settings.json 描述的表单）的取值规则，与界面无关：默认值写入、多选列表的规范化、读写。
/// Windows（WPF 控件）与 Linux（Avalonia 控件）的表单共用
/// </summary>
public static class SettingItemValues
{
    /// <summary>表单支持的设置项类型</summary>
    public static readonly IReadOnlyList<string> SupportedTypes = ["separator", "input-text", "select", "checkbox", "multi-checkbox", "cascade-select"];

    /// <summary>按设置项把默认值写入设置对象（已有值时不覆盖）。多选项返回规范化后的已选列表，其它类型返回 null</summary>
    public static List<string>? ApplyDefault(SettingItem item, IDictionary<string, object?> ctx)
    {
        switch (item.Type)
        {
            case "input-text" or "select" or "cascade-select":
                if (item.Default != null)
                {
                    ctx.TryAdd(item.Name, item.Default.ToString());
                }

                return null;
            case "checkbox":
                if (item.Default != null && bool.TryParse(item.Default.ToString(), out var value))
                {
                    ctx.TryAdd(item.Name, value);
                }

                return null;
            case "multi-checkbox":
                if (!ctx.ContainsKey(item.Name))
                {
                    ctx[item.Name] = item.Default is JsonElement j ? j.Deserialize<List<string>>() : new List<string>();
                }
                else if (ctx[item.Name] is List<object> listOfObjects)
                {
                    ctx[item.Name] = listOfObjects.Select(i => (string)i).ToList();
                }

                return (List<string>)ctx[item.Name]!;
            default:
                return null;
        }
    }

    /// <summary>读取文本值（输入框、下拉框、级联选择）</summary>
    public static string? GetText(IDictionary<string, object?> ctx, string name)
        => ctx.TryGetValue(name, out var v) ? v?.ToString() : null;

    /// <summary>读取勾选值</summary>
    public static bool GetBool(IDictionary<string, object?> ctx, string name)
        => ctx.TryGetValue(name, out var v) && v switch { bool b => b, string s => bool.TryParse(s, out var r) && r, _ => false };

    /// <summary>勾选或取消多选项中的一项（与 WPF 表单相同：保持原有顺序，勾选时追加）</summary>
    public static void SetChecked(List<string> checkedValues, string option, bool isChecked)
    {
        if (isChecked && !checkedValues.Contains(option))
        {
            checkedValues.Add(option);
        }
        else if (!isChecked)
        {
            checkedValues.Remove(option);
        }
    }

    /// <summary>级联选择保存的是二级选项值；返回包含该值的一级选项（与 WPF CascadeSelector 相同：取第一个匹配）</summary>
    public static string? FindCascadeParent(SettingItem item, string? value)
    {
        if (string.IsNullOrEmpty(value) || item.CascadeOptions == null)
        {
            return null;
        }

        foreach (var kvp in item.CascadeOptions)
        {
            if (kvp.Value.Contains(value))
            {
                return kvp.Key;
            }
        }

        return null;
    }
}
