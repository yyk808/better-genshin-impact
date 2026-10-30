using System.Diagnostics;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notifier.Interface;
using SixLabors.ImageSharp;

namespace BetterGenshinImpact.Host.Common.Notification;

/// <summary>
/// Linux 系统通知（freedesktop Notifications）：优先 notify-send，没有时用 gdbus 调用 org.freedesktop.Notifications.Notify。
/// 两者都不可用、没有会话总线或调用失败时静默（只在第一次失败时记一条调试日志），不影响其他通知渠道
/// </summary>
public sealed class FreedesktopNotifier : INotifier
{
    private static readonly Lazy<string?> NotifySend = new(() => FindExecutable("notify-send"));
    private static readonly Lazy<string?> GDBus = new(() => FindExecutable("gdbus"));
    private int _failureLogged;

    public string Name => "系统通知";

    /// <summary>当前环境能否发送（找到 notify-send 或 gdbus）</summary>
    public static bool IsAvailable => NotifySend.Value != null || GDBus.Value != null;

    /// <summary>失败时的日志（可为空）</summary>
    public Action<string>? LogFailure { get; init; }

    public async Task SendAsync(BaseNotificationData data)
    {
        var title = "BetterGI";
        var body = data.Message ?? data.Event;
        string? imagePath = null;
        try
        {
            if (data.Screenshot != null)
            {
                imagePath = Path.Combine(Path.GetTempPath(), $"bgi-notify-{Guid.NewGuid():N}.png");
                await data.Screenshot.SaveAsPngAsync(imagePath);
            }

            var ok = NotifySend.Value is { } notifySend
                ? await RunAsync(notifySend, BuildNotifySendArgs(title, body, data.Event, imagePath))
                : GDBus.Value is { } gdbus && await RunAsync(gdbus, BuildGDBusArgs(title, body, imagePath));
            if (!ok)
            {
                LogOnce($"系统通知未发送（{(IsAvailable ? "调用失败，可能没有通知服务或会话总线" : "未找到 notify-send / gdbus")}）");
            }
        }
        catch (Exception e)
        {
            LogOnce("系统通知未发送：" + e.Message);
        }
        finally
        {
            if (imagePath != null)
            {
                // 通知服务读取图片需要时间，稍后删除临时文件
                _ = Task.Delay(TimeSpan.FromSeconds(60)).ContinueWith(_ =>
                {
                    try
                    {
                        File.Delete(imagePath);
                    }
                    catch (Exception)
                    {
                        // 临时目录中的残留文件不影响使用
                    }
                });
            }
        }
    }

    public static List<string> BuildNotifySendArgs(string title, string body, string? category, string? imagePath)
    {
        var args = new List<string> { "--app-name=BetterGI" };
        if (!string.IsNullOrEmpty(category))
        {
            args.Add("--hint=string:x-bgi-event:" + category);
        }

        if (imagePath != null)
        {
            args.Add("--hint=string:image-path:" + imagePath);
        }

        args.Add("--");
        args.Add(title);
        args.Add(body);
        return args;
    }

    public static List<string> BuildGDBusArgs(string title, string body, string? imagePath)
    {
        // Notify(app_name, replaces_id, app_icon, summary, body, actions, hints, expire_timeout)
        var hints = imagePath != null ? $"{{'image-path': <{GVariantString(imagePath)}>}}" : "{}";
        return
        [
            "call", "--session", "--dest", "org.freedesktop.Notifications", "--object-path", "/org/freedesktop/Notifications",
            "--method", "org.freedesktop.Notifications.Notify",
            "BetterGI", "0", "", GVariantString(title), GVariantString(body), "[]", hints, "-1",
        ];
    }

    /// <summary>GVariant 文本格式的字符串字面量（单引号，转义反斜杠与单引号）</summary>
    public static string GVariantString(string s) => "'" + s.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    private static async Task<bool> RunAsync(string file, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var process = Process.Start(psi);
        if (process is null)
        {
            return false;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill();
            }
            catch (Exception)
            {
                // 已退出
            }

            return false;
        }

        return process.ExitCode == 0;
    }

    private void LogOnce(string message)
    {
        if (Interlocked.Exchange(ref _failureLogged, 1) == 0)
        {
            LogFailure?.Invoke(message);
        }
    }

    private static string? FindExecutable(string name)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var path = Path.Combine(dir, name);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }
}
