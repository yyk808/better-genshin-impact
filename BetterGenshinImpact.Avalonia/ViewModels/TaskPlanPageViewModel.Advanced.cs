using Avalonia.Controls;
using Avalonia.Media;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// 任务设置的“高级设置”（对应 WPF 一条龙步骤配置窗口的“高级配置与有效来源”）：
/// 本步骤参数、共享设置、执行策略以 JSON 编辑并显示有效参数；解析与校验在 Core PuloniaNodeAdvancedEditing
/// </summary>
public partial class TaskPlanPageViewModel
{
    [RelayCommand]
    public async Task EditNodeAdvancedAsync()
    {
        if (SelectedPlan is not { } item || NodeEditor is not { } editor) return;
        var task = editor.Row.Task;
        var owner = (global::Avalonia.Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null) return;

        TextBox Box(string name, string text) => new()
        {
            Name = name, Text = text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60, MaxHeight = 140,
            FontFamily = new FontFamily("monospace"),
        };
        var parameters = Box("AdvancedParametersBox", PuloniaNodeAdvancedEditing.FormatParameters(task));
        var settings = Box("AdvancedSettingsBox", PuloniaNodeAdvancedEditing.FormatSettings(task));
        var policy = Box("AdvancedPolicyBox", PuloniaNodeAdvancedEditing.FormatPolicy(task));
        var clear = new Button { Name = "AdvancedClearParametersButton", Content = "清空本任务参数" };
        clear.Click += (_, _) => parameters.Text = "{}";
        var source = Box("AdvancedSourceBox", PuloniaNodeAdvancedEditing.FormatSource(task));
        var content = new StackPanel { Spacing = 6, MinWidth = 480 };
        // 资源位置与资源版本（WPF 一条龙步骤配置窗口顶部）：仓库资源由来源定位，不能直接改路径
        TextBox? path = null;
        if (PuloniaNodeAdvancedEditing.CanEditPath(task))
        {
            content.Children.Add(new TextBlock { Text = "资源位置" });
            path = new TextBox { Name = "AdvancedPathBox", Text = task.Path ?? "" };
            content.Children.Add(path);
        }
        var runUpdate = false;
        FAContentDialog? dialog = null;
        if (PuloniaResourceUpdateRules.CanUpdate(task))
        {
            var row = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock { Name = "AdvancedResourceVersionText", Text = ResourceVersionText(task), VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center });
            var update = new Button { Name = "AdvancedUpdateResourceButton", Content = "更新此任务资源" };
            // 确认对话框不能叠在本对话框上：先关闭（不应用本窗口的修改），再走“更新此任务资源”
            update.Click += (_, _) => { runUpdate = true; dialog?.Hide(FAContentDialogResult.None); };
            row.Children.Add(update);
            content.Children.Add(row);
        }
        content.Children.Add(new TextBlock { Text = "任务参数（只属于此步骤；清空后使用任务默认值，共享设置不受影响）", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(parameters);
        content.Children.Add(clear);
        content.Children.Add(new TextBlock { Text = "共享设置（JSON 对象，设置项 ID 到值）" });
        content.Children.Add(settings);
        content.Children.Add(new TextBlock { Text = "执行策略（timeout_seconds / failure_behavior / max_retries / retry_delay_seconds）", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(policy);
        content.Children.Add(new TextBlock { Text = "引用来源（计划或目录引用的 JSON；清空表示不引用）", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(source);
        content.Children.Add(new TextBlock { Text = "有效参数（任务默认值叠加本步骤参数，只读）" });
        content.Children.Add(new TextBox
        {
            Name = "AdvancedEffectiveBox", Text = PuloniaNodeAdvancedEditing.FormatEffective(editor.Definition, task), IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap, MaxHeight = 140, FontFamily = new FontFamily("monospace"),
        });
        content.Children.Add(new TextBlock { Text = "应用后自动保存，可用“撤销最近编辑”恢复。", FontSize = 12, Opacity = 0.75 });
        dialog = new FAContentDialog
        {
            Title = $"高级设置 · {task.Name}", Content = new ScrollViewer { Content = content, MaxHeight = 560 },
            PrimaryButtonText = "应用", CloseButtonText = "取消", DefaultButton = FAContentDialogButton.Primary,
        };
        var result = await Services.FluentMessageBox.ShowCustomAsync(dialog, owner);
        if (runUpdate)
        {
            SelectedNode = Nodes.FirstOrDefault(n => n.Task.Id == task.Id) ?? SelectedNode;
            await UpdateSelectedResourceAsync();
            return;
        }
        if (result != FAContentDialogResult.Primary) return;
        try
        {
            PuloniaNodeAdvancedEditing.Apply(item.Plan, task, parameters.Text, settings.Text, policy.Text, source.Text, path?.Text ?? task.Path);
        }
        catch (Exception e) when (e is FormatException or PuloniaTaskValidationException or InvalidOperationException)
        {
            StatusMessage = "高级设置无效：" + e.Message;
            return;
        }

        await SaveAsync(item, $"已保存“{task.Name}”的高级设置。");
        RebuildNodes();
        SelectedNode = Nodes.FirstOrDefault(n => n.Task.Id == task.Id);
    }

    /// <summary>当前固定的资源版本摘要（与 WPF ResourceVersionText 相同：有声明版本时显示版本号，否则“已固定 / 未固定”）</summary>
    private string ResourceVersionText(PuloniaTask task)
    {
        var declared = GetCheck(CurrentPlanId, task)?.State?.ApprovedDeclaredVersion;
        if (!string.IsNullOrWhiteSpace(declared)) return "资源版本：" + declared;
        return string.IsNullOrWhiteSpace(PuloniaResourceUpdateRules.ApprovedVersion(task)) ? "资源版本：未固定" : "资源版本：已固定";
    }
}
