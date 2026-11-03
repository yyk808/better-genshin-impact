using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Host.Common.Storage;

/// <summary>
/// 一次性迁移：早期 Linux 版的悬浮小窗与脚本仓库网页没有指定数据目录，WebKitGTK 把网页数据写在默认目录
/// <c>$XDG_DATA_HOME/&lt;进程名&gt;</c>（通常为 ~/.local/share/dotnet 或 ~/.local/share/bgi-avalonia）。
/// 新目录 Data/WebKit/Guide 不存在时，在创建网页之前把旧目录中 WebKitGTK 的数据复制到 Data/WebKit/Guide/data。
/// 只复制 WebKitGTK 自己的条目（同一目录可能还有其他程序的文件，例如 dotnet 的 runfile），旧目录保持不变；缓存不复制（需要时重新下载）
/// </summary>
public static class LinuxWebDataMigration
{
    /// <summary>WebKitGTK 网页数据目录中的条目（Cookie、本地存储、IndexedDB、HSTS、媒体密钥等）</summary>
    public static readonly IReadOnlyList<string> WebKitDataEntries =
        ["cookies.sqlite", "localstorage", "databases", "storage", "mediakeys", "hsts-storage.sqlite", "deviceidhashsalts", "itp", "serviceworkers"];

    /// <summary>旧默认目录的候选（按进程名；XDG_DATA_HOME 未设置时为 ~/.local/share）</summary>
    public static IEnumerable<string> LegacyDefaultDataDirectories()
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(dataHome))
        {
            dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        var names = new[] { Path.GetFileNameWithoutExtension(Environment.ProcessPath), AppDomain.CurrentDomain.FriendlyName }
            .Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.Ordinal);
        return names.Select(n => Path.Combine(dataHome, n!));
    }

    /// <summary>迁移结果</summary>
    public enum Result
    {
        /// <summary>新目录已存在，不迁移</summary>
        TargetExists,

        /// <summary>没有旧数据</summary>
        NoLegacyData,

        /// <summary>已复制</summary>
        Copied,

        /// <summary>复制失败（已记日志，删除未完成的新目录，下次启动再试）</summary>
        Failed,
    }

    /// <summary>悬浮小窗：从旧默认目录迁移到 Data/WebKit/Guide</summary>
    public static Result MigrateGuide(ILogger logger) =>
        Migrate(LegacyDefaultDataDirectories(), LinuxWebDataPaths.Guide, logger);

    /// <summary>
    /// 迁移：<paramref name="targetProfile"/> 不存在、且某个候选旧目录中有 WebKitGTK 数据时，把这些条目复制到 targetProfile/data。
    /// 任何异常都只记日志并返回 <see cref="Result.Failed"/>，不影响启动
    /// </summary>
    public static Result Migrate(IEnumerable<string> legacyDirectories, string targetProfile, ILogger logger)
    {
        try
        {
            if (Directory.Exists(targetProfile))
            {
                return Result.TargetExists;
            }

            var legacy = legacyDirectories.FirstOrDefault(d => WebKitDataEntries.Any(e => Path.Exists(Path.Combine(d, e))));
            if (legacy is null)
            {
                return Result.NoLegacyData;
            }

            var target = LinuxWebDataPaths.Data(targetProfile);
            try
            {
                Directory.CreateDirectory(target);
                foreach (var entry in WebKitDataEntries)
                {
                    var source = Path.Combine(legacy, entry);
                    if (Directory.Exists(source)) CopyDirectory(source, Path.Combine(target, entry));
                    else if (File.Exists(source)) File.Copy(source, Path.Combine(target, entry));
                }
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "迁移旧网页数据失败：{Legacy} → {Target}，已跳过，下次启动再试", legacy, target);
                try { Directory.Delete(targetProfile, recursive: true); } catch (Exception) { /* 尽力而为 */ }
                return Result.Failed;
            }

            logger.LogInformation("已把旧网页数据 {Legacy} 复制到 {Target}（旧目录保留）", legacy, target);
            return Result.Copied;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "检查旧网页数据失败，已跳过");
            return Result.Failed;
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            // 符号链接不跟随，避免复制到目录之外
            if (new DirectoryInfo(dir).LinkTarget is not null) continue;
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }
    }
}
