using System.Diagnostics;
using System.Text;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Platform.Linux.Desktop;

/// <summary>
/// 独立任务期间临时关闭桌面通知横幅，任务结束后恢复。
/// 优先 GNOME gsettings（show-banners）；其次 swaync / mako。无可用后端或本来已关闭时返回 null。
/// </summary>
public sealed class LinuxNotificationSuppressionService : ISoloTaskNotificationSuppressor
{
    /// <summary>运行一条外部命令：参数、可选 stdin；返回退出码与 stdout。</summary>
    public delegate (int ExitCode, string Stdout) CommandRunner(string fileName, IReadOnlyList<string> args);

    private readonly IConfigService _configService;
    private readonly ILogger<LinuxNotificationSuppressionService> _logger;
    private readonly CommandRunner _run;

    public LinuxNotificationSuppressionService(
        IConfigService configService,
        ILogger<LinuxNotificationSuppressionService> logger)
        : this(configService, logger, DefaultRun)
    {
    }

    /// <summary>测试用：注入命令执行器。</summary>
    public LinuxNotificationSuppressionService(
        IConfigService configService,
        ILogger<LinuxNotificationSuppressionService> logger,
        CommandRunner run)
    {
        _configService = configService;
        _logger = logger;
        _run = run;
    }

    public IDisposable? TrySuppressForSoloTask()
    {
        if (!_configService.Get().OtherConfig.SuppressWindowsNotificationsDuringSoloTask)
        {
            return null;
        }

        try
        {
            return TryGsettings()
                ?? TrySwaync()
                ?? TryMako();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "开启 Linux 桌面勿扰失败，独立任务将继续运行");
            return null;
        }
    }

    private IDisposable? TryGsettings()
    {
        // gsettings get 在 schema 不存在时非 0；缺命令时也非 0
        var (getCode, getOut) = _run("gsettings", ["get", "org.gnome.desktop.notifications", "show-banners"]);
        if (getCode != 0)
        {
            return null;
        }

        var original = getOut.Trim();
        // 已是 false：用户本来勿扰，不取得所有权
        if (string.Equals(original, "false", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var (setCode, _) = _run("gsettings", ["set", "org.gnome.desktop.notifications", "show-banners", "false"]);
        if (setCode != 0)
        {
            _logger.LogWarning("gsettings 关闭通知横幅失败（exit {Code}）", setCode);
            return null;
        }

        _logger.LogInformation("独立任务运行期间已关闭 GNOME 通知横幅（gsettings show-banners）");
        return new RestoreScope(
            "gsettings",
            () => _run("gsettings", ["set", "org.gnome.desktop.notifications", "show-banners", original]),
            original,
            _logger);
    }

    private IDisposable? TrySwaync()
    {
        var (getCode, getOut) = _run("swaync-client", ["-D"]);
        if (getCode != 0)
        {
            return null;
        }

        // swaync-client -D 输出 true/false（是否已勿扰）
        var alreadyDnd = getOut.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        if (alreadyDnd)
        {
            return null;
        }

        var (dnCode, _) = _run("swaync-client", ["-dn"]);
        if (dnCode != 0)
        {
            return null;
        }

        _logger.LogInformation("独立任务运行期间已开启 swaync 勿扰");
        return new RestoreScope(
            "swaync",
            () => _run("swaync-client", ["-df"]),
            "off",
            _logger);
    }

    private IDisposable? TryMako()
    {
        // makoctl mode：列出当前模式；无 mako 时失败
        var (listCode, listOut) = _run("makoctl", ["mode"]);
        if (listCode != 0)
        {
            return null;
        }

        if (listOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(m => string.Equals(m, "dnd", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(m, "do-not-disturb", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var (addCode, _) = _run("makoctl", ["mode", "-a", "dnd"]);
        if (addCode != 0)
        {
            return null;
        }

        _logger.LogInformation("独立任务运行期间已开启 mako dnd 模式");
        return new RestoreScope(
            "mako",
            () => _run("makoctl", ["mode", "-r", "dnd"]),
            "dnd",
            _logger);
    }

    private static (int ExitCode, string Stdout) DefaultRun(string fileName, IReadOnlyList<string> args)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
            };
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }

            using var process = Process.Start(psi);
            if (process is null)
            {
                return (127, string.Empty);
            }

            var stdout = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return (124, stdout);
            }

            return (process.ExitCode, stdout);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            return (127, string.Empty);
        }
    }

    private sealed class RestoreScope : IDisposable
    {
        private readonly string _backend;
        private readonly Func<(int ExitCode, string Stdout)> _restore;
        private readonly string _label;
        private readonly ILogger _logger;
        private int _disposed;

        public RestoreScope(
            string backend,
            Func<(int ExitCode, string Stdout)> restore,
            string label,
            ILogger logger)
        {
            _backend = backend;
            _restore = restore;
            _label = label;
            _logger = logger;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                var (code, _) = _restore();
                if (code == 0)
                {
                    _logger.LogInformation("已恢复 Linux 桌面通知设置（{Backend} → {Label}）", _backend, _label);
                }
                else
                {
                    _logger.LogWarning("恢复 Linux 桌面通知设置失败（{Backend} exit {Code}）", _backend, code);
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "恢复 Linux 桌面通知设置异常（{Backend}）", _backend);
            }
        }
    }
}
