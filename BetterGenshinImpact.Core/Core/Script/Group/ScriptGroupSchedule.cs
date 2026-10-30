using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Core.Script.Group;

/// <summary>
/// 配置组“下次从此执行”的调度（原 ScriptControlViewModel 中的静态方法，抽到 Core 供一条龙与两个宿主共用）
/// </summary>
public static class ScriptGroupSchedule
{
    /// <summary>按配置根中的 NextScheduledTask 标记配置组项目的 NextFlag</summary>
    public static void SetProjectNextFlagFromSchedule(ScriptGroup group, AllConfig rootConfig)
    {
        var nst = rootConfig.NextScheduledTask.Find(item => item.Item1 == group.Name);
        foreach (var item in group.Projects)
        {
            item.NextFlag = false;
            if (nst != default)
            {
                if (nst.Item2 == item.Index && nst.Item3 == item.FolderName && nst.Item4 == item.Name)
                {
                    item.NextFlag = true;
                }
            }
        }
    }

    /// <summary>
    /// 取本次要执行的项目：有“从此执行”标记时，标记之前的项目设置 SkipFlag，并清除调度记录与标记
    /// </summary>
    public static List<ScriptGroupProject> GetNextProjects(ScriptGroup group, AllConfig rootConfig)
    {
        SetProjectNextFlagFromSchedule(group, rootConfig);
        List<ScriptGroupProject> ls = new List<ScriptGroupProject>();
        if (group.Projects.Where(g => g.NextFlag ?? false).Count() > 0)
        {
            bool start = false;
            foreach (var item in group.Projects)
            {
                if (item.NextFlag ?? false)
                {
                    start = true;
                }

                if (!start)
                {
                    item.SkipFlag = true;
                }
                ls.Add(item);
            }

            if (!start)
            {
                ls.AddRange(group.Projects);
            }

            //拿出来后清空，和置状态
            if (start)
            {
                List<ValueTuple<string, int, string, string>> nextScheduledTask = rootConfig.NextScheduledTask;
                foreach (var item in nextScheduledTask)
                {
                    if (item.Item1 == group.Name)
                    {
                        nextScheduledTask.Remove(item);
                        break;
                    }
                }

                foreach (var item in group.Projects)
                {
                    item.NextFlag = false;
                }
            }

            return ls;
        }

        return group.Projects.Select(g => g).ToList();
    }
}
