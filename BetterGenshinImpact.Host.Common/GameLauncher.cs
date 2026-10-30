using System.Diagnostics;
using BetterGenshinImpact.Host.Common.Config;
using Serilog;

namespace BetterGenshinImpact.Host.Common;

/// <summary>
/// 按 <see cref="LinuxHostConfig"/> 的命令启动游戏（Wine / Proton / Steam 等）。
/// 命令经 <c>/bin/sh -c</c> 执行，<c>{game}</c> 替换为加单引号的游戏路径；附加环境变量每行一个 KEY=VALUE，值中开头的 <c>~/</c> 展开为主目录
/// </summary>
public static class GameLauncher
{
    public static string BuildCommand(LinuxHostConfig config, string? gamePath = null)
    {
        var path = string.IsNullOrWhiteSpace(gamePath) ? config.GamePath : gamePath;
        var command = config.LaunchCommand.Trim();
        if (command.Length == 0)
        {
            throw new InvalidOperationException("未设置启动游戏的命令");
        }

        if (command.Contains("{game}", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("启动命令中有 {game}，但未设置游戏路径");
            }

            command = command.Replace("{game}", ShellQuote(ExpandHome(path)), StringComparison.Ordinal);
        }

        return command;
    }

    public static IReadOnlyDictionary<string, string> ParseEnvironment(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                throw new FormatException($"环境变量格式应为 KEY=VALUE：{line}");
            }

            result[line[..eq].Trim()] = ExpandHome(line[(eq + 1)..].Trim());
        }

        return result;
    }

    /// <summary>启动游戏进程（不等待窗口）</summary>
    public static Process Launch(LinuxHostConfig config, ILogger log, string? gamePath = null)
    {
        var command = BuildCommand(config, gamePath);
        var path = string.IsNullOrWhiteSpace(gamePath) ? config.GamePath : gamePath;
        var workingDirectory = !string.IsNullOrWhiteSpace(config.WorkingDirectory)
            ? ExpandHome(config.WorkingDirectory)
            : !string.IsNullOrWhiteSpace(path) && Path.GetDirectoryName(ExpandHome(path)) is { Length: > 0 } dir && Directory.Exists(dir)
                ? dir
                : Environment.CurrentDirectory;
        var psi = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(command);
        foreach (var (key, value) in ParseEnvironment(config.Environment))
        {
            psi.Environment[key] = value;
        }

        log.Information("启动游戏：{Command}（工作目录 {Dir}）", command, workingDirectory);
        return Process.Start(psi) ?? throw new InvalidOperationException("无法启动游戏进程");
    }

    internal static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    internal static string ExpandHome(string value) =>
        value == "~" ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        : value.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), value[2..])
            : value;
}
