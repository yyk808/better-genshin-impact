using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// 任务计划页的路线 CD：任务树上的标记与按计划筛选的 CD 记录（对照 WPF PuloniaPlanEditingSession.Cooldowns）。
/// 判定统一调用 Core 的 <see cref="PuloniaCollectionCooldown.GetStatus"/>，规则按节点有效的共享设置“路线 CD”解析。
/// 普通计划的作用域为当前登录账号（Avalonia 页只列普通计划）。
/// </summary>
public sealed partial class TaskPlanPageViewModel
{
    private PuloniaCollectionCooldownIndex _cooldownIndex = PuloniaCollectionCooldownIndex.Empty;

    /// <summary>CD 记录只看当前计划（含引用计划）中的路线</summary>
    [ObservableProperty] private bool _cooldownsForPlanOnly = true;

    partial void OnCooldownsForPlanOnlyChanged(bool value) => _ = RefreshCooldownsAsync();

    /// <summary>由外到内的节点模型转为设置链，方案按 ID 关联已加载的设置方案</summary>
    internal IReadOnlyList<PuloniaSettingLevel> CreateSettingChain(IEnumerable<PuloniaTask> models)
        => models.Select(model => new PuloniaSettingLevel(model, model.Id,
            model.ProfileId is { } id ? Profiles.FirstOrDefault(p => p.Profile.Id == id)?.Profile : null)).ToArray();

    /// <summary>地图追踪叶子按有效设置解析的 CD 规则；不是路线、路径含变量或设置无效时为空</summary>
    internal PuloniaCollectionCooldownPolicy? ResolveCooldownPolicy(IReadOnlyList<PuloniaTask> chain)
    {
        var task = chain[^1];
        if (task.TaskType != "pathing" || task.Source is not null) return null;
        var path = ResolveCooldownPath(task);
        if (task.Resource is null && path is null) return null;
        try
        {
            var values = PuloniaParameterResolver.ResolveSettings(CreateSettingChain(chain)).Values;
            values.TryGetValue(PuloniaSettingCatalog.CollectionCooldownKey, out var setting);
            return PuloniaCollectionCooldown.Resolve(path, task.Resource,
                new JObject { [PuloniaCollectionCooldown.ParameterName] = setting?.DeepClone() });
        }
        catch (Exception ex) when (ex is PuloniaTaskValidationException or ArgumentException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    private string? ResolveCooldownPath(PuloniaTask task)
    {
        var path = task.Path;
        if (string.IsNullOrWhiteSpace(path) || path.Contains('{') || path.Contains('}')) return null;
        try
        {
            var baseDirectory = _tasks?.Definitions.FirstOrDefault(d => d.TaskType == task.TaskType)?.ResourceBaseDirectory;
            return Path.GetFullPath(path, Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>节点的 CD 标记与说明（与 WPF 相同：CD 中 · 剩余时间 / 已完成 / 未识别 CD）</summary>
    internal (string? Text, string? Tip) DescribeCooldown(IReadOnlyList<PuloniaTask> chain)
    {
        if (ResolveCooldownPolicy(chain) is not { } policy) return (null, null);
        var now = DateTimeOffset.UtcNow;
        var scope = PuloniaCollectionCooldown.BuildScopeKey();
        var status = PuloniaCollectionCooldown.GetStatus(policy,
            _cooldownIndex.Find(scope, PuloniaCollectionCooldown.GetEffectKey(policy)), now, PuloniaServerTime.GetUtcOffsetMinutes());
        var text = status.State switch
        {
            PuloniaCollectionCooldownState.CoolingDown when status.NextEligibleAt is { } next => "CD 中 · " + Remaining(next - now),
            PuloniaCollectionCooldownState.Completed => "已完成",
            PuloniaCollectionCooldownState.Unmatched => "未识别 CD",
            _ => null,
        };
        if (text is null) return (null, null);
        var lines = new List<string> { $"规则：{policy.MaterialName ?? "未识别"} · {policy.Description}" };
        if (status.LastSuccessAt is { } last) lines.Add($"上次成功：{last.ToLocalTime():yyyy-MM-dd HH:mm}");
        if (status.NextEligibleAt is { } eligible) lines.Add($"下次可执行：{eligible.ToLocalTime():yyyy-MM-dd HH:mm}");
        lines.Add(status.State == PuloniaCollectionCooldownState.Unmatched
            ? "路径中没有已知的材料或类别目录，成功后不会跳过；可在共享设置“路线 CD”中选择自定义周期。"
            : "本次运行会跳过；可在“路线 CD”页签清除记录。");
        return (text, string.Join("\n", lines));
    }

    private static string Remaining(TimeSpan span) => span.TotalDays >= 1 ? $"{(int)span.TotalDays} 天 {span.Hours} 小时"
        : span.TotalHours >= 1 ? $"{(int)span.TotalHours} 小时 {span.Minutes} 分" : $"{Math.Max(1, (int)Math.Ceiling(span.TotalMinutes))} 分钟";

    /// <summary>计划（含引用计划）中直接添加的路线的资源键</summary>
    internal HashSet<string> CollectPlanEffectKeys(PuloniaTaskPlan plan)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        Visit(plan);
        return keys;

        void Visit(PuloniaTaskPlan current)
        {
            if (!visiting.Add(current.Id)) return;
            Walk([current.RootTask]);
        }

        void Walk(List<PuloniaTask> chain)
        {
            var task = chain[^1];
            if (task.Source is { Kind: "plan", PlanId: { } planId })
            {
                if (Plans.FirstOrDefault(p => p.Plan.Id == planId) is { } target) Visit(target.Plan);
                return;
            }

            if (ResolveCooldownPolicy(chain) is { } policy) keys.Add(PuloniaCollectionCooldown.GetEffectKey(policy));
            foreach (var child in task.Children) Walk([.. chain, child]);
        }
    }

    private void RefreshCooldownBadges()
    {
        foreach (var row in Nodes) row.RefreshCooldown();
    }
}
