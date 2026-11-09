using Avalonia.Controls;
using Avalonia.VisualTree;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Model;
using FluentAvalonia.UI.Controls;

namespace BetterGenshinImpact.Avalonia;

internal sealed partial class UiTests
{
    /// <summary>
    /// 检查更新（对照 WPF UpdateService / CheckUpdateWindow）：设置页关于区按钮、假版本弹窗、打开下载页 / 忽略此版本。
    /// 不联网；不启动 Windows 更新程序。
    /// </summary>
    private async Task CheckUpdateAsync()
    {
        vm.Navigate("设置");
        await Settle(600);
        foreach (var ex in All<FASettingsExpander>()) ex.IsExpanded = true;
        await Settle(400);

        Check("关于区有检查更新按钮", Named<Button>("CheckUpdateButton") is { Command: not null });
        Check("关于区有检查测试版更新按钮", Named<Button>("CheckUpdateAlphaButton") is { Command: not null });
        Named<Button>("CheckUpdateButton")!.BringIntoView();
        await Settle(200);

        var opened = new List<string>();
        var fetch0 = LinuxUpdateService.FetchLatestVersion;
        var open0 = LinuxUpdateService.OpenUrl;
        LinuxUpdateService.OpenUrl = url => opened.Add(url);

        try
        {
            // 1) 已是最新（空版本）
            LinuxUpdateService.FetchLatestVersion = _ => Task.FromResult("");
            await Click(Named<Button>("CheckUpdateButton"), "检查更新（已最新）");
            var latest = await WaitFor(() => FluentMessageBox.Current);
            Check("已是最新时弹出提示", latest is not null && (latest.Title as string ?? "").Contains("检查更新"));
            latest?.Hide(FAContentDialogResult.None);
            await Settle(300);

            // 2) 发现新版本 → 打开下载页
            LinuxUpdateService.FetchLatestVersion = opt =>
            {
                Check("稳定版检查走 Stable 通道", opt.Channel == UpdateChannel.Stable && opt.Trigger == UpdateTrigger.Manual);
                return Task.FromResult("99.0.0");
            };
            await Click(Named<Button>("CheckUpdateButton"), "检查更新（新版本）");
            var dlg = await WaitFor(() => LinuxUpdateService.CurrentDialog ?? FluentMessageBox.Current);
            Check("发现新版本对话框", dlg is not null && (dlg.Title as string ?? "").Contains("99.0.0"), dlg?.Title);
            Check("对话框按钮文案", dlg is { PrimaryButtonText: "打开下载页", SecondaryButtonText: "忽略此版本", CloseButtonText: "取消" });
            Shot("avalonia-check-update-new.png");
            dlg?.Hide(FAContentDialogResult.Primary);
            await Settle(300);
            Check("打开下载页走稳定版链接", opened.SequenceEqual([LinuxUpdateService.StableDownloadPageUrl]), string.Join("|", opened));

            // 3) 忽略此版本（自动检查会被跳过）
            opened.Clear();
            var cfg = vm.Settings.Config;
            var ignored = cfg.NotShowNewVersionNoticeEndVersion;
            LinuxUpdateService.FetchLatestVersion = _ => Task.FromResult("99.0.1");
            await Click(Named<Button>("CheckUpdateButton"), "检查更新（忽略）");
            dlg = await WaitFor(() => LinuxUpdateService.CurrentDialog ?? FluentMessageBox.Current);
            Check("再次发现新版本", dlg is not null && (dlg.Title as string ?? "").Contains("99.0.1"));
            dlg?.Hide(FAContentDialogResult.Secondary);
            await Settle(300);
            Check("忽略写回 NotShowNewVersionNoticeEndVersion", cfg.NotShowNewVersionNoticeEndVersion == "99.0.1", cfg.NotShowNewVersionNoticeEndVersion);
            Check("忽略时不打开链接", opened.Count == 0);

            // Auto + 已忽略同版本 → 不弹窗
            var svc = vm.Host?.Services.GetService(typeof(BetterGenshinImpact.Service.Interface.IUpdateService))
                as BetterGenshinImpact.Service.Interface.IUpdateService;
            Check("已注册 IUpdateService", svc is not null);
            if (svc is not null)
            {
                LinuxUpdateService.FetchLatestVersion = _ => Task.FromResult("99.0.1");
                await svc.CheckUpdateAsync(new UpdateOption { Trigger = UpdateTrigger.Auto, Channel = UpdateChannel.Stable });
                await Settle(400);
                Check("自动检查对已忽略版本不再弹窗", FluentMessageBox.Current is null && LinuxUpdateService.CurrentDialog is null);
            }

            // 4) 测试版：先警告，再假版本 → 打开预览版发布页
            LinuxUpdateService.FetchLatestVersion = opt =>
            {
                Check("测试版检查走 Alpha 通道", opt.Channel == UpdateChannel.Alpha);
                return Task.FromResult("99.1.0-alpha.1");
            };
            await Click(Named<Button>("CheckUpdateAlphaButton"), "检查测试版");
            var warn = await WaitFor(() => FluentMessageBox.Current);
            Check("测试版先警告不稳定", warn is not null);
            warn?.Hide(FAContentDialogResult.Primary); // Yes
            await Settle(300);
            dlg = await WaitFor(() => LinuxUpdateService.CurrentDialog ?? FluentMessageBox.Current);
            Check("测试版发现新版本", dlg is not null && (dlg.Title as string ?? "").Contains("99.1.0"));
            dlg?.Hide(FAContentDialogResult.Primary);
            await Settle(300);
            Check("打开预览版发布页", opened.SequenceEqual([LinuxUpdateService.AlphaReleasesUrl]), string.Join("|", opened));
            Shot("avalonia-check-update-alpha.png");

            cfg.NotShowNewVersionNoticeEndVersion = ignored;
        }
        finally
        {
            LinuxUpdateService.FetchLatestVersion = fetch0;
            LinuxUpdateService.OpenUrl = open0;
            if (FluentMessageBox.Current is { } left) left.Hide(FAContentDialogResult.None);
        }
    }

}
