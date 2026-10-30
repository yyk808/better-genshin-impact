using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Pages.SoloTaskSettings;
using BetterGenshinImpact.ViewModel.SoloTaskSettings;

// 独立任务的专属设置界面声明（原为任务类上的 SoloTaskSettingsView 特性，任务类不再引用界面类型）
[assembly: SoloTaskSettingsMap(typeof(BetterGenshinImpact.GameTask.AutoBoss.AutoBossTask), typeof(AutoBossSettingsView), typeof(AutoBossSettingsViewModel))]
[assembly: SoloTaskSettingsMap(typeof(BetterGenshinImpact.GameTask.AutoDomain.AutoDomainTask), typeof(AutoDomainSettingsView), typeof(AutoDomainSettingsViewModel))]
[assembly: SoloTaskSettingsMap(typeof(BetterGenshinImpact.GameTask.AutoLeyLineOutcrop.AutoLeyLineOutcropTask), typeof(AutoLeyLineOutcropSettingsView), typeof(AutoLeyLineOutcropSettingsViewModel))]
[assembly: SoloTaskSettingsMap(typeof(BetterGenshinImpact.GameTask.AutoStygianOnslaught.AutoStygianOnslaughtTask), typeof(AutoStygianOnslaughtSettingsView), typeof(AutoStygianOnslaughtSettingsViewModel))]
[assembly: SoloTaskSettingsMap(typeof(BetterGenshinImpact.GameTask.Common.Job.ClaimDailyRewardsTask), typeof(ClaimDailyRewardsSettingsView), typeof(ClaimDailyRewardsSettingsViewModel))]
[assembly: SoloTaskSettingsMap(typeof(BetterGenshinImpact.GameTask.Common.Job.GoToCraftingBenchTask), typeof(CraftCondensedResinSettingsView), typeof(CraftCondensedResinSettingsViewModel))]
[assembly: SoloTaskSettingsMap(typeof(BetterGenshinImpact.GameTask.Common.Job.GoToSereniteaPotTask), typeof(SereniteaPotSettingsView), typeof(SereniteaPotSettingsViewModel))]
[assembly: SoloTaskSettingsMap(typeof(BetterGenshinImpact.GameTask.ResinPlan.ResinPlanTask), typeof(ResinPlanSettingsView), typeof(ResinPlanSettingsViewModel))]
