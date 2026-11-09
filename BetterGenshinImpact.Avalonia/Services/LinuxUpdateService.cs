using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Media;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Infrastructure.Network;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Model.MirrorChyan;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Avalonia.Services;

/// <summary>
/// Linux 版检查更新（对照 WPF UpdateService / CheckUpdateWindow）：查询最新版本后弹出“发现新版本”对话框。
/// 与 Windows 的差异：不下载、不运行 BetterGI.update.exe / Windows 安装包；提供“打开下载页”
/// （xdg-open 稳定版 https://www.bettergi.com/download.html 或预览版 cnb releases）、“忽略此版本”、“取消”。
/// 版本查询与打开链接均可由界面自测替换，不联网。
/// </summary>
public sealed class LinuxUpdateService(
    ILogger<LinuxUpdateService> log,
    IHttpClientFactory httpClientFactory) : IUpdateService
{
    public const string NoticeUrl = "https://hui-config.oss-cn-hangzhou.aliyuncs.com/bgi/notice.json";
    public const string StableDownloadPageUrl = "https://www.bettergi.com/download.html";
    public const string AlphaReleasesUrl = "https://cnb.cool/bettergi/better-genshin-impact/-/releases";

    /// <summary>查询最新版本号（界面自测替换；默认走真实 HTTP）</summary>
    public static Func<UpdateOption, Task<string>>? FetchLatestVersion { get; set; }

    /// <summary>打开外链（默认 xdg-open）</summary>
    public static Action<string> OpenUrl { get; set; } = url =>
    {
        try
        {
            Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { url }, UseShellExecute = false });
        }
        catch
        {
            // 没有 xdg-open 时忽略
        }
    };

    /// <summary>当前“发现新版本”对话框（界面自测用；与 FluentMessageBox.Current 相同实例）</summary>
    public static FAContentDialog? CurrentDialog { get; private set; }

    public async Task CheckUpdateAsync(UpdateOption option)
    {
        try
        {
            var fetch = FetchLatestVersion ?? FetchLatestVersionCoreAsync;
            var newVersion = await fetch(option);
            if (string.IsNullOrWhiteSpace(newVersion))
            {
                if (option.Trigger == UpdateTrigger.Manual)
                {
                    await UiHost.Dialogs.ShowAsync("当前已是最新版本！", "检查更新", DialogKind.Information);
                }

                return;
            }

            if (!Global.IsNewVersion(newVersion))
            {
                if (option.Trigger == UpdateTrigger.Manual)
                {
                    await UiHost.Dialogs.ShowAsync("当前已是最新版本！", "检查更新", DialogKind.Information);
                }

                return;
            }

            var config = ConfigHost.Config;
            if (config is not null
                && !string.IsNullOrEmpty(config.NotShowNewVersionNoticeEndVersion)
                && !Global.IsNewVersion(config.NotShowNewVersionNoticeEndVersion, newVersion)
                && option.Trigger == UpdateTrigger.Auto)
            {
                return;
            }

            await ShowNewVersionDialogAsync(option, newVersion, config);
        }
        catch (Exception e)
        {
            log.LogWarning(e, "获取 BetterGI 最新版本信息失败");
            if (option.Trigger == UpdateTrigger.Manual)
            {
                await UiHost.Dialogs.ShowAsync("获取最新版本信息失败。", "检查更新", DialogKind.Warning);
            }
        }
    }

    private async Task<string> FetchLatestVersionCoreAsync(UpdateOption option)
    {
        if (option.Channel == UpdateChannel.Alpha)
        {
            return await FetchFromMirrorChyanAsync();
        }

        return await FetchFromOssAsync();
    }

    private async Task<string> FetchFromOssAsync()
    {
        try
        {
            var httpClient = httpClientFactory.CreateClient(HttpClientNames.Default);
            await using var stream = await httpClient.GetStreamAsync(NoticeUrl);
            using var doc = await JsonDocument.ParseAsync(stream);
            var root = doc.RootElement;
            if (!root.TryGetProperty("version", out var verEl))
            {
                return "";
            }

            var version = verEl.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(version))
            {
                return "";
            }

            var gray = 10;
            if (root.TryGetProperty("gray", out var grayEl))
            {
                gray = grayEl.ValueKind switch
                {
                    JsonValueKind.Number => grayEl.GetInt32(),
                    JsonValueKind.String when int.TryParse(grayEl.GetString(), out var g) => g,
                    _ => 10,
                };
            }

            // 灰度：用 /etc/machine-id（或主机名）做稳定哈希，同设备每次启动结果相同
            var deviceId = ReadLinuxDeviceId();
            if (string.IsNullOrEmpty(deviceId))
            {
                return version;
            }

            var hash = BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(deviceId)), 0);
            var mod = (int)(hash % 10);
            return mod < gray ? version : "";
        }
        catch (Exception e)
        {
            log.LogDebug(e, "OSS 源更新检查失败");
            return "";
        }
    }

    private async Task<string> FetchFromMirrorChyanAsync()
    {
        try
        {
            const string url = "https://mirrorchyan.com/api/resources/BGI/latest";
            var query = "user_agent=BetterGI&os=linux&arch=x64&channel=alpha";
            var httpClient = httpClientFactory.CreateClient(HttpClientNames.Default);
            var response = await httpClient.GetAsync($"{url}?{query}");
            var content = await response.Content.ReadAsStringAsync();
            var result = System.Text.Json.JsonSerializer.Deserialize<LatestResponse>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result is { Code: 0 })
            {
                return result.Data?.VersionName ?? "";
            }

            if (result is not null)
            {
                log.LogDebug("Mirror 源更新检查返回 Code={Code} Msg={Msg}", result.Code, result.Msg);
            }
        }
        catch (Exception e)
        {
            log.LogDebug(e, "Mirror 源更新检查失败");
        }

        return "";
    }

    private static string ReadLinuxDeviceId()
    {
        try
        {
            const string path = "/etc/machine-id";
            if (File.Exists(path))
            {
                return File.ReadAllText(path).Trim();
            }
        }
        catch
        {
            // ignore
        }

        return Environment.MachineName;
    }

    private static async Task ShowNewVersionDialogAsync(UpdateOption option, string newVersion, AllConfig? config)
    {
        if (AvaloniaPickers.MainWindow is not { } owner)
        {
            return;
        }

        var downloadUrl = option.Channel == UpdateChannel.Stable ? StableDownloadPageUrl : AlphaReleasesUrl;
        var body = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = $"发现新版本 {newVersion}。Linux 版请从下载页获取安装包（不能运行 Windows 更新程序）。",
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock { Text = downloadUrl, Classes = { "desc" }, TextWrapping = TextWrapping.Wrap },
            },
        };
        var dialog = new FAContentDialog
        {
            Title = $"发现新版本 {newVersion}",
            Content = body,
            PrimaryButtonText = "打开下载页",
            SecondaryButtonText = "忽略此版本",
            CloseButtonText = "取消",
            DefaultButton = FAContentDialogButton.Primary,
        };
        CurrentDialog = dialog;
        FAContentDialogResult result;
        try
        {
            result = await FluentMessageBox.ShowCustomAsync(dialog, owner);
        }
        finally
        {
            CurrentDialog = null;
        }

        switch (result)
        {
            case FAContentDialogResult.Primary:
                OpenUrl(downloadUrl);
                break;
            case FAContentDialogResult.Secondary:
                if (config is not null)
                {
                    config.NotShowNewVersionNoticeEndVersion = newVersion;
                }

                break;
        }
    }
}
