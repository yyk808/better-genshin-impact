using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace BetterGenshinImpact.Platform.Linux;

/// <summary>
/// 通过命令行工具访问桌面剪贴板（Wayland：wl-copy / wl-paste；X11：xclip 或 xsel）。
/// 供无界面宿主使用；Avalonia 宿主应使用 TopLevel.Clipboard。找不到工具时退回进程内剪贴板
/// </summary>
public sealed class CommandLineClipboard : IClipboardService
{
    /// <summary>一组剪贴板命令：写入（文本经标准输入）、读取（文本输出到标准输出）、清空</summary>
    public sealed record Commands(string[] Set, string[] Get, string[] Clear);

    public static readonly Commands WlClipboard = new(["wl-copy"], ["wl-paste", "--no-newline"], ["wl-copy", "--clear"]);
    public static readonly Commands XClip = new(["xclip", "-selection", "clipboard", "-in"], ["xclip", "-selection", "clipboard", "-out"], ["xclip", "-selection", "clipboard", "-in", "/dev/null"]);
    public static readonly Commands XSel = new(["xsel", "--clipboard", "--input"], ["xsel", "--clipboard", "--output"], ["xsel", "--clipboard", "--clear"]);

    private readonly Commands? _commands;
    private readonly InMemoryClipboard _fallback = new();
    private readonly TimeSpan _timeout;

    public CommandLineClipboard(Commands? commands = null, TimeSpan? timeout = null)
    {
        _commands = commands ?? Detect();
        _timeout = timeout ?? TimeSpan.FromSeconds(3);
    }

    /// <summary>实际使用的命令；为 null 表示使用进程内剪贴板</summary>
    public Commands? Active => _commands;

    /// <summary>按会话类型与 PATH 中可用的工具选择命令</summary>
    public static Commands? Detect()
    {
        var wayland = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        if (wayland && OnPath("wl-copy") && OnPath("wl-paste"))
        {
            return WlClipboard;
        }

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            if (OnPath("xclip")) return XClip;
            if (OnPath("xsel")) return XSel;
        }

        return null;
    }

    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_commands is null)
        {
            _fallback.SetText(text);
            return;
        }

        // wl-copy / xclip 会留在后台持有选区，因此只等待写入完成，不读取它们的输出
        Run(_commands.Set, text, readOutput: false);
    }

    public string? GetText()
    {
        if (_commands is null)
        {
            return _fallback.GetText();
        }

        var (code, output) = Run(_commands.Get, null, readOutput: true);
        return code == 0 ? output : null;
    }

    public void Clear()
    {
        if (_commands is null)
        {
            _fallback.Clear();
            return;
        }

        Run(_commands.Clear, null, readOutput: false);
    }

    private (int Code, string Output) Run(string[] command, string? input, bool readOutput)
    {
        var psi = new ProcessStartInfo(command[0])
        {
            RedirectStandardInput = input is not null,
            RedirectStandardOutput = readOutput,
            RedirectStandardError = false,
            UseShellExecute = false,
            StandardInputEncoding = input is not null ? new UTF8Encoding(false) : null,
            StandardOutputEncoding = readOutput ? Encoding.UTF8 : null,
        };
        for (var i = 1; i < command.Length; i++)
        {
            psi.ArgumentList.Add(command[i]);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"无法启动剪贴板命令 {command[0]}");
        if (input is not null)
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }

        var output = readOutput ? process.StandardOutput.ReadToEnd() : string.Empty;
        if (!process.WaitForExit(_timeout))
        {
            // 写入命令可能常驻以持有选区（例如 xsel 未 fork 时），这里不强制结束
            return (0, output);
        }

        return (process.ExitCode, output);
    }

    private static bool OnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return false;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(Path.Combine(dir, name))) return true;
        }

        return false;
    }
}
