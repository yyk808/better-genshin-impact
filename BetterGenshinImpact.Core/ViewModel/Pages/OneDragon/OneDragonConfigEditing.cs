using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.ViewModel.Pages.OneDragon;

/// <summary>
/// 一条龙任务列表的新增规则（WPF OneDragonFlowViewModel 与 Avalonia 一条龙页共用）
/// </summary>
public static class OneDragonTaskListEditing
{
    /// <summary>新增任务弹窗标题</summary>
    public const string AddTitle = "选择增加的配置组（可多选）";

    /// <summary>名称（按逗号拆分后任一部分）是否为内置一条龙任务</summary>
    public static bool IsBuiltIn(string groupName, IEnumerable<string> builtInNames)
    {
        var names = groupName.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).ToList();
        var builtIn = builtInNames as ICollection<string> ?? builtInNames.ToList();
        return names.Any(builtIn.Contains);
    }

    /// <summary>
    /// 新增任务的插入位置：内置任务插在最后一个内置任务之后（没有则插在开头），配置组追加到末尾（返回 -1）
    /// </summary>
    public static int InsertIndex(IReadOnlyList<string> currentTaskNames, string groupName, IEnumerable<string> builtInNames)
    {
        var builtIn = builtInNames as ICollection<string> ?? builtInNames.ToList();
        if (!IsBuiltIn(groupName, builtIn))
        {
            return -1;
        }

        for (var i = currentTaskNames.Count - 1; i >= 0; i--)
        {
            if (builtIn.Contains(currentTaskNames[i]))
            {
                return i + 1;
            }
        }

        return 0;
    }

    /// <summary>单个任务添加成功的提示</summary>
    public static string AddedText(bool builtIn) => builtIn ? "一条龙任务添加成功" : "配置组添加成功";

    /// <summary>多个任务添加成功的提示</summary>
    public static string AddedManyText(int count) => count + " 个任务添加成功";

    /// <summary>生成与现有 ID 不重复的任务 ID</summary>
    public static string NewTaskId(IEnumerable<string> existingIds)
    {
        var existing = new HashSet<string>(existingIds);
        string id;
        do
        {
            id = Guid.NewGuid().ToString();
        } while (existing.Contains(id));
        return id;
    }
}

/// <summary>
/// 一条龙配置的新增/重命名/删除（文字与文件操作，WPF 与 Avalonia 共用）
/// </summary>
public static class OneDragonConfigEditing
{
    public const string DefaultName = "默认配置";
    public const string AddPrompt = "请输入一条龙配置名称";
    public const string AddTitle = "新增一条龙配置";
    public const string RenamePrompt = "请输入新的配置名称";
    public const string RenameTitle = "重命名配置";
    public const string DeleteTitle = "删除配置";
    public const string DeleteNoSelection = "请先选择要删除的配置";
    public const string RenameNoSelection = "请先选择要重命名的配置";
    public const string DeletedText = "配置删除成功";
    public const string DeleteFailedText = "删除配置时失败";
    public const string RenamedText = "配置重命名成功";
    public const string RenameFailedText = "重命名配置时失败";

    /// <summary>删除确认文字（长名称缩写为 前4…后4）</summary>
    public static string DeleteConfirmText(string name)
    {
        var display = name.Length > 14 ? $"{name[..4]}...{name[^4..]}" : name;
        return $"确定要删除配置「{display}」吗？";
    }

    /// <summary>新增时名称已存在的提示；可以新增时返回 null</summary>
    public static string? AddConflict(string name, IEnumerable<string> existingNames) =>
        existingNames.Contains(name) ? $"一条龙配置 {name} 已经存在，请勿重复添加" : null;

    /// <summary>重命名时名称已存在的提示；可以重命名时返回 null</summary>
    public static string? RenameConflict(string newName, IEnumerable<string> existingNames) =>
        existingNames.Contains(newName) ? $"配置名称「{newName}」已存在，请使用其他名称" : null;

    /// <summary>删除配置文件（不存在时忽略）</summary>
    public static void DeleteFile(string name)
    {
        var file = OneDragonFlowConfigStore.GetFilePath(name);
        if (File.Exists(file))
        {
            File.Delete(file);
        }
    }

    /// <summary>重命名：先以新名称写入，成功后删除旧文件</summary>
    public static void Rename(OneDragonFlowConfig config, string newName)
    {
        var oldName = config.Name;
        config.Name = newName;
        OneDragonFlowConfigStore.Write(config);
        DeleteFile(oldName);
    }
}
