using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.GameTask.AutoPathing;

namespace BetterGenshinImpact.ViewModel.Pages.ScriptControl;

/// <summary>
/// 调度器配置组的复制 / 重命名 / 根据文件夹更新与移除 / 项目所在目录（WPF ScriptControlViewModel 与 Avalonia 调度器页共用）
/// </summary>
public static class ScriptGroupEditing
{
    public const string PromptText = "请输入配置组名称";
    public const string CopyTitle = "复制配置组";
    public const string RenameTitle = "重命名配置组";
    public const string ExistsTitle = "配置组已存在";
    public const string RemovedTitle = "脚本配置移除成功";

    public static string CopyConflictText(string name) => $"配置组 {name} 已经存在，复制失败";

    public static string RenameConflictText(string name) => $"配置组 {name} 已经存在，重命名失败";

    public static string UpdatedText(int added) => $"增加了{added}个地图追踪任务";

    public static string RemovedByFolderText(string folder) => $"已移除 {folder} 下的所有关联配置";

    /// <summary>深拷贝配置组并改名（与 WPF 相同：序列化后再读回）</summary>
    public static ScriptGroup Copy(ScriptGroup item, string newName)
    {
        var copy = ScriptGroup.FromJson(JsonSerializer.Serialize(item));
        copy.Name = newName;
        return copy;
    }

    /// <summary>重命名配置组文件并更新名称；若是连续执行的起点则同步 NextScriptGroupName。调用方随后保存配置组</summary>
    public static void Rename(string folder, ScriptGroup item, string newName, AllConfig config)
    {
        File.Move(Path.Combine(folder, $"{item.Name}.json"), Path.Combine(folder, $"{newName}.json"));
        item.Name = newName;
        if (item.NextFlag)
        {
            config.NextScriptGroupName = item.Name;
        }
    }

    /// <summary>地图追踪任务文件夹（FolderName 里的反斜杠在非 Windows 上换成目录分隔符，Windows 上不变）</summary>
    public static string PathingFolder(string folderName) =>
        Path.Combine(PathingPaths.PathJsonPath, folderName.Replace('\\', Path.DirectorySeparatorChar));

    /// <summary>
    /// 根据文件夹更新：地图追踪项目按其所在文件夹重新列出 json（已有的保留原项目与设置，新增的按文件名添加），其它类型保持原样
    /// </summary>
    public static List<ScriptGroupProject> UpdateByFolder(IReadOnlyList<ScriptGroupProject> oldProjects)
    {
        var projects = new List<ScriptGroupProject>();
        var folderNames = new List<string>();
        foreach (var project in oldProjects)
        {
            if (project.Type == "Pathing")
            {
                if (!folderNames.Contains(project.FolderName))
                {
                    folderNames.Add(project.FolderName);
                    var dirPath = PathingFolder(project.FolderName);
                    var files = Directory.Exists(dirPath) ? Directory.GetFiles(dirPath, "*.json", SearchOption.TopDirectoryOnly) : [];
                    foreach (var jsonFile in files)
                    {
                        var fileInfo = new FileInfo(jsonFile);
                        var oldProject = oldProjects.FirstOrDefault(item => item.Name == fileInfo.Name);
                        projects.Add(oldProject ?? ScriptGroupProject.BuildPathingProject(fileInfo.Name, project.FolderName));
                    }
                }
            }
            else
            {
                projects.Add(project);
            }
        }

        return projects;
    }

    /// <summary>与 item 同一文件夹的项目（根据文件夹移除）</summary>
    public static List<ScriptGroupProject> SameFolder(ScriptGroup group, ScriptGroupProject item) =>
        group.Projects.Where(p => p.FolderName == item.FolderName).ToList();

    /// <summary>项目所在目录（JS / 键鼠 / 地图追踪），其它类型返回 null</summary>
    public static string? ProjectFolder(ScriptGroupProject item) => item.Type switch
    {
        "Javascript" => Path.Combine(Global.ScriptPath(), item.FolderName),
        "KeyMouse" => Global.Absolute(@"User\KeyMouseScript"),
        "Pathing" => PathingFolder(item.FolderName),
        _ => null,
    };
}
