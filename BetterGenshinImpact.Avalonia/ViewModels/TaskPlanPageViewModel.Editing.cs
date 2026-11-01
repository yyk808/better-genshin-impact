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

        // 与 WPF 相同记录复制时各叶子的有效参数与子树外层的有效共享设置，粘贴时据此判断继承差异
        IReadOnlyList<PuloniaTask> ancestors = row.Chain.Count > 1 ? row.Chain.Take(row.Chain.Count - 1).ToList() : [item.Plan.RootTask];
        Clipboard.Set(item.Name, row.Task,
            PuloniaPasteInheritance.CaptureEffectiveLeafParameters(row.Task, ancestors, FindDefinition, FindProfile),
            PuloniaPasteInheritance.CaptureOuterSettings(ancestors, FindProfile));
        StatusMessage = $"已复制“{row.Name}”，可在任意计划中粘贴。";
    }

    private PuloniaTaskDefinition? FindDefinition(PuloniaTask task)
        => _tasks is null ? null : PuloniaParameterResolver.FindDefinition(_tasks.Definitions.ToList(), task);

    private PuloniaSettingsProfile? FindProfile(string id) => Profiles.FirstOrDefault(p => p.Profile?.Id == id)?.Profile;

    /// <summary>粘贴目标父节点的模型链（由根到父节点），与 InsertTask 的插入位置一致</summary>
    private IReadOnlyList<PuloniaTask> PasteTargetAncestors(TaskPlanItem item)
        => !IsOneDragon && SelectedNode is { Task.TaskType: "group", Task.Source: null } group && group.Chain.Count > 0
            ? group.Chain : [item.Plan.RootTask];

    /// <summary>粘贴时继承差异询问的结果（自测用）</summary>
    public string? LastPasteChoice { get; private set; }

    /// <summary>
    /// 粘贴为独立副本（新 ID）：位置与“添加”相同。与 WPF 相同，目标位置继承的共享设置会改变有效设置时询问：
    /// 保留复制时的有效设置（不同的项写在粘贴的节点上）、使用目标位置的继承结果，或不粘贴。
    /// </summary>
    [RelayCommand]
    private async Task PasteTaskAsync()
    {
        if (SelectedPlan is not { } item) return;
        if (!Clipboard.TryCreatePaste(out var source, out var pasted, out var effective, out var outer) || source is null || pasted is null)
        {
            StatusMessage = "剪贴板里没有任务，请先复制。";
            return;
        }

        if (ReferencedPlanIds(pasted).Any(id => id == item.Plan.Id || WouldCreateReferenceCycle(item.Plan.Id, id)))
        {
            StatusMessage = "不能粘贴：粘贴的内容引用了当前计划，两个计划会互相引用。";
            return;
        }

        var targetAncestors = PasteTargetAncestors(item);
        LastPasteChoice = null;
        if (PuloniaPasteInheritance.HasDifference(source, pasted, effective, targetAncestors, FindDefinition, FindProfile))
        {
            var owner = (global::Avalonia.Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            var dialog = new FAContentDialog
            {
                Title = "粘贴时发现设置差异",
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap, MaxWidth = 460,
                    Text = $"复制内容来自“{Clipboard.SourcePlanName}”，目标位置继承的共享设置会改变部分有效设置。\n\n" +
                           "选择“是”：保留复制时的有效设置，不同的项写在粘贴的节点上。\n" +
                           "选择“否”：使用目标位置的继承结果。\n选择“取消”：不粘贴。",
                },
                PrimaryButtonText = "是", SecondaryButtonText = "否", CloseButtonText = "取消", DefaultButton = FAContentDialogButton.Close,
            };
            var result = await Services.FluentMessageBox.ShowCustomAsync(dialog, owner);
            LastPasteChoice = result switch { FAContentDialogResult.Primary => "保留", FAContentDialogResult.Secondary => "继承", _ => "取消" };
            if (result == FAContentDialogResult.None)
            {
                StatusMessage = "已取消粘贴。";
                return;
            }
            if (result == FAContentDialogResult.Primary)
                PuloniaPasteInheritance.PreserveCopiedSettings(pasted, outer, targetAncestors, FindDefinition, FindProfile);
        }

        InsertTask(item, pasted);
        await SaveAsync(item, "已粘贴；粘贴的任务是独立副本，修改不会影响原任务。");
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
