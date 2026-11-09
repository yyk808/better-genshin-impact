using Avalonia.Controls;
using Avalonia.VisualTree;
using BetterGenshinImpact.Avalonia.Views;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Host.Common.Storage;
using BetterGenshinImpact.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace BetterGenshinImpact.Avalonia;

internal sealed partial class UiTests
{
    /// <summary>
    /// 存储空间（--ui-test storage，同 WPF StorageManagementWindow 与常规设置页“存储空间”卡片）：设置页摘要与“管理”按钮、
    /// 窗口分组（无“下载”分组）与各行占用、一键清理（确认框列出内容；立即清理的缓存被删除，网页缓存登记为下次启动清理并显示“待清理”）、
    /// 需确认的“截图”清除（取消不删、确定删除）、“打开”目录、文件缓存上限写回配置、推迟清理在下次启动执行、悬浮小窗设置页的“打开存储空间”。
    /// 只在 Data/Cache/Files/map-point-images、Data/WebKit/Guide/cache、log/screenshot 下建本测试的文件，结束时删除并恢复推迟清理记录与缓存上限
    /// </summary>
    private async Task StorageAsync()
    {
        var host = vm.Host;
        var storage = host?.Services.GetService<IStorageService>();
        Check("宿主注册存储空间服务", storage is not null);
        if (host is null || storage is null) return;

        var cacheFile = Path.Combine(AppPaths.FileCache, "map-point-images", "uitest-storage.bin");
        var guideFile = Path.Combine(LinuxWebDataPaths.Cache(LinuxWebDataPaths.Guide), "uitest-storage.bin");
        var shotFile = Path.Combine(Global.Absolute(@"log\screenshot"), "uitest-storage.png");
        var created = new List<string>();
        // 记下本测试会新建的各级目录（到程序目录为止），结束时删除
        foreach (var file in new[] { cacheFile, guideFile, shotFile })
        {
            for (var d = Path.GetDirectoryName(file); d is not null && d.Length > Global.StartUpPath.TrimEnd('/').Length && !Directory.Exists(d); d = Path.GetDirectoryName(d))
            {
                if (!created.Contains(d)) created.Add(d);
            }
        }

        var pendingFile = AppPaths.PendingCleanupFile;
        var pending0 = File.Exists(pendingFile) ? File.ReadAllText(pendingFile) : null;
        var limit0 = host.Config.StorageConfig.FileCacheLimitMb;
        try
        {
            Write(cacheFile, 1024 * 1024);
            Write(guideFile, 2 * 1024 * 1024);
            Write(shotFile, 100 * 1024);

            vm.Navigate("设置");
            vm.Settings.SelectedTab = 0;
            await Settle(600);
            await vm.Settings.RefreshStorageSummaryAsync(force: true);
            var card = Named<FluentAvalonia.UI.Controls.FASettingsExpander>("StorageCard");
            Check("设置页“存储空间”卡片与摘要（同 WPF）", UiText(card?.Header) == "存储空间" && vm.Settings.StorageSummaryText.StartsWith("缓存、下载、网页数据和临时文件共占用 "),
                vm.Settings.StorageSummaryText);
            Check("“Windows 版专属设置”不再列出存储空间",
                Named<FluentAvalonia.UI.Controls.FASettingsExpander>("WindowsOnlyCard")?.Items.OfType<FluentAvalonia.UI.Controls.FASettingsExpanderItem>().All(i => UiText(i.Content) != "存储空间") == true);

            await Click(Named<Button>("OpenStorageButton"), "管理");
            var win = await WaitFor(() => StorageManagementWindow.Current);
            Check("打开存储空间窗口", win is { Title: "存储空间" });
            if (win is null) return;
            if (win.InitialRefresh is { } refresh) await refresh;
            await Task.Delay(300);
            var svm = (StorageManagementViewModel)win.DataContext!;
            var groups = svm.Groups.Select(g => g.Name).ToList();
            Check("分组同 WPF 顺序（Linux 无下载分组）", string.Join(",", groups) == "缓存,网页数据,临时文件,日志与诊断,用户数据", string.Join(",", groups));
            StorageArea? Find(string id) => svm.Groups.SelectMany(g => g.Items).FirstOrDefault(i => i.Area.Id == id)?.Area;
            var cacheRow = svm.Groups.SelectMany(g => g.Items).FirstOrDefault(i => i.Area.Id == "cache:map-point-images");
            var guideRow = svm.Groups.SelectMany(g => g.Items).FirstOrDefault(i => i.Area.Id == "webview:guide:Default");
            var shotRow = svm.Groups.SelectMany(g => g.Items).FirstOrDefault(i => i.Area.Id == "diagnostics:screenshot");
            Check("各行统计占用（缓存 1 MB、悬浮小窗网页缓存 2 MB、截图 ≥100 KB）",
                cacheRow?.SizeBytes >= 1024 * 1024 && guideRow?.SizeBytes >= 2 * 1024 * 1024 && shotRow?.SizeBytes >= 100 * 1024,
                $"{cacheRow?.SizeText} / {guideRow?.SizeText} / {shotRow?.SizeText}");
            Check("网页数据行与 WPF 名称一致", Find("webview:app:Default")?.DisplayName == "软件内网页 · 网页缓存" && Find("webview:app:HtmlMask")?.DisplayName == "软件内网页 · 脚本遮罩数据"
                && guideRow?.DisplayName == "悬浮小窗 · 网页缓存" && Find("webview:cloud-game") is not null);
            Check("总览：总大小、比例条与图例", svm.TotalSizeText.StartsWith("共 ") && win.FindControl<Grid>("OverviewBar")?.Children.Count == svm.OverviewSegments.Count && svm.OverviewSegments.Count > 0,
                svm.TotalSizeText);
            Check("一键清理说明", svm.QuickCleanupText.StartsWith("可安全清理 ") && svm.QuickCleanupCommand.CanExecute(null), svm.QuickCleanupText);
            Shot(win, "avalonia-storage.png");

            // 一键清理：确认框显示在存储空间窗口上，列出将清理的内容
            var quick = svm.QuickCleanupCommand.ExecuteAsync(null);
            var dialog = await WaitFor(() => Services.FluentMessageBox.Current);
            await Until(() => dialog is not null && TopLevel.GetTopLevel(dialog) is not null, 3000);
            var dialogText = (dialog?.Content as DockPanel)?.Children.OfType<SelectableTextBlock>().FirstOrDefault()?.Text ?? "";
            Check("一键清理确认框列出内容（同 WPF）", dialog?.Title as string == "一键清理" && dialogText.Contains("· 悬浮小窗 · 网页缓存") && dialogText.Contains("不会删除脚本、配置、截图和登录状态。")
                && !dialogText.Contains("截图（"), dialogText);
            Check("确认框显示在存储空间窗口上", dialog is not null && TopLevel.GetTopLevel(dialog) == win, TopLevel.GetTopLevel(dialog!)?.GetType().Name);
            dialog?.Hide(FluentAvalonia.UI.Controls.FAContentDialogResult.Primary);
            await quick;
            await Settle(300);
            Check("立即清理的缓存已删除", !File.Exists(cacheFile));
            Check("网页缓存推迟到下次启动（文件仍在、显示“待清理”、写入推迟清理记录）",
                File.Exists(guideFile) && guideRow?.IsPending == true && guideRow.SizeText.EndsWith(" · 待清理") && File.Exists(pendingFile)
                && File.ReadAllText(pendingFile).Contains("Guide"), guideRow?.SizeText);
            Check("清理后显示提示", svm.LastNotice?.Contains("下次启动时清理") == true && win.FindControl<Border>("NoticeBar")?.IsVisible == true, svm.LastNotice);
            Check("截图不在一键清理中", File.Exists(shotFile));

            // 截图：清除前确认；取消不删，确定删除
            var shotButton = win.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("clean") && b.DataContext == shotRow);
            Check("截图行按钮为“清除…”", shotButton is not null && UiText(shotButton.Content) == "清除…" && shotButton.IsEffectivelyEnabled);
            var clean = svm.CleanupCommand.ExecuteAsync(shotRow);
            dialog = await WaitFor(() => Services.FluentMessageBox.Current);
            Check("清除确认（同 WPF）", dialog?.Title as string == "清除确认");
            dialog?.Hide(FluentAvalonia.UI.Controls.FAContentDialogResult.None);
            await clean;
            Check("取消后截图仍在", File.Exists(shotFile));
            clean = svm.CleanupCommand.ExecuteAsync(shotRow);
            dialog = await WaitFor(() => Services.FluentMessageBox.Current);
            dialog?.Hide(FluentAvalonia.UI.Controls.FAContentDialogResult.Primary);
            await clean;
            Check("确定后截图已删除", !File.Exists(shotFile));

            // 打开目录（自测替换为记录）
            string? opened = null;
            var open0 = svm.OpenFolderAction;
            svm.OpenFolderAction = path => opened = path;
            var openButton = win.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("openFolder") && b.DataContext == guideRow);
            await Click(openButton, "打开");
            await Click(win.FindControl<Button>("OpenDataFolderButton"), "打开目录");
            Check("“打开”与“打开目录”", opened == AppPaths.Root, opened);
            svm.OpenFolderAction = open0;

            // 文件缓存上限写回配置
            var box = win.FindControl<ComboBox>("CacheLimitBox");
            Check("文件缓存上限选项（同 WPF）", box?.ItemCount >= 5 && box.SelectedValue is int v0 && v0 == limit0, $"{box?.SelectedValue}");
            if (box is not null) box.SelectedIndex = 2;
            await Settle(200);
            Check("修改缓存上限写回配置", host.Config.StorageConfig.FileCacheLimitMb == 1024, host.Config.StorageConfig.FileCacheLimitMb.ToString());

            await Click(win.FindControl<Button>("CloseButton"), "关闭");
            await Until(() => StorageManagementWindow.Current is null, 3000);
            Check("关闭窗口", StorageManagementWindow.Current is null && !win.IsVisible);

            // 下次启动：执行推迟的清理
            host.Services.GetRequiredService<PendingCleanupStore>().ExecutePending();
            Check("下次启动时清理网页缓存", !File.Exists(guideFile));

            // 悬浮小窗设置页：打开存储空间
            vm.Navigate("悬浮小窗");
            await Settle(600);
            await Settle(600); // 第二次：展开后才出现的卡片内控件
            var guideButton = Named<Button>("GuideOpenStorageButton");
            Check("悬浮小窗设置页“网页缓存 → 打开存储空间”（同 WPF）", guideButton is not null);
            await Click(guideButton, "打开存储空间");
            var win2 = await WaitFor(() => StorageManagementWindow.Current);
            Check("从悬浮小窗设置页打开存储空间", win2 is not null);
            if (win2?.InitialRefresh is { } r2) await r2;
            win2?.Close();
            await Settle(300);
        }
        finally
        {
            host.Config.StorageConfig.FileCacheLimitMb = limit0;
            StorageManagementWindow.Current?.Close();
            foreach (var f in new[] { cacheFile, guideFile, shotFile })
            {
                if (File.Exists(f)) File.Delete(f);
            }

            if (pending0 is null) { if (File.Exists(pendingFile)) File.Delete(pendingFile); }
            else File.WriteAllText(pendingFile, pending0);
            foreach (var d in created.OrderByDescending(d => d.Length))
            {
                if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
            }
        }

        static void Write(string path, int bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[bytes]);
        }
    }
}
