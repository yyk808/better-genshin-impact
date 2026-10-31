using System;
using System.Linq;

namespace BetterGenshinImpact.Model;

/// <summary>
/// 快捷键页“按功能名称筛选”的匹配规则（WPF HotKeyPage 与 Avalonia 设置-快捷键共用）：
/// 去除首尾空白，空关键字不筛选；任一名称（当前语言名称或中文原名、分组名）包含关键字即匹配，忽略大小写
/// </summary>
public static class HotkeyFilter
{
    public const string Placeholder = "按功能名称筛选";
    public const string EmptyText = "没有匹配的功能";

    /// <summary>规范化关键字（去除首尾空白，null 视为空）</summary>
    public static string Normalize(string? keyword) => keyword?.Trim() ?? string.Empty;

    /// <summary>任一名称包含关键字即匹配（关键字应已规范化；空关键字总是匹配）</summary>
    public static bool IsMatch(string keyword, params string?[] names) =>
        keyword.Length == 0 || names.Any(n => n?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true);
}
