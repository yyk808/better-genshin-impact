using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Host.Common;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// 新一条龙“导入旧配置 / 查看导入报告”（对照 WPF PuloniaOneDragonViewModel.ImportLegacyAsync、ShowImportReport 与 PuloniaOneDragonImportDialog）：
/// 选择旧配置组、一条龙或 d-v3 计划（可多选），Core 的 PuloniaTaskLegacyImporter 生成预览，确认后只写新存储（原文件保留），
/// 差异报告写到 imports/yyyyMMdd-HHmmss.txt，可从“查看导入报告”打开
/// </summary>
public sealed partial class TaskPlanPageViewModel
{
    /// <summary>选择旧配置文件（界面自测替换为返回固定文件）</summary>
    public static Func<Task<IReadOnlyList<string>>> PickLegacyFiles { get; set; } = () =>
        Services.AvaloniaPickers.PickFilesAsync("导入旧配置组、一条龙或 d-v3 计划", "JSON 配置", Global.Absolute("User"), "*.json");

    /// <summary>上次导入的差异报告（本次运行内有效，同 WPF）</summary>
    [ObservableProperty]
    private string? _importReportPath;

    /// <summary>当前打开的导入预览（界面自测用）</summary>
    public static FAContentDialog? CurrentImportPreview { get; private set; }

    /// <summary>选择并预览旧配置，确认后仅写新存储及差异报告</summary>
    [RelayCommand]
    private async Task ImportLegacyAsync()
    {
        if (_store is null || _tasks is null || _catalog is null) return;
        var files = await PickLegacyFiles();
        if (files.Count == 0) return;
        PuloniaTaskImportResult? result = null;
        try
        {
            // 冻结当前业务配置，以免后台导入期间其他页面修改配置导致不同节点取值不一致（同 WPF）
            var config = System.Text.Json.JsonSerializer.Deserialize<AllConfig>(
                System.Text.Json.JsonSerializer.Serialize(ConfigHost.Config!, ConfigJson.Options), ConfigJson.Options)!;
            var definitions = _tasks.Definitions;
            var catalog = _catalog;
            result = await Task.Run(() => new PuloniaTaskLegacyImporter(definitions, catalog).PrepareAsync(files, config));
            if (!await ShowImportPreviewAsync(result) || result.Plans.Count == 0) return;
            await ImportPreparedAsync(result);
            result.Differences.Add("写入完成：" + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            StatusMessage = $"已导入 {result.Plans.Count} 个计划；原文件保留，差异报告可从导入按钮旁打开。";
        }
        catch (Exception ex)
        {
            result?.Differences.Add("写入未全部完成：" + ex.Message + "；已保存的计划保留在列表中，原文件不变。");
            await UiHost.Dialogs.ShowAsync("导入未全部完成：" + ex.Message, "导入旧数据", DialogKind.Error);
        }
        finally
        {
            if (result is not null)
            {
                try
                {
                    var directory = Path.Combine(_store.RootDirectory, "imports");
                    Directory.CreateDirectory(directory);
                    ImportReportPath = await WriteImportReportAsync(directory, result.Report);
                }
                catch (Exception ex)
                {
                    StatusMessage = "导入报告保存失败：" + ex.Message;
                    ImportReportPath = null;
                }
            }
        }
    }

    /// <summary>导入预览（同 WPF“旧数据导入预览”）：报告文本、“仅写新存储，原文件保留。”；没有可导入的计划时“确认导入”不可用</summary>
    private static async Task<bool> ShowImportPreviewAsync(PuloniaTaskImportResult result)
    {
        if (Services.AvaloniaPickers.MainWindow is not { } owner) return false;
        var report = new TextBox
        {
            Name = "ImportPreviewText", Text = result.Report, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            Width = 760, Height = 520,
        };
        var dialog = new FAContentDialog
        {
            Title = "旧数据导入预览",
            Content = new DockPanel
            {
                Children =
                {
                    new TextBlock { Text = "仅写新存储，原文件保留。", Margin = new global::Avalonia.Thickness(0, 10, 0, 0), [DockPanel.DockProperty] = Dock.Bottom },
                    report,
                },
            },
            PrimaryButtonText = "确认导入",
            IsPrimaryButtonEnabled = result.Plans.Count > 0,
            CloseButtonText = "取消",
            DefaultButton = FAContentDialogButton.Primary,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 850.0;
        CurrentImportPreview = dialog;
        try
        {
            return await Services.FluentMessageBox.ShowCustomAsync(dialog, owner) == FAContentDialogResult.Primary;
        }
        finally
        {
            CurrentImportPreview = null;
        }
    }

    /// <summary>导入确认后逐份原子保存并加入列表；重名时追加“（导入 N）”，归属固定为当前页面（同 WPF ImportPreparedAsync）</summary>
    private async Task ImportPreparedAsync(PuloniaTaskImportResult result)
    {
        TaskPlanItem? last = null;
        foreach (var plan in result.Plans)
        {
            var baseName = plan.Name;
            var suffix = 2;
            while (Plans.Any(p => p.Name == plan.Name)) plan.Name = baseName + $"（导入 {suffix++}）";
            // 归属来自导入入口，不以名称、任务类型或引用目标推断
            plan.Purpose = Purpose;
            var saved = await _store!.SavePlanAsync(plan);
            last = new TaskPlanItem(saved);
            Plans.Add(last);
        }

        await _store!.SavePlanOrderAsync(PlanOrder(Plans.Select(p => p.Plan.Id)));
        OnPropertyChanged(nameof(PlanCountText));
        if (last is not null) SelectedPlan = last;
        _ = CheckResourcesAsync(false);
    }

    /// <summary>以 yyyyMMdd-HHmmss.txt 新建导入报告，同秒重名时依次追加 -2、-3，不覆盖已有报告（同 WPF）</summary>
    private static async Task<string> WriteImportReportAsync(string directory, string report)
    {
        var baseName = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var bytes = new UTF8Encoding(false).GetBytes(report);
        for (var index = 1; ; index++)
        {
            var path = Path.Combine(directory, (index == 1 ? baseName : baseName + "-" + index) + ".txt");
            FileStream stream;
            try { stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
            catch (IOException) when (File.Exists(path)) { continue; }
            await using (stream)
                await stream.WriteAsync(bytes);
            return path;
        }
    }

    /// <summary>打开可复制的上次导入报告（只读），不执行导入或修改源文件（同 WPF）</summary>
    [RelayCommand]
    private async Task ShowImportReportAsync()
    {
        if (ImportReportPath is null || !File.Exists(ImportReportPath) || Services.AvaloniaPickers.MainWindow is not { } owner) return;
        var dialog = new FAContentDialog
        {
            Title = "导入差异报告",
            Content = new TextBox
            {
                Name = "ImportReportText", Text = await File.ReadAllTextAsync(ImportReportPath), IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true, Width = 760, Height = 520,
            },
            CloseButtonText = "关闭",
        };
        dialog.Resources["ContentDialogMaxWidth"] = 850.0;
        await Services.FluentMessageBox.ShowCustomAsync(dialog, owner);
    }
}
