using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// 任务计划页的路线 CD：任务树标记、计划摘要与按计划筛选的 CD 记录（对照 WPF PuloniaPlanEditingSession.Cooldowns）。
/// 判定统一调用 Core 的 <see cref="PuloniaRouteCooldown.GetStatus"/>，规则按节点有效的共享设置“路线 CD”解析；
/// 分组汇总下属启用路线，计划引用沿用被引用计划自身的汇总，目录引用不展开、只按该组规则判定完成记录中目录下的路线。
/// 账户与 WPF（上游统筹设计 5.6）相同：计划指定账户 → 本实例当前账户 → 账户列表第一个；完成记录与日切时区都按该账户。
/// </summary>
public sealed partial class TaskPlanPageViewModel
{
    /// <summary>账户 ID → 该账户的完成记录索引</summary>
    private Dictionary<string, PuloniaRouteCompletionIndex> _completions = new(StringComparer.Ordinal);

    /// <summary>账户目录</summary>
    private IReadOnlyList<PuloniaGenshinAccountProfile> _cooldownAccounts = [];

    /// <summary>本实例当前账户</summary>
    private string? _currentAccountId;

    private IPuloniaAccountService? AccountService => _services?.GetService(typeof(IPuloniaAccountService)) as IPuloniaAccountService;

    /// <summary>全部计划（含一条龙计划），供计划引用与作用域解析</summary>
    private Dictionary<string, PuloniaTaskPlan> _allPlans = new(StringComparer.Ordinal);

    /// <summary>CD 记录只看当前计划（含引用计划与目录引用）中的路线</summary>
    [ObservableProperty] private bool _cooldownsForPlanOnly = true;

    /// <summary>任务页签顶部的计划路线 CD 摘要；没有路线时为空</summary>
    [ObservableProperty] private string? _planCooldownSummary;

    [ObservableProperty] private string? _planCooldownSummaryTip;

    partial void OnCooldownsForPlanOnlyChanged(bool value) => _ = RefreshCooldownsAsync();

    /// <summary>切到“路线 CD”页签时重新读取，反映刚保存的节点设置与目录引用</summary>
    partial void OnSelectedTabChanged(int value)
    {
        if (value == 4) _ = RefreshCooldownsAsync();
    }

    /// <summary>由外到内的节点模型转为设置链，方案按 ID 关联已加载的设置方案</summary>
    internal IReadOnlyList<PuloniaSettingLevel> CreateSettingChain(IEnumerable<PuloniaTask> models)
        => models.Select(model => new PuloniaSettingLevel(model, model.Id,
            model.ProfileId is { } id ? Profiles.FirstOrDefault(p => p.Profile?.Id == id)?.Profile : null)).ToArray();

    private JToken? EffectiveCooldownSetting(IReadOnlyList<PuloniaTask> chain)
        => PuloniaParameterResolver.ResolveSettings(CreateSettingChain(chain)).Values
            .TryGetValue(PuloniaSettingCatalog.RouteCooldownKey, out var setting) ? setting : null;

    /// <summary>计划判定路线 CD 所按的账户 ID（与 WPF ResolveCooldownAccount 相同）；没有账户目录时为空字符串</summary>
    internal string GetCooldownScope(PuloniaTaskPlan plan)
    {
        if (_cooldownAccounts.Count == 0) return "";
        return Find(plan.AccountId) ?? Find(_currentAccountId) ?? _cooldownAccounts[0].Id;
        string? Find(string? id) => id is not null && _cooldownAccounts.Any(a => a.Id == id) ? id : null;
    }

    /// <summary>账户的显示文字（与 WPF PuloniaCooldownText.Account 相同）</summary>
    internal string DescribeScope(string accountId)
        => "CD 按账户：" + (_cooldownAccounts.FirstOrDefault(a => a.Id == accountId)?.Name ?? (accountId.Length == 0 ? "（没有账户）" : accountId));

    private PuloniaRouteCompletionIndex IndexOf(string accountId) => _completions.GetValueOrDefault(accountId) ?? PuloniaRouteCompletionIndex.Empty;

    private int OffsetOf(string accountId) => _cooldownAccounts.FirstOrDefault(a => a.Id == accountId) is { } account
        ? PuloniaServerTime.GetUtcOffsetMinutes(account.ServerTimeZone) : PuloniaServerTime.GetUtcOffsetMinutes();

    /// <summary>地图追踪叶子按有效设置解析的 CD 规则；不是路线、路径含变量或设置无效时为空</summary>
    internal PuloniaRouteRefreshRule? ResolveCooldownPolicy(IReadOnlyList<PuloniaTask> chain)
    {
        var task = chain[^1];
        if (task.TaskType != "pathing" || task.Source is not null) return null;
        var path = ResolveCooldownPath(task.Path, task.TaskType);
        if (task.Resource is null && path is null) return null;
        try
        {
            return PuloniaRouteCooldown.Resolve(path, task.Resource,
                new JObject { [PuloniaRouteCooldown.ParameterName] = EffectiveCooldownSetting(chain)?.DeepClone() });
        }
        catch (Exception ex) when (ex is PuloniaTaskValidationException or ArgumentException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>目录引用在完成记录中的位置（路线身份前缀）与该组的路线 CD 设置</summary>
    private (string Prefix, bool Recursive, JToken? Setting)? ResolveCooldownDirectory(IReadOnlyList<PuloniaTask> chain)
    {
        if (chain[^1].Source is not { Kind: "directory", TaskType: "pathing" } source) return null;
        string identity;
        if (source.Resource is { } resource)
            identity = PuloniaRouteCooldown.GetFileIdentity(null, resource);
        else if (ResolveCooldownPath(source.Path, null) is { } path)
            identity = PuloniaRouteCooldown.GetFileIdentity(path, null);
        else
            return null;
        return (identity.TrimEnd('/') + "/", source.Recursive, EffectiveCooldownSetting(chain));
    }

    /// <summary>叶子相对能力资源目录，目录引用（taskType 为空）相对程序目录；含路径变量时无法预测</summary>
    private string? ResolveCooldownPath(string? path, string? taskType)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('{') || path.Contains('}')) return null;
        try
        {
            var baseDirectory = taskType is null ? null : _tasks?.Definitions.FirstOrDefault(d => d.TaskType == taskType)?.ResourceBaseDirectory;
            return Path.GetFullPath(path, Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>汇总：启用路线数、会跳过数、未识别数、最早可执行时间、是否含目录引用</summary>
    internal readonly record struct CooldownSummary(int Total, int Skipped, int Unmatched, DateTimeOffset? EarliestNext, bool HasDirectory)
    {
        public static CooldownSummary Empty => default;

        public static CooldownSummary operator +(CooldownSummary a, CooldownSummary b) => new(a.Total + b.Total, a.Skipped + b.Skipped,
            a.Unmatched + b.Unmatched, a.EarliestNext is null ? b.EarliestNext : b.EarliestNext is null || a.EarliestNext < b.EarliestNext ? a.EarliestNext : b.EarliestNext,
            a.HasDirectory || b.HasDirectory);
    }

    private PuloniaTaskPlan? CurrentPlanForChain(IReadOnlyList<PuloniaTask> chain)
        => SelectedPlan?.Plan is { } plan && ReferenceEquals(plan.RootTask, chain[0]) ? plan
            : _allPlans.Values.FirstOrDefault(p => p.RootTask.Id == chain[0].Id) ?? SelectedPlan?.Plan;

    /// <summary>节点的 CD 标记与说明（与 WPF 相同的文字）</summary>
    internal (string? Text, string? Tip) DescribeCooldown(IReadOnlyList<PuloniaTask> chain)
    {
        var plan = CurrentPlanForChain(chain);
        var scope = plan is null ? GetDefaultScope() : GetCooldownScope(plan);
        var now = DateTimeOffset.UtcNow;
        var offset = OffsetOf(scope);
        var task = chain[^1];
        if (task.TaskType == "pathing" && task.Source is null)
        {
            if (ResolveCooldownPolicy(chain) is not { } policy) return (null, null);
            var status = PuloniaRouteCooldown.GetStatus(policy, IndexOf(scope).Find(policy.FileIdentity), now, offset);
            var text = status.State switch
            {
                PuloniaRouteCooldownState.CoolingDown when status.NextEligibleAt is { } next => "CD 中 · " + Remaining(next - now),
                PuloniaRouteCooldownState.Completed => "已完成",
                PuloniaRouteCooldownState.Unmatched => "未识别 CD",
                _ => null,
            };
            if (text is null) return (null, null);
            var lines = new List<string>
            {
                $"路线 CD（{DescribeScope(scope)}）：{text}",
                $"规则：{policy.MaterialName ?? "未识别"} · {policy.Description}"
            };
            if (status.LastSuccessAt is { } last) lines.Add($"上次成功：{last.ToLocalTime():yyyy-MM-dd HH:mm}");
            if (status.NextEligibleAt is { } eligible) lines.Add($"下次可执行：{eligible.ToLocalTime():yyyy-MM-dd HH:mm}");
            lines.Add(status.State == PuloniaRouteCooldownState.Unmatched
                ? "路径中没有已知的材料或类别目录，成功后不会跳过；可在共享设置“路线 CD”中选择自定义周期。"
                : "本次运行会跳过；可在“路线 CD”页签清除记录。");
            return (text, string.Join("\n", lines));
        }

        if (task.TaskType != "group") return (null, null);
        var summary = SummarizeNode(chain, scope, now, offset, new HashSet<string>(StringComparer.Ordinal), countDisabled: true);
        return DescribeGroup(summary);
    }

    private CooldownSummary SummarizeNode(IReadOnlyList<PuloniaTask> chain, string scope, DateTimeOffset now, int offset,
        HashSet<string> visiting, bool countDisabled = false)
    {
        var task = chain[^1];
        if (!countDisabled && !task.IsEnabled) return CooldownSummary.Empty;
        // 计划引用：沿用被引用计划自身的汇总（含其作用域），循环引用按空汇总
        if (task.Source is { Kind: "plan", PlanId: { } planId })
            return _allPlans.TryGetValue(planId, out var target) ? SummarizePlan(target, now, offset, visiting) : CooldownSummary.Empty;
        // 目录引用：不展开目录，只按该组规则判定账本中目录下的记录
        if (ResolveCooldownDirectory(chain) is { } directory) return SummarizeDirectory(directory, scope, now, offset);
        if (task.TaskType == "pathing" && task.Source is null)
        {
            if (ResolveCooldownPolicy(chain) is not { } policy) return CooldownSummary.Empty;
            var status = PuloniaRouteCooldown.GetStatus(policy, IndexOf(scope).Find(policy.FileIdentity), now, offset);
            return new CooldownSummary(1, status.IsSkipped ? 1 : 0, status.State == PuloniaRouteCooldownState.Unmatched ? 1 : 0,
                status.IsSkipped ? status.NextEligibleAt : null, false);
        }

        var total = CooldownSummary.Empty;
        foreach (var child in task.Children) total += SummarizeNode([.. chain, child], scope, now, offset, visiting);
        return total;
    }

    private CooldownSummary SummarizePlan(PuloniaTaskPlan plan, DateTimeOffset now, int offset, HashSet<string> visiting)
    {
        if (!visiting.Add(plan.Id)) return CooldownSummary.Empty;
        var scope = GetCooldownScope(plan);
        var total = CooldownSummary.Empty;
        foreach (var child in plan.RootTask.Children) total += SummarizeNode([plan.RootTask, child], scope, now, OffsetOf(scope), visiting);
        visiting.Remove(plan.Id);
        return total;
    }

    private CooldownSummary SummarizeDirectory((string Prefix, bool Recursive, JToken? Setting) directory, string scope, DateTimeOffset now, int offset)
    {
        var skipped = 0;
        DateTimeOffset? earliest = null;
        foreach (var completion in IndexOf(scope).EnumerateUnder(directory.Prefix, directory.Recursive))
        {
            PuloniaRouteCooldownStatus status;
            try
            {
                status = PuloniaRouteCooldown.GetStatus(PuloniaRouteCooldown.ResolveFromFileIdentity(completion.Route, directory.Setting), completion, now, offset);
            }
            catch (PuloniaTaskValidationException)
            {
                continue;
            }

            if (!status.IsSkipped) continue;
            skipped++;
            if (status.NextEligibleAt is { } next && (earliest is null || next < earliest)) earliest = next;
        }

        return new CooldownSummary(0, skipped, 0, earliest, true);
    }

    private static (string? Text, string? Tip) DescribeGroup(CooldownSummary summary)
    {
        var text = summary.Skipped > 0
            ? summary.HasDirectory ? $"{summary.Skipped} 项 CD 中" : $"{summary.Skipped}/{summary.Total} CD 中"
            : summary.Unmatched > 0 ? $"{summary.Unmatched} 项未识别 CD" : null;
        if (text is null) return (null, null);
        var lines = new List<string>
        {
            $"下属启用路线：{(summary.HasDirectory ? "已知 " : "")}{summary.Total} 条，本次会跳过 {summary.Skipped} 条（CD 中或一次性已完成），未识别刷新规则 {summary.Unmatched} 条"
        };
        if (summary.EarliestNext is { } next) lines.Add($"最早可执行：{next.ToLocalTime():yyyy-MM-dd HH:mm}");
        if (summary.HasDirectory) lines.Add("目录引用不展开，只计入仍在 CD 的记录。");
        return (text, string.Join("\n", lines));
    }

    /// <summary>刷新计划摘要（任务页签顶部），与 WPF DescribeDocumentSummary 相同</summary>
    private void RefreshPlanCooldownSummary()
    {
        if (SelectedPlan?.Plan is not { } plan)
        {
            (PlanCooldownSummary, PlanCooldownSummaryTip) = (null, null);
            return;
        }

        var summary = SummarizePlan(plan, DateTimeOffset.UtcNow, OffsetOf(GetCooldownScope(plan)), new HashSet<string>(StringComparer.Ordinal));
        if (summary.Total == 0 && !summary.HasDirectory)
        {
            (PlanCooldownSummary, PlanCooldownSummaryTip) = (null, null);
            return;
        }

        var parts = new List<string>();
        if (summary.Skipped > 0) parts.Add($"本次 {summary.Skipped} 项会因 CD 跳过");
        if (summary.Unmatched > 0) parts.Add($"{summary.Unmatched} 项未识别刷新规则");
        PlanCooldownSummary = "路线 CD：" + (parts.Count == 0 ? "启用的路线均可执行" : string.Join(" · ", parts));
        PlanCooldownSummaryTip = $"按{DescribeScope(GetCooldownScope(plan))}与各节点当前规则计算；运行到每个节点时会重新判定。"
                                 + (summary.EarliestNext is { } next ? $"\n最早可执行：{next.ToLocalTime():yyyy-MM-dd HH:mm}" : "")
                                 + (summary.HasDirectory ? "\n目录引用只计入仍在 CD 的记录。" : "");
    }

    private static string Remaining(TimeSpan span) => span.TotalDays >= 1 ? $"{(int)span.TotalDays} 天 {span.Hours} 小时"
        : span.TotalHours >= 1 ? $"{(int)span.TotalHours} 小时 {span.Minutes} 分" : $"{Math.Max(1, (int)Math.Ceiling(span.TotalMinutes))} 分钟";

    /// <summary>计划会用到的路线记录范围（含引用计划与目录引用），按各计划自身的账户；供 CD 记录按计划筛选（参数：账户 ID、完成记录）</summary>
    internal Func<string, PuloniaRouteCompletion, bool> CreatePlanCooldownFilter(PuloniaTaskPlan plan)
    {
        var keys = new HashSet<(string Scope, string Key)>();
        var directories = new List<(string Scope, string Prefix, bool Recursive)>();
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        Visit(plan);
        return (accountId, entry) => keys.Contains((accountId, entry.Route)) || directories.Any(d => d.Scope == accountId
            && entry.Route.StartsWith(d.Prefix, StringComparison.Ordinal)
            && (d.Recursive || entry.Route.IndexOf('/', d.Prefix.Length) < 0));

        void Visit(PuloniaTaskPlan current)
        {
            if (!visiting.Add(current.Id)) return;
            var scope = GetCooldownScope(current);
            foreach (var child in current.RootTask.Children) Walk([current.RootTask, child], scope);
        }

        void Walk(List<PuloniaTask> chain, string scope)
        {
            var task = chain[^1];
            if (task.Source is { Kind: "plan", PlanId: { } planId })
            {
                if (_allPlans.TryGetValue(planId, out var target)) Visit(target);
                return;
            }

            if (ResolveCooldownDirectory(chain) is { } directory)
            {
                directories.Add((scope, directory.Prefix, directory.Recursive));
                return;
            }

            if (ResolveCooldownPolicy(chain) is { } policy) keys.Add((scope, policy.FileIdentity));
            foreach (var child in task.Children) Walk([.. chain, child], scope);
        }
    }

    /// <summary>没有计划时按本实例当前账户（没有时为账户列表第一个）</summary>
    private string GetDefaultScope()
        => _currentAccountId is { } id && _cooldownAccounts.Any(a => a.Id == id) ? id : _cooldownAccounts.FirstOrDefault()?.Id ?? "";

    /// <summary>读取账户目录、本实例当前账户、各账户完成记录与全部计划</summary>
    private async Task LoadCooldownContextAsync()
    {
        if (_store is null) return;
        try
        {
            if (AccountService is { } accounts)
            {
                var settings = await accounts.LoadAccountsAsync();
                _cooldownAccounts = settings.Accounts.ToList();
                _currentAccountId = await accounts.GetCurrentAccountIdAsync(accounts.CurrentInstanceKey);
                var completions = new Dictionary<string, PuloniaRouteCompletionIndex>(StringComparer.Ordinal);
                foreach (var account in _cooldownAccounts)
                {
                    try
                    {
                        completions[account.Id] = await accounts.GetRouteCompletionIndexAsync(account.Id);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        // 一个账户的完成记录读不出时不影响其他账户
                        _log.Warning(e, "读取账户 {Account} 的完成记录失败", account.Id);
                        completions[account.Id] = _completions.GetValueOrDefault(account.Id) ?? PuloniaRouteCompletionIndex.Empty;
                    }
                }

                _completions = completions;
            }

            var plans = await _store.ListPlansWithFailuresAsync();
            _allPlans = plans.Items.ToDictionary(p => p.Id, StringComparer.Ordinal);
        }
        catch (Exception e)
        {
            _log.Warning(e, "读取账户、完成记录或计划目录失败");
        }
    }

    private void RefreshCooldownBadges()
    {
        foreach (var row in Nodes) row.RefreshCooldown();
        RefreshPlanCooldownSummary();
    }
}
