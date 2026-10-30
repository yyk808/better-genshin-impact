using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation;
using BetterGenshinImpact.GameTask.GetGridIcons;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.GameTask.SoloTask.Execution;
using BetterGenshinImpact.GameTask.UseRedeemCode;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.Core.UnitTest.GameTask;

/// <summary>
/// 独立任务参数工厂（两个宿主共用）：策略检查失败、用户取消输入时返回 null，且失败原因经提示回调给出
/// </summary>
public sealed class SoloTaskParamFactoryTests
{
    private static readonly SoloTaskRegistry Registry = new([typeof(CoreServices).Assembly]);

    private sealed class Inputs : ISoloTaskParamInputs
    {
        public List<(DialogKind Kind, string Message)> Notes { get; } = [];
        public List<string>? RedeemCodes { get; init; }
        public string? FightStrategyName { get; init; }
        public string? TcgStrategyName { get; init; }
        public int AutoWoodRoundNum => 0;
        public int AutoWoodDailyMaxCount => 2000;
        public bool SaveScreenshotOnKeyTick => false;
        public int AutoDomainRoundNum => 0;
        public InventoryCountComparisonTarget InventoryCountComparisonTarget => InventoryCountComparisonTarget.CharacterDevelopmentItems;
        public List<string>? PromptForRedeemCodes() => RedeemCodes;

        public bool GetTcgStrategy(out string content) =>
            TaskStrategyLookup.GetTcgStrategy(TcgStrategyName, (k, m) => Notes.Add((k, m)), out content);

        public bool GetFightStrategy(out string path) => GetFightStrategy(FightStrategyName!, out path);

        public bool GetFightStrategy(string strategyName, out string path) =>
            TaskStrategyLookup.GetFightStrategy(strategyName, (k, m) => Notes.Add((k, m)), out path);
    }

    [Fact]
    public void ParseRedeemCodesTrimsAndDropsEmptyLines()
    {
        Assert.Equal(["ABC", "DEF"], SoloTaskParamFactory.ParseRedeemCodes(" ABC \r\n\r\n DEF\n  \n"));
        Assert.Empty(SoloTaskParamFactory.ParseRedeemCodes("   "));
        Assert.Empty(SoloTaskParamFactory.ParseRedeemCodes(null));
    }

    [Fact]
    public void CancelledRedeemInputGivesNoParam()
    {
        var inputs = new Inputs { RedeemCodes = null };
        Assert.Null(SoloTaskParamFactory.Create(UseRedemptionCodeTask.TaskId, new AllConfig(), Registry, inputs));
    }

    [Theory]
    [InlineData(AutoFightEntryTask.TaskId)]
    [InlineData(AutoDomainTask.TaskId)]
    public void EmptyFightStrategyWarnsAndGivesNoParam(string taskId)
    {
        var inputs = new Inputs { FightStrategyName = "" };
        Assert.Null(SoloTaskParamFactory.Create(taskId, new AllConfig(), Registry, inputs));
        var note = Assert.Single(inputs.Notes);
        Assert.Equal(DialogKind.Warning, note.Kind);
        Assert.Equal("请先在下拉列表配置中选择战斗策略！", note.Message);
    }

    [Fact]
    public void MissingBossStrategyFileReportsError()
    {
        var config = new AllConfig();
        config.AutoBossConfig.StrategyName = "不存在的策略-" + Guid.NewGuid().ToString("N");
        var inputs = new Inputs();
        Assert.Null(SoloTaskParamFactory.Create(AutoBossTask.TaskId, config, Registry, inputs));
        var note = Assert.Single(inputs.Notes);
        Assert.Equal(DialogKind.Error, note.Kind);
        Assert.Equal("当前选择的自动战斗策略文件不存在", note.Message);
    }

    [Fact]
    public void TcgStrategyChecksNameThenFile()
    {
        var empty = new Inputs { TcgStrategyName = "" };
        Assert.Null(SoloTaskParamFactory.Create(AutoGeniusInvokationTask.TaskId, new AllConfig(), Registry, empty));
        Assert.Equal((DialogKind.Warning, "请先选择策略"), Assert.Single(empty.Notes));

        var missing = new Inputs { TcgStrategyName = "不存在的策略-" + Guid.NewGuid().ToString("N") };
        Assert.Null(SoloTaskParamFactory.Create(AutoGeniusInvokationTask.TaskId, new AllConfig(), Registry, missing));
        Assert.Equal((DialogKind.Error, "策略文件不存在"), Assert.Single(missing.Notes));
    }
}
