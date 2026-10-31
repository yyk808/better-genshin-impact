using System.Diagnostics;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Host.Common;
using BetterGenshinImpact.Host.Common.Config;

namespace BetterGenshinImpact.Avalonia.Services;

/// <summary>软件日志文件：按 Linux 宿主设置启用（log 目录，与 Windows 版相同），并提供“打开日志 / 打开日志目录”</summary>
internal static class LogFiles
{
    public static string Folder => Global.Absolute("log");

    /// <summary>按设置启用或关闭日志文件，设置改变时立即生效</summary>
    public static void Attach(LinuxHostConfig config)
    {
        Apply(config);
        config.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LinuxHostConfig.LogFileEnabled))
            {
                Apply(config);
            }
        };
    }

    private static void Apply(LinuxHostConfig config)
    {
        if (config.LogFileEnabled)
        {
            HostLogging.FileLog.Enable(Folder);
        }
        else
        {
            HostLogging.FileLog.Disable();
        }
    }

    /// <summary>用系统默认程序打开最新的日志文件；没有日志文件时打开日志目录</summary>
    public static void OpenLatest() => Open(HostLogging.FileLog.LatestFile() ?? EnsureFolder());

    public static void OpenFolder() => Open(EnsureFolder());

    private static string EnsureFolder()
    {
        Directory.CreateDirectory(Folder);
        return Folder;
    }

    private static void Open(string path) =>
        Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { path }, UseShellExecute = false });
}
