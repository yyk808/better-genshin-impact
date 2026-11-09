using Avalonia.Controls;
using BetterGenshinImpact.Avalonia.ViewModels;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace BetterGenshinImpact.Avalonia;

internal sealed partial class UiTests
{
    /// <summary>
    /// 新一条龙“导入旧配置 / 查看导入报告”（--ui-test newonedragon-import，同 WPF ImportLegacyAsync 与 PuloniaOneDragonImportDialog）：
    /// 菜单项可用；多选文件后显示“旧数据导入预览”（计划数、未识别文件的差异、“仅写新存储，原文件保留。”）；确认后计划加入列表并选中、
    /// 归属一条龙、状态栏说明、差异报告写到 imports/ 且含“写入完成”；重名追加“（导入 2）”；取消不导入但仍写报告；
    /// 没有可导入计划时“确认导入”不可用；“查看导入报告”显示上次报告；原文件不变。
    /// 旧配置文件放在临时目录；结束时删除本测试导入的计划、报告与临时文件
    /// </summary>
    private async Task NewOneDragonImportAsync()
    {
        var page = vm.NewOneDragon;
        var plans = page.Plans;
        var store = vm.Host?.Services.GetService<PuloniaTaskStore>();
        Check("宿主提供任务存储", store is not null);
        if (store is null) return;
        var tmp = Path.Combine(Path.GetTempPath(), $"bgi-od-import-{Environment.ProcessId}");
        Directory.CreateDirectory(tmp);
        var group = Path.Combine(tmp, "old-group.json");
        var bad = Path.Combine(tmp, "bad.json");
        File.WriteAllText(group, "{\"name\":\"M64 旧配置组\",\"projects\":[]}");
        File.WriteAllText(bad, "{\"foo\":1}");
        var groupText = File.ReadAllText(group);
        var imports = Path.Combine(store.RootDirectory, "imports");
        var reports0 = Directory.Exists(imports) ? Directory.GetFiles(imports).ToHashSet() : [];
        var createdImports = !Directory.Exists(imports);
        var pick0 = TaskPlanPageViewModel.PickLegacyFiles;
        IReadOnlyList<string> picked = [group, bad];
        TaskPlanPageViewModel.PickLegacyFiles = () => Task.FromResult(picked);
        try
        {
            vm.Navigate("新一条龙");
            await plans.ReloadAsync();
            await Settle(600);
            var count0 = plans.Plans.Count;
            var more = await WaitFor(() => Named<DropDownButton>("MoreButton"));
            more?.Flyout?.ShowAt(more!);
            await Settle(400);
            var items = (more?.Flyout as MenuFlyout)?.Items.OfType<MenuItem>().ToList() ?? [];
            var importItem = items.FirstOrDefault(i => UiText(i.Header) == "导入旧配置");
            var reportItem = items.FirstOrDefault(i => UiText(i.Header) == "查看导入报告");
            Check("“导入旧配置”“查看导入报告”可用（同 WPF）", importItem is { IsEnabled: true, Command: not null } && reportItem is { IsEnabled: true, Command: not null });
            more?.Flyout?.Hide();

            // 1. 预览并确认
            var run = plans.ImportLegacyCommand.ExecuteAsync(null);
            var preview = await WaitFor(() => TaskPlanPageViewModel.CurrentImportPreview);
            await Settle(300);
            var text = PreviewText(preview);
            Check("旧数据导入预览：计划数、未识别文件的差异与来源（同 WPF 报告）", preview?.Title as string == "旧数据导入预览" && text.StartsWith("导入预览：1 个计划")
                && text.Contains("计划：M64 旧配置组") && text.Contains("bad.json：未导入") && text.Contains("old-group.json · SHA256 "), text);
            Check("预览说明与按钮", preview?.PrimaryButtonText == "确认导入" && preview.IsPrimaryButtonEnabled && preview.CloseButtonText == "取消"
                && FindText(preview, "仅写新存储，原文件保留。"));
            Shot("avalonia-newonedragon-import.png");
            preview?.Hide(FAContentDialogResult.Primary);
            await run;
            await Settle(300);
            var imported = plans.Plans.Skip(count0).ToList();
            Check("确认后计划加入列表并选中、归属一条龙", imported.Count == 1 && imported[0].Name == "M64 旧配置组" && plans.SelectedPlan == imported[0]
                && imported[0].Plan.Purpose == PuloniaTaskPlanPurpose.OneDragon, string.Join(",", imported.Select(p => p.Name)));
            Check("状态栏说明（同 WPF）", plans.StatusMessage == "已导入 1 个计划；原文件保留，差异报告可从导入按钮旁打开。", plans.StatusMessage);
            var report1 = plans.ImportReportPath;
            Check("差异报告写到 imports/yyyyMMdd-HHmmss.txt 且含“写入完成”", report1 is not null && Path.GetDirectoryName(report1) == imports
                && System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(report1), @"^\d{8}-\d{6}(-\d+)?\.txt$") && File.ReadAllText(report1).Contains("写入完成："), report1);
            Check("原文件不变", File.ReadAllText(group) == groupText && File.Exists(bad));
            var reloaded = await store.ListPlansAsync();
            Check("计划已写入存储", reloaded.Any(p => p.Id == imported[0].Plan.Id && p.Purpose == PuloniaTaskPlanPurpose.OneDragon));

            // 2. 重名：追加“（导入 2）”
            run = plans.ImportLegacyCommand.ExecuteAsync(null);
            preview = await WaitFor(() => TaskPlanPageViewModel.CurrentImportPreview);
            preview?.Hide(FAContentDialogResult.Primary);
            await run;
            await Settle(300);
            Check("重名时追加“（导入 2）”（同 WPF）", plans.Plans.Any(p => p.Name == "M64 旧配置组（导入 2）"));
            var report2 = plans.ImportReportPath;
            Check("同一秒内也不覆盖已有报告", report2 is not null && report2 != report1 && File.Exists(report1!));

            // 3. 取消：不导入，但仍写报告
            var countBefore = plans.Plans.Count;
            run = plans.ImportLegacyCommand.ExecuteAsync(null);
            preview = await WaitFor(() => TaskPlanPageViewModel.CurrentImportPreview);
            preview?.Hide(FAContentDialogResult.None);
            await run;
            Check("取消后不导入、仍写报告（同 WPF）", plans.Plans.Count == countBefore && plans.ImportReportPath is { } r3 && r3 != report2 && !File.ReadAllText(r3).Contains("写入完成："));

            // 4. 没有可导入的计划：确认不可用
            picked = [bad];
            run = plans.ImportLegacyCommand.ExecuteAsync(null);
            preview = await WaitFor(() => TaskPlanPageViewModel.CurrentImportPreview);
            Check("没有可导入的计划时“确认导入”不可用", preview is { IsPrimaryButtonEnabled: false } && PreviewText(preview).StartsWith("导入预览：0 个计划"));
            preview?.Hide(FAContentDialogResult.None);
            await run;

            // 5. 查看导入报告
            var last = plans.ImportReportPath!;
            var show = plans.ShowImportReportCommand.ExecuteAsync(null);
            var dialog = await WaitFor(() => Services.FluentMessageBox.Current);
            Check("“查看导入报告”显示上次报告（只读）", dialog?.Title as string == "导入差异报告" && dialog.Content is TextBox { IsReadOnly: true } box && box.Text == File.ReadAllText(last));
            dialog?.Hide(FAContentDialogResult.None);
            await show;
        }
        finally
        {
            TaskPlanPageViewModel.PickLegacyFiles = pick0;
            TaskPlanPageViewModel.CurrentImportPreview?.Hide();
            try
            {
                foreach (var p in (await store.ListPlansAsync()).Where(p => p.Name.StartsWith("M64 旧配置组")))
                {
                    await store.DeletePlanAsync(p.Id, p.Revision);
                }
            }
            catch (Exception e)
            {
                Check("清理导入的计划", false, e.Message);
            }

            if (Directory.Exists(imports))
            {
                foreach (var f in Directory.GetFiles(imports).Where(f => !reports0.Contains(f))) File.Delete(f);
                if (createdImports && Directory.GetFileSystemEntries(imports).Length == 0) Directory.Delete(imports);
            }

            Directory.Delete(tmp, recursive: true);
            await plans.ReloadAsync();
        }

        static string PreviewText(FAContentDialog? d) =>
            (d?.Content as DockPanel)?.Children.OfType<TextBox>().FirstOrDefault()?.Text ?? "";

        static bool FindText(FAContentDialog? d, string text) =>
            (d?.Content as DockPanel)?.Children.OfType<TextBlock>().Any(t => t.Text == text) == true;
    }
}
