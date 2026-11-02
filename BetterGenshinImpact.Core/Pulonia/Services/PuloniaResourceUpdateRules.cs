using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 任务资源更新的界面无关规则：哪些节点可更新、当前使用版本、是否有更新、提示文字与确认后的写回。
/// WPF 任务计划页与 Avalonia 共用，资源读取与固定仍由 <see cref="PuloniaTaskResourceVersionService"/> 完成。
/// </summary>
public static class PuloniaResourceUpdateRules
{
    /// <summary>有外部资源、可以更新任务使用版本的节点：地图追踪、JS、录制回放与目录来源的分组。</summary>
    public static bool CanUpdate(PuloniaTask task)
        => task.TaskType is "pathing" or "javascript" or "keymouse" || task is { TaskType: "group", Source.Kind: "directory" };

    /// <summary>任务当前使用（已确认）的资源版本；目录来源记在来源上。</summary>
    public static string? ApprovedVersion(PuloniaTask task)
        => task.Source?.Kind == "directory" ? task.Source.Version : task.ResourceVersion;

    /// <summary>节点引用的仓库来源；旧路径资源为 null。</summary>
    public static ScriptResourceReference? Reference(PuloniaTask task) => task.Resource ?? task.Source?.Resource;

    /// <summary>检查到的当前版本与任务使用版本不同（尚未固定也算有更新）。</summary>
    public static bool HasUpdate(PuloniaTask task, string? currentVersion)
        => CanUpdate(task) && currentVersion is not null && currentVersion != ApprovedVersion(task);

    /// <summary>任务副本：后台读取资源期间界面编辑不会改变本轮读取的任务。</summary>
    public static PuloniaTask Snapshot(PuloniaTask task) => PuloniaTaskJson.Read<PuloniaTask>(PuloniaTaskJson.Write(task));

    /// <summary>只捕获影响资源定位和比较的字段，参数或名称修改不会把无关 IO 结果误判为新版本。</summary>
    public static string CheckKey(PuloniaTask task)
        => PuloniaTaskJson.Write(new { task.TaskType, task.Path, task.Resource, task.Source, task.ResourceVersion });

    /// <summary>检查失败时节点上显示的原因；仓库来源的资源仍可使用已确认缓存。</summary>
    public static string CheckErrorText(PuloniaTask task, Exception ex)
        => Reference(task) is not null
            ? (ex is FileNotFoundException or DirectoryNotFoundException ? "来源中已移除：" : "检查失败：") + ex.Message + "；仍可使用已确认缓存。"
            : ex.Message;

    /// <summary>优先展示资源声明版本变化；缺少可读版本时只说明内容已经变化。</summary>
    public static string FormatVersionChange(PuloniaTaskResourceVersionState state)
    {
        if (!string.IsNullOrWhiteSpace(state.ApprovedDeclaredVersion)
            && !string.IsNullOrWhiteSpace(state.CurrentDeclaredVersion)
            && state.ApprovedDeclaredVersion != state.CurrentDeclaredVersion)
            return $"版本：{state.ApprovedDeclaredVersion} → {state.CurrentDeclaredVersion}";
        return "内容已经变化，但资源没有可显示的版本号变化。";
    }

    /// <summary>声明版本仅在两端都有值且文字不同时显示箭头，不参与更新判断。</summary>
    public static bool HasDeclaredVersionChange(string? approvedDeclared, string? currentDeclared)
        => !string.IsNullOrWhiteSpace(approvedDeclared) && !string.IsNullOrWhiteSpace(currentDeclared)
           && !string.Equals(approvedDeclared, currentDeclared, StringComparison.Ordinal);

    /// <summary>把仓库声明版本限制为适合任务树标签的长度，完整值仍通过悬停展示。</summary>
    public static string FormatDeclaredVersion(string? version)
        => version is { Length: > 14 } ? version[..13] + "…" : version ?? string.Empty;

    /// <summary>树标签文字（不以颜色作为唯一提示）。</summary>
    public static string BadgeText(PuloniaTask task, string? checkError, int referencedUpdates, bool hasUpdate,
        string? approvedDeclared, string? currentDeclared)
        => checkError is not null
            ? checkError.StartsWith("来源中已移除：", StringComparison.Ordinal) ? "来源中已移除"
            : Reference(task) is not null ? "检查失败" : "资源不可用"
            : referencedUpdates > 0 ? $"{referencedUpdates} 项有更新"
            : hasUpdate && HasDeclaredVersionChange(approvedDeclared, currentDeclared)
                ? $"有更新 {FormatDeclaredVersion(approvedDeclared)} → {FormatDeclaredVersion(currentDeclared)}"
            : "有更新";

    /// <summary>标签的具体原因，文件更新必须用户确认后才影响新运行。</summary>
    public static string BadgeToolTip(string? checkError, int referencedUpdates, bool hasUpdate,
        string? approvedDeclared, string? currentDeclared)
        => checkError
           ?? (referencedUpdates > 0
               ? $"引用计划中有 {referencedUpdates} 项任务资源可更新。"
               : hasUpdate && HasDeclaredVersionChange(approvedDeclared, currentDeclared)
                   ? $"任务使用版本 {approvedDeclared}，本机仓库版本 {currentDeclared}。可右键更新此任务资源，或在计划卡片上更新整个计划。"
                   : "本机仓库内容与任务使用版本不同；可右键更新此任务资源，或在计划卡片上更新整个计划。");

    /// <summary>写回确认后的版本与固定的仓库引用（目录来源写在来源上），参数、开关与执行历史不变。</summary>
    public static void ApplyVersion(PuloniaTask task, string version, ScriptResourceReference? reference)
    {
        if (task.Source?.Kind == "directory")
        {
            task.Source.Version = version;
            if (reference is not null) task.Source.Resource = reference;
        }
        else
        {
            task.ResourceVersion = version;
            if (reference is not null) task.Resource = reference;
        }
    }

    /// <summary>一组任务实际引用的仓库（去重、保持首次出现顺序），供一键同步。</summary>
    public static string[] ReferencedRepositoryIds(IEnumerable<PuloniaTask> tasks)
        => tasks.Select(Reference).Where(r => r is not null).Select(r => r!.RepositoryId).Distinct(StringComparer.Ordinal).ToArray();
}
