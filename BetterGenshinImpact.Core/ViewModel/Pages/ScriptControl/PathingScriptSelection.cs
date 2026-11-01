using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Script.Group;

namespace BetterGenshinImpact.ViewModel.Pages.ScriptControl;

/// <summary>地图追踪任务选择面板中的一个路线文件</summary>
/// <param name="FullPath">完整路径</param>
/// <param name="Folder">相对 User/AutoPathing 的目录（根目录为“.”，与 WPF 相同）</param>
/// <param name="FileName">文件名</param>
public sealed record PathingScriptEntry(string FullPath, string Folder, string FileName)
{
    /// <summary>显示用的相对路径</summary>
    public string Display => Folder == "." ? FileName : Folder.Replace('\\', '/') + "/" + FileName;
}

/// <summary>
/// 调度器“添加地图追踪任务”：列出路线文件、按名称筛选、排除已选择过的目录，并建立配置组项目（WPF 选择面板与 Avalonia 选择对话框共用）
/// </summary>
public static class PathingScriptSelection
{
    public const string Title = "请选择需要添加的地图追踪任务";

    /// <summary>列出目录下全部路线文件（含子目录），按相对路径排序</summary>
    public static IReadOnlyList<PathingScriptEntry> List(string root)
    {
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Select(f => new PathingScriptEntry(f, Path.GetRelativePath(root, Path.GetDirectoryName(f)!), Path.GetFileName(f)))
            .OrderBy(e => e.Display, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>筛选：名称或目录包含关键字（不区分大小写）；可排除配置组中已有项目所在的目录（含其子目录）</summary>
    public static IEnumerable<PathingScriptEntry> Filter(IEnumerable<PathingScriptEntry> entries, string? keyword, IEnumerable<string?>? excludeFolders)
    {
        var skip = (excludeFolders ?? []).Where(f => !string.IsNullOrEmpty(f)).Select(f => Normalize(f!)).Distinct().ToList();
        foreach (var e in entries)
        {
            if (!string.IsNullOrWhiteSpace(keyword) && !e.Display.Contains(keyword.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            var folder = Normalize(e.Folder);
            if (skip.Any(s => folder == s || folder.StartsWith(s + "/", StringComparison.Ordinal))) continue;
            yield return e;
        }
    }

    /// <summary>为选中的路线文件建立配置组项目（目录取相对 User/AutoPathing 的路径，与 WPF 相同）</summary>
    public static ScriptGroupProject Build(string fullPath, string root)
    {
        var file = new FileInfo(fullPath);
        return ScriptGroupProject.BuildPathingProject(file.Name, Path.GetRelativePath(root, file.Directory!.FullName));
    }

    private static string Normalize(string folder) => folder.Replace('\\', '/').Trim('/');
}
