using BetterGenshinImpact.Pulonia.Executors;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>任务节点类型的中文显示名称（任务计划页的任务列表使用，Windows 与 Linux 界面共用）</summary>
public static class PuloniaTaskTypeText
{
    public static string Get(PuloniaTask task) => task.Source?.Kind switch
    {
        "plan" => "计划引用",
        "directory" => "目录引用",
        _ => task.TaskType switch
        {
            "group" => "分组",
            "pathing" => "地图追踪",
            "javascript" => "JS 脚本",
            "keymouse" => "录制回放",
            "shell" => "Shell",
            "csharp" => "进程内 C#",
            _ when PuloniaSoloTaskExecutor.IsSoloTaskType(task.TaskType) => "独立任务",
            _ => task.TaskType
        }
    };
}
