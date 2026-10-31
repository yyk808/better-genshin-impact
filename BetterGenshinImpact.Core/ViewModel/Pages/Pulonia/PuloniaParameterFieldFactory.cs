using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.ViewModel.Resin;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>按能力 Schema 生成通用设置字段（原 PuloniaTaskSettingsEditorViewModel.CreateFields，WPF 委托到这里）。</summary>
public static class PuloniaParameterFieldFactory
{
    /// <summary>
    /// 按能力 Schema 生成通用设置卡片：枚举、秘境和战斗策略显示为下拉框。
    /// </summary>
    /// <param name="definition">能力定义；为空时没有字段。</param>
    /// <param name="values">当前有效参数。</param>
    /// <param name="dialogs">树脂设置弹窗；为空时树脂字段只显示摘要。</param>
    /// <returns>设置字段。</returns>
    public static IReadOnlyList<PuloniaOneDragonParameterFieldViewModel> CreateFields(
        PuloniaTaskDefinition? definition, JObject values, IResinSettingsDialogs? dialogs = null)
    {
        if (definition?.ParameterSchema["properties"] is not JObject properties)
            return [];
        var required = new HashSet<string>(
            (definition.ParameterSchema["required"] as JArray ?? new JArray()).Values<string>().OfType<string>(),
            StringComparer.Ordinal);
        // 共享设置覆盖的顶层参数只能在共享设置中修改，任务参数表单不显示
        var sharedRoots = definition.SettingBindings.Values.Select(path => path.Split('.')[0]).ToHashSet(StringComparer.Ordinal);
        return properties.Properties()
            .Where(property => !sharedRoots.Contains(property.Name))
            .Select(property =>
            {
                var schema = PuloniaOneDragonSettingChoices.Decorate(property.Name, property.Value as JObject ?? new JObject(),
                    values[property.Name], definition.TaskType);
                return new PuloniaOneDragonParameterFieldViewModel(property.Name, schema, values[property.Name],
                    required.Contains(property.Name), dialogs);
            })
            .ToArray();
    }
}
