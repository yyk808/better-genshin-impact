using System.Collections.Generic;

namespace BetterGenshinImpact.Core.Script.Group;

/// <summary>HTTP 权限提示的级别（界面据此着色）</summary>
public enum ScriptHttpInfoLevel
{
    Ok,
    Warning,
    Error,
}

/// <summary>
/// 配置组内单个项目的通用设置（执行周期、次数、状态、JS 通知与 HTTP 权限）的界面无关逻辑。
/// WPF ScriptGroupProjectEditor 与 Avalonia 项目设置对话框共用
/// </summary>
public static class ScriptGroupProjectSettings
{
    /// <summary>JS 通知/HTTP 权限下拉的选项</summary>
    public static readonly IReadOnlyList<KeyValuePair<bool, string>> EnableOptions =
    [
        new(true, "启用"),
        new(false, "禁用"),
    ];

    public static bool IsJsScript(ScriptGroupProject project) => project.Type == "Javascript";

    /// <summary>
    /// 设置 HTTP 权限。为了避免误用，不直接保存布尔值，而是保存当前允许地址列表的 Hash：
    /// 脚本作者更新时如果地址列表变化，Hash 不再匹配，权限自动失效
    /// </summary>
    public static void SetAllowJsHttp(ScriptGroupProject project, bool? value)
    {
        project.AllowJsHTTPHash = value == true ? project.GetHttpAllowedUrlsHash() : null;
    }

    /// <summary>设置 JS 通知权限；全局关闭 JS 通知时不可修改，返回 false</summary>
    public static bool TrySetAllowJsNotification(ScriptGroupProject project, bool globalEnabled, bool? value)
    {
        if (!globalEnabled)
        {
            return false;
        }

        project.AllowJsNotification = value;
        return true;
    }

    /// <summary>脚本声明的 HTTP 地址列表（项目未加载时先尝试加载）</summary>
    public static string[] GetHttpAllowedUrls(ScriptGroupProject project)
    {
        if (project.Project == null)
        {
            project.BuildScriptProjectRelation();
        }

        return project.Project?.Manifest?.HttpAllowedUrls ?? [];
    }

    /// <summary>HTTP 权限概要文字</summary>
    public static (string Text, ScriptHttpInfoLevel Level) GetHttpInfo(ScriptGroupProject project)
    {
        if (project.Project == null)
        {
            project.BuildScriptProjectRelation();
        }

        if (project.Project == null)
        {
            return ("当前脚本项目未加载", ScriptHttpInfoLevel.Error);
        }

        var urls = project.Project.Manifest?.HttpAllowedUrls ?? [];
        return urls.Length == 0
            ? ("当前脚本无需使用HTTP资源", ScriptHttpInfoLevel.Ok)
            : ($"当前脚本使用 {urls.Length} 个HTTP资源", ScriptHttpInfoLevel.Warning);
    }
}
