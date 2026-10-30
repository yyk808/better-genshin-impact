using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;

namespace BetterGenshinImpact.Core.UnitTest.Script;

/// <summary>
/// 配置组调度（两个宿主共用）：“连续任务从此组开始”与“下一次从此项目执行”的标记读写
/// </summary>
public sealed class ScriptGroupScheduleTests
{
    private static ScriptGroup Group(string name, params string[] projects)
    {
        var g = new ScriptGroup { Name = name };
        var i = 1;
        foreach (var p in projects)
        {
            var project = ScriptGroupProject.BuildShellProject(p);
            project.Index = i++;
            g.AddProject(project);
        }

        return g;
    }

    [Fact]
    public void GetNextScriptGroups_NoFlag_ReturnsAll()
    {
        var config = new AllConfig();
        var groups = new List<ScriptGroup> { Group("a"), Group("b"), Group("c") };
        Assert.Equal(["a", "b", "c"], ScriptGroupSchedule.GetNextScriptGroups(groups, config).Select(g => g.Name));
    }

    [Fact]
    public void SetGroupNextFlag_ThenGetNext_StartsFromFlaggedGroupAndClears()
    {
        var config = new AllConfig();
        var groups = new List<ScriptGroup> { Group("a"), Group("b"), Group("c") };
        ScriptGroupSchedule.SetGroupNextFlag(groups, groups[1], config);
        Assert.Equal("b", config.NextScriptGroupName);
        Assert.True(groups[1].NextFlag);

        var next = ScriptGroupSchedule.GetNextScriptGroups(groups, config);
        Assert.Equal(["b", "c"], next.Select(g => g.Name));
        Assert.False(groups[1].NextFlag);
        Assert.Equal(string.Empty, config.NextScriptGroupName);
    }

    [Fact]
    public void SetProjectNextFlag_ThenGetNextProjects_SkipsEarlierProjectsAndClearsSchedule()
    {
        var config = new AllConfig();
        var group = Group("g", "p1", "p2", "p3");
        ScriptGroupSchedule.SetProjectNextFlag(group, group.Projects[1], config);
        Assert.Single(config.NextScheduledTask);
        Assert.True(group.Projects[1].NextFlag);

        var projects = ScriptGroupSchedule.GetNextProjects(group, config);
        Assert.Equal(3, projects.Count);
        Assert.True(projects[0].SkipFlag);
        Assert.False(projects[1].SkipFlag);
        Assert.Empty(config.NextScheduledTask);
        Assert.All(group.Projects, p => Assert.False(p.NextFlag ?? false));
    }

    [Fact]
    public void SetProjectNextFlag_ReplacesPreviousEntryOfSameGroup()
    {
        var config = new AllConfig();
        var group = Group("g", "p1", "p2");
        ScriptGroupSchedule.SetProjectNextFlag(group, group.Projects[0], config);
        ScriptGroupSchedule.SetProjectNextFlag(group, group.Projects[1], config);
        Assert.Single(config.NextScheduledTask);
        Assert.Equal(2, config.NextScheduledTask[0].Item2);
        Assert.False(group.Projects[0].NextFlag ?? false);
    }
}
