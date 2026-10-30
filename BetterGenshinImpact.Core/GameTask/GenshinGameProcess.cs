using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 原神进程名与启动参数的平台无关部分（从 SystemControl 拆出）
/// </summary>
public static class GenshinGameProcess
{
    /// <summary>内置候选进程名：国服、国际服、云原神</summary>
    public static IReadOnlyList<string> DefaultProcessNames { get; } =
        ["YuanShen", "GenshinImpact", "Genshin Impact Cloud Game", "Genshin Impact Cloud"];

    private const string ChildSessionGenshinStartArgs =
        "-popupwindow -screen-width 1920 -screen-height 1080";

    private static readonly Regex ChildSessionOverriddenArgumentRegex = new(
        @"(?<!\S)(?:-popupwindow|-screen-(?:width|height)(?:\s*=\s*(?:""[^""]*""|\S+)|\s+(?:""[^""]*""|(?!-)\S+))?)(?=\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// 候选进程名：内置名称 + 安装路径中的自定义 exe 名（不含扩展名，去重）。
    /// 安装路径既可以是 Windows 路径也可以是 Linux 路径
    /// </summary>
    public static List<string> CandidateProcessNames(string? installPath)
    {
        List<string> list = [.. DefaultProcessNames];
        try
        {
            if (!string.IsNullOrEmpty(installPath))
            {
                // 在 Linux 上 Path 不识别反斜杠，先统一分隔符
                var customName = Path.GetFileNameWithoutExtension(installPath.Replace('\\', '/'));
                if (!string.IsNullOrEmpty(customName) && !list.Contains(customName))
                {
                    list.Add(customName);
                }
            }
        }
        catch
        {
            /* ignore */
        }

        return list;
    }

    /// <summary>
    /// 启动参数：子会话实例时去掉用户配置里的窗口化 / 分辨率参数，改为固定的 1920x1080 无边框窗口
    /// </summary>
    public static string BuildStartArguments(string? configuredArguments, bool isChildSession)
    {
        var arguments = configuredArguments?.Trim() ?? string.Empty;
        if (!isChildSession)
        {
            return arguments;
        }

        arguments = ChildSessionOverriddenArgumentRegex.Replace(arguments, string.Empty).Trim();
        return string.IsNullOrEmpty(arguments)
            ? ChildSessionGenshinStartArgs
            : $"{arguments} {ChildSessionGenshinStartArgs}";
    }
}
