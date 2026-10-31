using Avalonia.Controls;
using Avalonia.Threading;
using BetterGenshinImpact.Avalonia.Views;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.ViewModel.Resin;
using BetterGenshinImpact.ViewModel.SoloTaskSettings;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Avalonia.Services;

/// <summary>
/// 树脂设置弹窗的 Avalonia 实现（WPF 为 View/Windows/ResinSettingsDialogs）。
/// 接口是同步的（调用方直接拿确认后的新值），这里用嵌套消息循环等待模态窗口关闭。
/// </summary>
public sealed class AvaloniaResinSettingsDialogs : IResinSettingsDialogs
{
    public ResinUsage? EditResinUsage(string taskName, ResinUsageCapability capability, ResinUsage current)
    {
        ResinSettingsWindow? dialog = null;
        var editor = new ResinUsageEditorViewModel(taskName, capability, current, async confirmation =>
        {
            if (dialog is null) return true;
            return await FluentMessageBox.ShowAsync(dialog, confirmation.Message, confirmation.Title, DialogKind.Question, DialogButtons.YesNo) == DialogResult.Yes;
        });
        dialog = new ResinSettingsWindow(editor);
        return ShowModal(dialog) ? editor.BuildValue() : null;
    }

    public int? EditCraftResin(int minResinToKeep)
    {
        var editor = new CraftResinEditorViewModel(minResinToKeep);
        return ShowModal(new ResinSettingsWindow(editor)) ? editor.BuildValue() : null;
    }

    private static bool ShowModal(ResinSettingsWindow dialog)
    {
        RunModal(dialog);
        return dialog.Confirmed;
    }

    /// <summary>以活动窗口（或主窗口）为所有者显示模态窗口，嵌套消息循环直到窗口关闭</summary>
    internal static void RunModal(Window dialog)
    {
        var owner = AvaloniaPickers.MainWindow;
        if (owner is null)
        {
            return;
        }

        // 打开时活动的窗口作为所有者（如自动配音设置窗口之上的弹窗），否则用主窗口
        var active = (global::Avalonia.Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
            ?.Windows.FirstOrDefault(w => w.IsActive) ?? owner;
        using var done = new CancellationTokenSource();
        dialog.Closed += (_, _) => done.Cancel();
        _ = dialog.ShowDialog(active);
        Dispatcher.UIThread.MainLoop(done.Token);
    }
}

/// <summary>体力计划子任务设置弹窗（WPF 为 View/Windows/ResinPlanSettingsDialogs）</summary>
public sealed class AvaloniaResinPlanSettingsDialogs(SoloTaskRegistry registry, IResinSettingsDialogs resinDialogs) : IResinPlanSettingsDialogs
{
    public JObject? EditSubTask(ResinPlanSubTaskSpec spec, JObject parameters)
    {
        var schema = PuloniaSoloTaskSchema.Create(registry.Get(spec.TaskId).ParamType);
        var viewModel = new ResinPlanSubTaskSettingsViewModel(spec, schema, parameters, resinDialogs);
        var window = new ResinPlanSubTaskWindow(viewModel);
        AvaloniaResinSettingsDialogs.RunModal(window);
        return window.Result;
    }
}
