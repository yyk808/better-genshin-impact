using System;
using System.Collections.Generic;
using System.IO;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Core.Script.Group;

/// <summary>
/// User/ScriptGroup 下配置组文件的读写（原 ScriptControlViewModel 中的 ReadScriptGroup / SaveScriptGroupFile，供两个宿主共用）
/// </summary>
public static class ScriptGroupStore
{
    /// <summary>配置组文件夹</summary>
    public static string Folder => Global.Absolute(@"User\ScriptGroup");

    /// <summary>配置组文件路径</summary>
    public static string FilePath(string groupName) => Path.Combine(Folder, $"{groupName}.json");

    /// <summary>
    /// 读取全部配置组：按配置根恢复项目与配置组的“从此执行”标记，按 Index 排序。
    /// 单个文件读取失败时调用 onFileError 并跳过该文件；文件夹读取失败时异常向外抛出
    /// </summary>
    public static List<ScriptGroup> ReadAll(AllConfig rootConfig, Action<string, Exception>? onFileError = null)
    {
        Directory.CreateDirectory(Folder);
        var files = Directory.GetFiles(Folder, "*.json");
        List<ScriptGroup> groups = [];
        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var group = ScriptGroup.FromJson(json);
                ScriptGroupSchedule.SetProjectNextFlagFromSchedule(group, rootConfig);
                if (group.Name == rootConfig.NextScriptGroupName)
                {
                    group.NextFlag = true;
                }

                groups.Add(group);
            }
            catch (Exception e)
            {
                onFileError?.Invoke(file, e);
            }
        }

        // 按index排序
        groups.Sort((a, b) => a.Index.CompareTo(b.Index));
        return groups;
    }

    /// <summary>把配置组写入文件（原子替换），失败时抛出异常</summary>
    public static void Save(ScriptGroup scriptGroup)
    {
        Directory.CreateDirectory(Folder);
        scriptGroup.WriteToFileAtomically(FilePath(scriptGroup.Name));
    }

    /// <summary>删除配置组文件</summary>
    public static void Delete(string groupName)
    {
        File.Delete(FilePath(groupName));
    }
}
