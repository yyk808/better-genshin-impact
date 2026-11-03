using BetterGenshinImpact.Host.Common.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterGenshinImpact.Avalonia;

internal sealed partial class UiTests
{
    /// <summary>
    /// 网页数据一次性迁移（--ui-test webdata-migrate，LinuxWebDataMigration）：新目录不存在时把旧默认目录中 WebKitGTK 的条目
    /// （Cookie、本地存储、IndexedDB 等）复制到新目录的 data 下，不复制同目录中其他程序的文件（如 dotnet 的 runfile），旧目录不变；
    /// 新目录已存在时不迁移；没有旧数据时不建新目录；复制失败时返回失败、删除未完成的新目录且不抛异常。
    /// 全部在临时目录中进行，不读写主目录与程序数据目录
    /// </summary>
    private Task WebDataMigrateAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            Check("网页数据迁移仅在 Linux 上测试", true);
            return Task.CompletedTask;
        }

        var root = Path.Combine(Path.GetTempPath(), $"bgi-webdata-migrate-{Environment.ProcessId}");
        var legacy = Path.Combine(root, "share", "dotnet");
        var other = Path.Combine(root, "share", "bgi-avalonia");
        var target = Path.Combine(root, "Data", "WebKit", "Guide");
        try
        {
            Directory.CreateDirectory(Path.Combine(legacy, "localstorage"));
            Directory.CreateDirectory(Path.Combine(legacy, "databases", "indexeddb", "v1"));
            Directory.CreateDirectory(Path.Combine(legacy, "runfile", "a-1"));
            File.WriteAllText(Path.Combine(legacy, "localstorage", "https_www.bilibili.com_0.localstorage"), "ls");
            File.WriteAllText(Path.Combine(legacy, "databases", "indexeddb", "v1", "db"), "idb");
            File.WriteAllText(Path.Combine(legacy, "cookies.sqlite"), "cookie");
            File.WriteAllText(Path.Combine(legacy, "hsts-storage.sqlite"), "hsts");
            File.WriteAllText(Path.Combine(legacy, "runfile", "a-1", "csc.rsp"), "dotnet");
            Directory.CreateDirectory(other);

            // 候选目录依次查找：第一个没有 WebKit 数据，第二个有
            var r1 = LinuxWebDataMigration.Migrate([other, legacy], target, NullLogger.Instance);
            var data = LinuxWebDataPaths.Data(target);
            Check("新目录不存在时复制旧数据", r1 == LinuxWebDataMigration.Result.Copied, r1.ToString());
            Check("复制 Cookie、本地存储、IndexedDB、HSTS",
                File.ReadAllText(Path.Combine(data, "cookies.sqlite")) == "cookie" && File.Exists(Path.Combine(data, "localstorage", "https_www.bilibili.com_0.localstorage"))
                && File.Exists(Path.Combine(data, "databases", "indexeddb", "v1", "db")) && File.Exists(Path.Combine(data, "hsts-storage.sqlite")));
            Check("不复制其他程序的文件（runfile）", !Directory.Exists(Path.Combine(data, "runfile")));
            Check("旧目录保持不变", File.Exists(Path.Combine(legacy, "cookies.sqlite")) && File.Exists(Path.Combine(legacy, "runfile", "a-1", "csc.rsp")));

            File.WriteAllText(Path.Combine(legacy, "cookies.sqlite"), "newer");
            var r2 = LinuxWebDataMigration.Migrate([legacy], target, NullLogger.Instance);
            Check("新目录已存在时不迁移", r2 == LinuxWebDataMigration.Result.TargetExists && File.ReadAllText(Path.Combine(data, "cookies.sqlite")) == "cookie", r2.ToString());

            Directory.Delete(target, recursive: true);
            var r3 = LinuxWebDataMigration.Migrate([other], target, NullLogger.Instance);
            Check("没有旧数据时不建新目录", r3 == LinuxWebDataMigration.Result.NoLegacyData && !Directory.Exists(target), r3.ToString());

            // 复制失败：旧文件不可读（chmod 000）
            var locked = Path.Combine(legacy, "localstorage", "https_www.bilibili.com_0.localstorage");
            File.SetUnixFileMode(locked, UnixFileMode.None);
            LinuxWebDataMigration.Result r4;
            try
            {
                r4 = LinuxWebDataMigration.Migrate([legacy], target, NullLogger.Instance);
            }
            finally
            {
                File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            Check("复制失败时返回失败、不抛异常并删除未完成的新目录（下次启动再试）", r4 == LinuxWebDataMigration.Result.Failed && !Directory.Exists(target), r4.ToString());
            var r5 = LinuxWebDataMigration.Migrate([legacy], target, NullLogger.Instance);
            Check("恢复后再次启动可完成迁移", r5 == LinuxWebDataMigration.Result.Copied, r5.ToString());
            Check("旧默认目录按进程名查找（XDG_DATA_HOME 或 ~/.local/share）",
                LinuxWebDataMigration.LegacyDefaultDataDirectories().Any(d => Path.GetFileName(d) == Path.GetFileNameWithoutExtension(Environment.ProcessPath)),
                string.Join(",", LinuxWebDataMigration.LegacyDefaultDataDirectories()));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        return Task.CompletedTask;
    }
}
