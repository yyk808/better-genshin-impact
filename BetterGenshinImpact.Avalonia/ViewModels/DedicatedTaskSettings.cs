using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoFight.Strategy;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.ViewModel.SoloTaskSettings;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// 独立任务的专属参数编辑器（VM 在 Core，与 WPF 的 SoloTaskSettingsMappings 对应；视图见 Views/SoloTaskSettings）。
/// 尚未移植专属界面的任务返回 null，由宿主回退到按 Schema 生成的通用字段。
/// </summary>
public static class DedicatedTaskSettings
{
    private static readonly Dictionary<string, Type> ViewModelTypes = new(StringComparer.Ordinal)
    {
        [AutoDomainTask.TaskId] = typeof(AutoDomainSettingsViewModel),
        [GameTask.AutoLeyLineOutcrop.AutoLeyLineOutcropTask.TaskId] = typeof(AutoLeyLineOutcropSettingsViewModel),
        [GameTask.Common.Job.ClaimDailyRewardsTask.TaskId] = typeof(ClaimDailyRewardsSettingsViewModel),
        [GameTask.Common.Job.GoToCraftingBenchTask.TaskId] = typeof(CraftCondensedResinSettingsViewModel),
        [GameTask.Common.Job.GoToSereniteaPotTask.TaskId] = typeof(SereniteaPotSettingsViewModel),
        [GameTask.AutoBoss.AutoBossTask.TaskId] = typeof(AutoBossSettingsViewModel),
        [GameTask.AutoStygianOnslaught.AutoStygianOnslaughtTask.TaskId] = typeof(AutoStygianOnslaughtSettingsViewModel),
        [GameTask.ResinPlan.ResinPlanTask.TaskId] = typeof(ResinPlanSettingsViewModel),
    };

    /// <summary>按任务类型创建专属设置 VM；VM 构造函数接收当前有效参数，其余依赖从服务容器解析（可缺省）</summary>
    public static SoloTaskSettingsViewModel? TryCreate(string? taskType, JObject effectiveParameters, IServiceProvider? services)
    {
        if (PuloniaSoloTaskExecutor.ToTaskId(taskType) is not { } taskId || !ViewModelTypes.TryGetValue(taskId, out var vmType))
        {
            return null;
        }

        return (SoloTaskSettingsViewModel)ActivatorUtilities.CreateInstance(services ?? EmptyServices.Instance, vmType, effectiveParameters);
    }

    private sealed class EmptyServices : IServiceProvider
    {
        public static readonly EmptyServices Instance = new();
        public object? GetService(Type serviceType) => null;
    }
}

/// <summary>专属编辑器使用的选项列表（与 WPF 视图中的静态数据源对应）</summary>
public static class DedicatedTaskSettingsChoices
{
    private static IReadOnlyList<string>? _domainNames;

    public static IReadOnlyList<string> SundayChoices => AutoDomainSettingsViewModel.SundayChoices;

    public static IReadOnlyList<string> ArtifactStarChoices => AutoDomainSettingsViewModel.ArtifactStarChoices;

    public static IReadOnlyList<string> RunPreferences { get; } = ["适量刷取", "充分刷取"];

    /// <summary>战斗策略：自动选择 + User/AutoFight 下的策略文件（每次打开编辑器时重新扫描）</summary>
    public static IReadOnlyList<string> FightStrategies =>
        TaskSettingsPageViewModel.LoadStrategyNames(@"User\AutoFight", [CombatStrategyResolver.AutoSelectName]);

    /// <summary>可讨伐的首领（按地区顺序展开，与 WPF 的级联下拉相同的数据）</summary>
    public static IReadOnlyList<string> BossNames { get; } =
        GameTask.AutoBoss.AutoBossData.CountryToBosses.SelectMany(country => country.Value).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>可传送的秘境名称（tp.json）</summary>
    public static IReadOnlyList<string> DomainNames => _domainNames ??= LoadDomainNames();

    /// <summary>备选秘境：首项为空（不刷）</summary>
    public static IReadOnlyList<string> FallbackDomainNames => [""
        , ..DomainNames];

    private static IReadOnlyList<string> LoadDomainNames()
    {
        try
        {
            return MapLazyAssets.Get().DomainNameList.ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }
}
