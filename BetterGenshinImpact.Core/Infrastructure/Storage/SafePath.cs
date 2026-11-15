using System;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Infrastructure.Storage;

/// <summary>
/// 路径安全校验：防止外部输入的相对路径借助绝对路径或 .. 越出指定的根目录。
/// </summary>
public static class SafePath
{
    /// <summary>
    /// 判断是否为安全的相对路径：非空、不是绝对路径、不含驱动器号和非法字符，且任何一段都不是 ".."
    /// </summary>
    /// <param name="relativePath">待检查的相对路径，分隔符可以是 / 或 \</param>
    public static bool IsSafeRelativePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathRooted(relativePath)
            || relativePath.Contains(':')
            || relativePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return false;
        }

        var segments = relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 && !segments.Any(IsParentSegment);
    }

    /// <summary>
    /// Windows 会去掉路径段末尾的点和空格，".. "、"..." 等都可能被解析为上级目录，统一视为上级目录段
    /// </summary>
    private static bool IsParentSegment(string segment)
    {
        var trimmed = segment.Trim();
        return trimmed.Length >= 2 && trimmed.All(c => c == '.');
    }

    /// <summary>
    /// 判断 path 是否位于 root 之内。两者都先规范化为完整路径再比较，不区分大小写
    /// </summary>
    /// <param name="root">根目录</param>
    /// <param name="path">待检查的路径</param>
    /// <param name="allowRoot">path 等于 root 本身时是否视为位于其中</param>
    public static bool IsUnder(string root, string path, bool allowRoot = false)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            return allowRoot;
        }

        return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 把相对路径拼接到根目录下并返回完整路径
    /// </summary>
    /// <param name="root">根目录</param>
    /// <param name="relativePath">外部提供的相对路径</param>
    /// <exception cref="ArgumentException">相对路径不安全，或拼接结果越出根目录</exception>
    public static string Combine(string root, string relativePath)
    {
        if (!IsSafeRelativePath(relativePath))
        {
            throw new ArgumentException($"不安全的相对路径：{relativePath}", nameof(relativePath));
        }

        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!IsUnder(root, fullPath))
        {
            throw new ArgumentException($"路径越出了目录 {root}：{relativePath}", nameof(relativePath));
        }

        return fullPath;
    }

    /// <summary>
    /// 判断路径是否位于程序目录或（若已设置）Linux 可写数据根之内。
    /// 安装到只读前缀后，User/Data/log/Repos 在 <see cref="Global.WritableRoot"/>，不再位于 StartUpPath。
    /// </summary>
    public static bool IsUnderManagedRoot(string path, bool allowRoot = false)
    {
        if (IsUnder(Global.StartUpPath, path, allowRoot))
        {
            return true;
        }

        return !string.IsNullOrEmpty(Global.WritableRoot) && IsUnder(Global.WritableRoot, path, allowRoot);
    }

    /// <summary>
    /// 相对程序目录或可写数据根的路径，供待清理记录等使用；解析时用 <see cref="ResolveManagedPath"/>。
    /// </summary>
    public static string GetManagedRelativePath(string path)
    {
        var full = Path.GetFullPath(path);
        if (!string.IsNullOrEmpty(Global.WritableRoot) && IsUnder(Global.WritableRoot, full, allowRoot: true))
        {
            return Path.GetRelativePath(Global.WritableRoot, full);
        }

        return Path.GetRelativePath(Global.StartUpPath, full);
    }

    /// <summary>把 <see cref="GetManagedRelativePath"/> 保存的相对路径还原为完整路径。</summary>
    public static string ResolveManagedPath(string relativePath) => Path.GetFullPath(Global.Absolute(relativePath));
}
