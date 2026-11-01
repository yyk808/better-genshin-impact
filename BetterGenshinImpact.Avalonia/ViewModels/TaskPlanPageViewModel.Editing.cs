using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// 任务计划 / 新一条龙的编辑菜单（对应 WPF 工具栏与右键菜单）：复制 / 粘贴子树、启用全部、撤销、预览本次执行
/// </summary>
public partial class TaskPlanPageViewModel
{
    /// <summary>进程内剪贴板：任务计划页与新一条龙页共用（与 WPF 单例相同，可跨计划粘贴）</summary>
    private static readonly PuloniaTaskClipboardService FallbackClipboard = new();

    private PuloniaTaskClipboardService Clipboard => _services?.GetService(typeof(PuloniaTaskClipboardService)) as PuloniaTaskClipboardService ?? FallbackClipboard;

    /// <summary>与 WPF 相同最多保留 30 步撤销</summary>
    private const int MaxUndoCount = 30;

    /// <summary>每个计划最近一次保存后的内容（撤销时恢复到它之前的版本）</summary>
    private readonly Dictionary<string, PuloniaTaskPlan> _lastSaved = new(StringComparer.Ordinal);

    private readonly Dictionary<string, List<PuloniaTaskPlan>> _undo = new(StringComparer.Ordinal);

    private bool _undoing;

    /// <summary>最近一次预览的行（自测用）</summary>
    public IReadOnlyList<PuloniaTaskRunPreviewRow> LastPreviewRows { get; private set; } = [];

    /// <summary>当前计划可撤销的步数</summary>
    public int UndoCount => SelectedPlan is { } p && _undo.TryGetValue(p.Plan.Id, out var list) ? list.Count : 0;

    private void RememberSaved(TaskPlanItem? item)
    {
        if (item is null) return;
        _lastSaved[item.Plan.Id] = PuloniaTaskJson.ClonePlan(item.Plan);
        OnPropertyChanged(nameof(UndoCount));
    }

    /// <summary>保存前调用：把上一次保存的内容压入撤销栈</summary>
    private void RecordUndo(TaskPlanItem item)
    {
        if (_undoing || !_lastSaved.TryGetValue(item.Plan.Id, out var previous)) return;
        if (!_undo.TryGetValue(item.Plan.Id, out var list)) _undo[item.Plan.Id] = list = [];
        list.Add(previous);
        if (list.Count > MaxUndoCount) list.RemoveAt(0);
    }

    /// <summary>复制选中任务（含子树），可粘贴到本计划或其他计划</summary>
    [RelayCommand]
    private void CopyTask()
    {
        if (SelectedPlan is not { } item || SelectedNode is not { } row)
        {
            StatusMessage = "请先选中要复制的任务。";
            return;
        }

        Clipboard.Set(item.Name, row.Task, new Dictionary<string, JObject>(StringComparer.Ordinal), new JObject());
        StatusMessage = $"已复制“{row.Name}”，可在任意计划中粘贴。";
    }

    /// <summary>粘贴为独立副本（新 ID）：位置与“添加”相同；共享设置沿用目标位置的继承结果</summary>
    [RelayCommand]
    private async Task PasteTaskAsync()
    {
        if (SelectedPlan is not { } item) return;
        if (!Clipboard.TryCreatePaste(out _, out var pasted, out _, out _) || pasted is null)
        {
            StatusMessage = "剪贴板里没有任务，请先复制。";
            return;
        }

        if (ReferencedPlanIds(pasted).Any(id => id == item.Plan.Id || WouldCreateReferenceCycle(item.Plan.Id, id)))
        {
            StatusMessage = "不能粘贴：粘贴的内容引用了当前计划，两个计划会互相引用。";
            return;
        }

        InsertTask(item, pasted);
        await SaveAsync(item, "已粘贴；粘贴的任务是独立副本，修改不会影响原任务（共享设置沿用粘贴位置的继承结果）。");
        RebuildNodes();
    }

    /// <summary>启用全部：选中分组时启用其全部后代，否则启用整个计划</summary>
    [RelayCommand]
    private async Task EnableAllAsync()
    {
        if (SelectedPlan is not { } item) return;
        var scope = SelectedNode is { Task: { TaskType: "group", Source: null } group } ? group : item.Plan.RootTask;
        var disabled = Flatten(scope).Where(t => !ReferenceEquals(t, scope) && !t.IsEnabled).ToList();
        if (disabled.Count == 0)
        {
            StatusMessage = "没有已关闭的任务。";
            return;
        }

        foreach (var t in disabled) t.IsEnabled = true;
        await SaveAsync(item, $"已启用 {disabled.Count} 个任务。");
        RebuildNodes();
    }

    private static IEnumerable<PuloniaTask> Flatten(PuloniaTask task) => task.Children.SelectMany(Flatten).Prepend(task);

    /// <summary>撤销当前计划最近一次修改（任务、设置与触发器）</summary>
    [RelayCommand]
    private async Task UndoAsync()
    {
        if (SelectedPlan is not { } item || !_undo.TryGetValue(item.Plan.Id, out var list) || list.Count == 0)
        {
            StatusMessage = "没有可撤销的修改。";
            return;
        }

        var previous = PuloniaTaskJson.ClonePlan(list[^1]);
        list.RemoveAt(list.Count - 1);
        previous.Revision = item.Plan.Revision;
        item.Plan = previous;
        _undoing = true;
        try
        {
            await SaveAsync(item, "已撤销上一步修改。");
        }
        finally
        {
            _undoing = false;
        }

        item.Refresh();
        RebuildNodes();
        RebuildTriggers();
        OnPropertyChanged(nameof(UndoCount));
    }

    /// <summary>预览本次执行（对应 WPF 运行预览窗口）：逐项预测执行或因路线 CD 跳过，可直接开始执行</summary>
    [RelayCommand]
    private async Task PreviewRunAsync()
    {
        if (_tasks is null || SelectedPlan is not { } item) return;
        var typeNames = _tasks.Definitions.GroupBy(d => d.TaskType).ToDictionary(g => g.Key, g => g.First().DisplayName, StringComparer.Ordinal);
        string summary;
        string? error = null;
        try
        {
            var preview = await _tasks.PreviewAsync(new PuloniaTaskRequest { PlanId = item.Plan.Id, Source = "ui" });
            var now = DateTimeOffset.UtcNow;
            LastPreviewRows = preview.Items.Select((x, i) => new PuloniaTaskRunPreviewRow(x, i + 1, typeNames.GetValueOrDefault(x.TaskType) ?? x.TaskType, preview.AccountId, now)).ToList();
            summary = PuloniaTaskRunPreviewRow.Summary(preview, DescribeScope(preview.AccountId));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // 准备失败（资源待确认更新、引用缺失、计划无效等）只在对话框内显示原因
            LastPreviewRows = [];
            summary = "无法预览。";
            error = e.Message;
        }

        var owner = (global::Avalonia.Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null) return;
        var content = new StackPanel { Spacing = 8, MinWidth = 460 };
        content.Children.Add(new TextBlock { Name = "PreviewSummaryText", Text = summary, TextWrapping = TextWrapping.Wrap });
        if (error is not null) content.Children.Add(new TextBlock { Text = error, Foreground = new SolidColorBrush(Color.Parse("#C42B1C")), TextWrapping = TextWrapping.Wrap });
        var rows = new StackPanel { Spacing = 6 };
        foreach (var r in LastPreviewRows)
        {
            var line = new StackPanel { Opacity = r.IsDisabled ? 0.55 : 1 };
            var title = new TextBlock
            {
                Text = $"{r.Index}. {r.Name} · {r.TypeText} — {r.OutcomeText}",
                FontWeight = r.IsSkipped ? FontWeight.Normal : FontWeight.SemiBold,
            };
            // 跳过与需留意的行着色；其余沿用主题前景色（不能设为 null，否则不可见）
            if (r.IsSkipped) title.Foreground = new SolidColorBrush(Color.Parse("#C19C00"));
            else if (r.IsCaution) title.Foreground = new SolidColorBrush(Color.Parse("#E07A00"));
            line.Children.Add(title);
            if (r.DetailText.Length > 0) line.Children.Add(new TextBlock { Text = r.DetailText, FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
            rows.Children.Add(line);
        }

        if (LastPreviewRows.Count == 0 && error is null) rows.Children.Add(new TextBlock { Text = "计划里没有会执行的任务。" });
        content.Children.Add(new ScrollViewer { MaxHeight = 360, Content = rows, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        var dialog = new FAContentDialog
        {
            Title = $"预览本次执行 · {item.Name}", Content = content, CloseButtonText = "关闭",
            PrimaryButtonText = error is null ? "开始执行" : "", DefaultButton = FAContentDialogButton.Close,
        };
        if (await Services.FluentMessageBox.ShowCustomAsync(dialog, owner) == FAContentDialogResult.Primary)
            await RunPlanAsync(item);
    }
}
