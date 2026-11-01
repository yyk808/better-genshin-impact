using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 跨位置粘贴子树时的共享设置继承处理（WPF 任务计划与 Avalonia 共用）：
/// 复制时记录各叶子的有效参数与子树外层的有效共享设置，粘贴时比较目标位置的继承结果是否改变有效参数，
/// 需要时把复制来源外层的设置写到粘贴的子树根节点上以保留复制时的有效设置。
/// </summary>
public static class PuloniaPasteInheritance
{
    /// <summary>把由外到内的节点模型转换为设置链，方案按 ID 关联；找不到的方案视为缺失。</summary>
    /// <param name="models">由外到内的节点模型。</param>
    /// <param name="findProfile">按 ID 查找已加载的设置方案。</param>
    public static IReadOnlyList<PuloniaSettingLevel> CreateChain(IEnumerable<PuloniaTask> models, Func<string, PuloniaSettingsProfile?> findProfile)
        => models.Select(model => new PuloniaSettingLevel(model, model.Id,
            model.ProfileId is { } profileId ? findProfile(profileId) : null)).ToArray();

    /// <summary>按祖先链解析具体任务的有效参数（不含本次调用覆盖）。</summary>
    public static JObject ResolveEffectiveParameters(PuloniaTask task, IReadOnlyList<PuloniaTask> ancestors,
        Func<PuloniaTask, PuloniaTaskDefinition?> getDefinition, Func<string, PuloniaSettingsProfile?> findProfile)
        => PuloniaParameterResolver.Resolve(task, getDefinition(task), CreateChain([.. ancestors, task], findProfile)).Parameters;

    /// <summary>某个位置之下（不含任何子树内部设置）的有效共享设置：设置项 ID 到值。</summary>
    /// <param name="ancestors">由根到外层最近节点的模型链；为空时只有目录默认值。</param>
    public static JObject CaptureOuterSettings(IReadOnlyList<PuloniaTask> ancestors, Func<string, PuloniaSettingsProfile?> findProfile)
    {
        var resolution = PuloniaParameterResolver.ResolveSettings(CreateChain(ancestors, findProfile));
        return new JObject(resolution.Values.Select(item => new JProperty(item.Key, item.Value)));
    }

    /// <summary>收集子树中各叶子复制当时的有效参数（任务 ID 到参数）。</summary>
    /// <param name="subtree">复制的子树根。</param>
    /// <param name="ancestors">由根到子树父节点的模型链。</param>
    public static IReadOnlyDictionary<string, JObject> CaptureEffectiveLeafParameters(PuloniaTask subtree, IReadOnlyList<PuloniaTask> ancestors,
        Func<PuloniaTask, PuloniaTaskDefinition?> getDefinition, Func<string, PuloniaSettingsProfile?> findProfile)
    {
        var result = new Dictionary<string, JObject>(StringComparer.Ordinal);
        Visit(subtree, ancestors);
        return result;

        void Visit(PuloniaTask node, IReadOnlyList<PuloniaTask> chain)
        {
            if (IsLeaf(node))
            {
                result[node.Id] = ResolveEffectiveParameters(node, chain, getDefinition, findProfile);
                return;
            }

            var nested = chain.Concat([node]).ToArray();
            foreach (var child in node.Children) Visit(child, nested);
        }
    }

    /// <summary>比较粘贴到目标位置后各叶子的有效参数与复制时是否不同。</summary>
    /// <param name="sourceSubtree">复制时的子树（原 ID）。</param>
    /// <param name="pastedSubtree">待插入的副本（新 ID，结构与来源相同）。</param>
    /// <param name="sourceEffectiveParameters">复制时各叶子的有效参数，键为来源任务 ID。</param>
    /// <param name="targetAncestors">由根到目标父节点的模型链。</param>
    public static bool HasDifference(PuloniaTask sourceSubtree, PuloniaTask pastedSubtree,
        IReadOnlyDictionary<string, JObject> sourceEffectiveParameters, IReadOnlyList<PuloniaTask> targetAncestors,
        Func<PuloniaTask, PuloniaTaskDefinition?> getDefinition, Func<string, PuloniaSettingsProfile?> findProfile)
    {
        return Compare(sourceSubtree, pastedSubtree, targetAncestors);

        bool Compare(PuloniaTask source, PuloniaTask pasted, IReadOnlyList<PuloniaTask> ancestors)
        {
            if (IsLeaf(pasted))
            {
                var targetEffective = ResolveEffectiveParameters(pasted, ancestors, getDefinition, findProfile);
                return !sourceEffectiveParameters.TryGetValue(source.Id, out var sourceEffective)
                       || !JToken.DeepEquals(sourceEffective, targetEffective);
            }

            var nestedAncestors = ancestors.Concat([pasted]).ToArray();
            for (var i = 0; i < Math.Min(source.Children.Count, pasted.Children.Count); i++)
            {
                if (Compare(source.Children[i], pasted.Children[i], nestedAncestors))
                    return true;
            }
            return source.Children.Count != pasted.Children.Count;
        }
    }

    /// <summary>
    /// 保留复制时的有效设置：子树内部的设置随节点复制，只需让子树外层的有效设置与复制时一致。
    /// 把与复制来源外层不同的设置项写到粘贴的子树根节点上；根节点自身或其方案已设置的项本来就由内部提供，不写。
    /// 叶子根节点只写其能力使用的设置项。
    /// </summary>
    /// <param name="pastedSubtree">待插入的子树（新 ID 副本）。</param>
    /// <param name="sourceOuterSettings">复制来源外层的有效共享设置。</param>
    /// <param name="targetAncestors">由根到目标父节点的模型链。</param>
    public static void PreserveCopiedSettings(PuloniaTask pastedSubtree, JObject sourceOuterSettings, IReadOnlyList<PuloniaTask> targetAncestors,
        Func<PuloniaTask, PuloniaTaskDefinition?> getDefinition, Func<string, PuloniaSettingsProfile?> findProfile)
    {
        var targetOuter = CaptureOuterSettings(targetAncestors, findProfile);
        var profile = pastedSubtree.ProfileId is { } profileId ? findProfile(profileId) : null;
        var allowed = IsLeaf(pastedSubtree) ? getDefinition(pastedSubtree)?.SettingBindings.Keys.ToHashSet(StringComparer.Ordinal) : null;
        foreach (var (key, sourceValue) in sourceOuterSettings)
        {
            if (sourceValue is null || JToken.DeepEquals(sourceValue, targetOuter[key])
                || pastedSubtree.Settings.ContainsKey(key) || profile?.Settings.ContainsKey(key) == true
                || allowed is not null && !allowed.Contains(key))
                continue;
            pastedSubtree.Settings[key] = sourceValue.DeepClone();
        }
    }

    private static bool IsLeaf(PuloniaTask task) => task.TaskType != "group";
}
