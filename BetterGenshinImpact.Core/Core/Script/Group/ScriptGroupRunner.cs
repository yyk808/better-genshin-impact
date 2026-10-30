using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Service.Interface;

namespace BetterGenshinImpact.Core.Script.Group;

/// <summary>
/// 在同一个顶层运行环境与取消令牌中连续执行配置组（原 ScriptControlViewModel.StartGroupsCore，供两个宿主共用）
/// </summary>
public static class ScriptGroupRunner
{
    /// <summary>
    /// 执行一次或循环执行配置组：按“下次从此组执行”标记取组，组内按“从此执行”标记取项目，组间间隔 2 秒
    /// </summary>
    /// <param name="scriptService">配置组项目的执行服务</param>
    /// <param name="runtime">所属顶层任务的运行环境</param>
    /// <param name="scriptGroups">要执行的配置组</param>
    /// <param name="loop">是否循环执行</param>
    /// <param name="rootConfig">配置根（读写“从此执行”标记）</param>
    /// <param name="ct">所属顶层任务的取消令牌</param>
    public static async Task RunGroupsAsync(IScriptService scriptService, GameRuntime runtime, List<ScriptGroup> scriptGroups, bool loop,
        AllConfig rootConfig, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var sg = ScriptGroupSchedule.GetNextScriptGroups(scriptGroups, rootConfig);
            foreach (var scriptGroup in sg)
            {
                ct.ThrowIfCancellationRequested();
                await scriptService.RunMulti(runtime, ScriptGroupSchedule.GetNextProjects(scriptGroup, rootConfig), scriptGroup.Name, ct);
                await Task.Delay(2000, ct);
            }

            if (!loop)
            {
                return;
            }
        }
    }
}
