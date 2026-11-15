using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Semver;

namespace BetterGenshinImpact.Core.Config;

public class Global
{
    public static string Version { get; } = Assembly.GetEntryAssembly()?.
        GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.
        InformationalVersion!;

    public static string StartUpPath { get; set; } = AppContext.BaseDirectory;


    /// <summary>
    /// Linux 用户可写数据根（User/、Data/、log/、Repos/）。
    /// 为空时这些路径仍落在 <see cref="StartUpPath"/>（Windows 与现有可写的开发/便携布局）。
    /// 由 <see cref="ApplyWritableRoot"/> 设置；安装到只读前缀（如 /usr/lib）时必须设置，以免配置和日志写失败。
    /// </summary>
    public static string? WritableRoot { get; set; }

    /// <summary>环境变量：覆盖可写数据根，优先于自动探测。</summary>
    public const string DataDirectoryVariable = "BGI_DATA_DIR";

    public static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static string Absolute(string relativePath)
    {
        // 代码中的相对路径大多按 Windows 写法使用反斜杠；在其它平台上换成目录分隔符（Windows 上不做任何改动）
        if (Path.DirectorySeparatorChar != '\\')
        {
            relativePath = relativePath.Replace('\\', Path.DirectorySeparatorChar);
        }

        var root = UsesWritableRoot(relativePath) && !string.IsNullOrEmpty(WritableRoot)
            ? WritableRoot
            : StartUpPath;
        return Path.Combine(root, relativePath);
    }


    /// <summary>
    /// User/、Data/、log/、Repos/ 属于用户数据与程序生成数据，安装到只读目录后应写到 <see cref="WritableRoot"/>。
    /// 素材、模型等仍按 <see cref="StartUpPath"/> 解析。Windows 上 <see cref="WritableRoot"/> 保持为空。
    /// </summary>
    public static bool UsesWritableRoot(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return false;
        }

        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        foreach (var name in new[] { "User", "Data", "log", "Repos" })
        {
            if (normalized.Equals(name, StringComparison.Ordinal)
                || normalized.StartsWith(name + "/", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 按命令行、环境变量或（仅 Linux 且程序目录不可写时）XDG 数据目录设置 <see cref="WritableRoot"/>，
    /// 并创建 User/Data/log/Repos；若安装目录带有默认 I18n，则在用户目录尚无该目录时复制一份。
    /// Windows 上不设置，保持原路径。
    /// </summary>
    public static void ApplyWritableRoot(string? dataDirectory, Action<string>? warn = null)
    {
        if (OperatingSystem.IsWindows())
        {
            WritableRoot = null;
            return;
        }

        var specified = FirstNonEmpty(dataDirectory, Environment.GetEnvironmentVariable(DataDirectoryVariable));
        if (string.IsNullOrEmpty(specified) && IsDirectoryWritable(StartUpPath))
        {
            WritableRoot = null;
            return;
        }

        var root = string.IsNullOrEmpty(specified) ? DefaultXdgDataDirectory() : Path.GetFullPath(specified);
        if (!root.EndsWith(Path.DirectorySeparatorChar) && !root.EndsWith(Path.AltDirectorySeparatorChar))
        {
            root += Path.DirectorySeparatorChar;
        }

        WritableRoot = root;
        Directory.CreateDirectory(WritableRoot);
        foreach (var name in new[] { "User", "Data", "log", "Repos" })
        {
            Directory.CreateDirectory(Path.Combine(WritableRoot, name));
        }

        SeedDefaultUserFiles(StartUpPath, WritableRoot, warn);
        if (string.IsNullOrEmpty(specified))
        {
            warn?.Invoke($"程序目录不可写，用户数据改到 {WritableRoot}");
        }
    }

    /// <summary>XDG 默认数据目录（$XDG_DATA_HOME/better-genshin-impact，未设置时为 ~/.local/share/better-genshin-impact）。</summary>
    public static string DefaultXdgDataDirectory()
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(dataHome))
        {
            dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        return Path.GetFullPath(Path.Combine(dataHome, "better-genshin-impact"));
    }

    internal static bool IsDirectoryWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".bgi-write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static readonly string[] SeedUserSkipNames = ["config.json", "linux-host.json", "Account", "AccountRecords"];

    private static void SeedDefaultUserFiles(string assets, string writable, Action<string>? warn)
    {
        try
        {
            var source = Path.Combine(assets, "User");
            var dest = Path.Combine(writable, "User");
            if (!Directory.Exists(source))
            {
                return;
            }

            CopyNewFiles(source, dest);
        }
        catch (Exception e)
        {
            warn?.Invoke($"复制默认用户文件失败：{e.Message}");
        }
    }

    /// <summary>只复制目标尚不存在的文件，升级或再次启动不会覆盖用户已改的配置与脚本。</summary>
    private static void CopyNewFiles(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
        {
            var name = Path.GetFileName(file);
            if (ShouldSkipSeededUserEntry(name))
            {
                continue;
            }

            var target = Path.Combine(dest, name);
            if (!File.Exists(target))
            {
                File.Copy(file, target);
            }
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            var name = Path.GetFileName(dir);
            if (ShouldSkipSeededUserEntry(name))
            {
                continue;
            }

            CopyNewFiles(dir, Path.Combine(dest, name));
        }
    }

    private static bool ShouldSkipSeededUserEntry(string name)
    {
        foreach (var skip in SeedUserSkipNames)
        {
            if (name.Equals(skip, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    public static string ScriptPath()
    {
        return Absolute("User\\JsScript");
    }

    public static string? ReadAllTextIfExist(string relativePath)
    {
        var path = Absolute(relativePath);
        if (File.Exists(path)) return File.ReadAllText(path);
        return null;
    }

    /// <summary>
    ///     新获取到的版本号与当前版本号比较，判断是否为新版本
    /// </summary>
    /// <param name="currentVersion">新获取到的版本</param>
    /// <returns></returns>
    public static bool IsNewVersion(string currentVersion)
    {
        return IsNewVersion(Version, currentVersion);
    }

    /// <summary>
    ///     新获取到的版本号与当前版本号比较，判断是否为新版本
    /// </summary>
    /// <param name="oldVersion">老版本</param>
    /// <param name="currentVersion">新获取到的版本</param>
    /// <returns>是否需要更新</returns>
    public static bool IsNewVersion(string oldVersion, string currentVersion)
    {
        try
        {
            var oldVersionX = SemVersion.Parse(oldVersion);
            var currentVersionX = SemVersion.Parse(currentVersion);

            if (currentVersionX.CompareSortOrderTo(oldVersionX) > 0)
                // 需要更新
                return true;
        }
        catch
        {
            ///
        }

        // 不需要更新
        return false;
    }

    public static void WriteAllText(string relativePath, string blackListJson)
    {
        var path = Absolute(relativePath);
        File.WriteAllText(path, blackListJson);
    }
}
