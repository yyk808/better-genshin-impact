using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BetterGenshinImpact.Platform;

namespace BetterGenshinImpact.Avalonia.Services;

/// <summary>
/// UiHost.Dialogs 的 Avalonia 实现：模态消息框 + 主窗口内的轻提示。
/// 同步 Show 在非 UI 线程上等待结果；在 UI 线程上用嵌套消息循环等待（与 WPF ShowDialog 的行为一致）
/// </summary>
public sealed class AvaloniaDialogService(Func<Window?> owner, Action<DialogKind, string> toast) : IDialogService
{
    public DialogResult Show(string message, string title, DialogKind kind, DialogButtons buttons = DialogButtons.Ok)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return Dispatcher.UIThread.InvokeAsync(() => ShowCoreAsync(message, title, kind, buttons)).GetAwaiter().GetResult();
        }

        var task = ShowCoreAsync(message, title, kind, buttons);
        if (!task.IsCompleted)
        {
            using var cts = new CancellationTokenSource();
            task.ContinueWith(_ => cts.Cancel(), TaskScheduler.FromCurrentSynchronizationContext());
            Dispatcher.UIThread.MainLoop(cts.Token);
        }

        return task.GetAwaiter().GetResult();
    }

    public Task<DialogResult> ShowAsync(string message, string title, DialogKind kind, DialogButtons buttons = DialogButtons.Ok) =>
        Dispatcher.UIThread.CheckAccess()
            ? ShowCoreAsync(message, title, kind, buttons)
            : Dispatcher.UIThread.InvokeAsync(() => ShowCoreAsync(message, title, kind, buttons));

    public void Toast(DialogKind kind, string message) => Dispatcher.UIThread.Post(() => toast(kind, message));

    private async Task<DialogResult> ShowCoreAsync(string message, string title, DialogKind kind, DialogButtons buttons)
    {
        var parent = owner();
        if (parent is { IsVisible: true })
        {
            // 有可见的主窗口时用 Fluent 的 ContentDialog（对应 WPF 版 WPF-UI 的 MessageBox）
            return await FluentMessageBox.ShowAsync(parent, message, title, kind, buttons);
        }

        // 主窗口尚未显示（启动早期）时退回独立的消息框窗口
        var window = new MessageBoxWindow(message, title, kind, buttons);

        var tcs = new TaskCompletionSource<DialogResult>();
        window.Closed += (_, _) => tcs.TrySetResult(window.Result);
        window.Show();
        return await tcs.Task;
    }
}

/// <summary>
/// 消息框：FluentAvalonia 的 ContentDialog，显示在所属窗口的覆盖层上（图标 + 文本 + 按钮）。
/// 同一窗口同时只能显示一个 ContentDialog，已有对话框时排队等待。
/// </summary>
public static class FluentMessageBox
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>当前显示的对话框（自检与自动化测试用）</summary>
    public static FluentAvalonia.UI.Controls.FAContentDialog? Current { get; private set; }

    /// <summary>
    /// 输入框（对应 WPF PromptDialog.Prompt）：在主窗口上显示带文本框的 ContentDialog，确定返回输入，取消返回 null
    /// </summary>
    public static async Task<string?> PromptAsync(string message, string title, string initial)
    {
        var owner = (Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null) return null;
        var box = new TextBox { Name = "PromptTextBox", Text = initial, MinWidth = 320 };
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(box);
        var dialog = new FluentAvalonia.UI.Controls.FAContentDialog
        {
            Title = title, Content = content, PrimaryButtonText = "确定", CloseButtonText = "取消",
            DefaultButton = FluentAvalonia.UI.Controls.FAContentDialogButton.Primary,
        };
        dialog.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        await Gate.WaitAsync();
        try
        {
            Current = dialog;
            return await dialog.ShowAsync(owner) == FluentAvalonia.UI.Controls.FAContentDialogResult.Primary ? box.Text ?? "" : null;
        }
        finally
        {
            Current = null;
            Gate.Release();
        }
    }

    /// <summary>显示自定义内容的 ContentDialog（与消息框共用排队，Current 指向它）</summary>
    public static async Task<FluentAvalonia.UI.Controls.FAContentDialogResult> ShowCustomAsync(FluentAvalonia.UI.Controls.FAContentDialog dialog, Window owner)
    {
        await Gate.WaitAsync();
        try
        {
            Current = dialog;
            return await dialog.ShowAsync(owner);
        }
        finally
        {
            Current = null;
            Gate.Release();
        }
    }

    public static async Task<DialogResult> ShowAsync(Window owner, string message, string title, DialogKind kind, DialogButtons buttons)
    {
        var list = MessageBoxWindow.ButtonsOf(buttons).ToList();
        var (glyph, color) = MessageBoxWindow.IconOf(kind);
        var content = new DockPanel { MaxWidth = 460 };
        var icon = new FluentAvalonia.UI.Controls.FAFontIcon
        {
            Glyph = FluentSymbols.Glyph(glyph), FontFamily = FluentSymbols.Font, FontSize = 28,
            Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 14, 0),
        };
        DockPanel.SetDock(icon, Dock.Left);
        content.Children.Add(icon);
        content.Children.Add(new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        var dialog = new FluentAvalonia.UI.Controls.FAContentDialog
        {
            Title = string.IsNullOrEmpty(title) ? "BetterGI" : title,
            Content = content,
            PrimaryButtonText = list[0].Label,
            SecondaryButtonText = list.Count > 2 ? list[1].Label : null,
            CloseButtonText = list.Count > 1 ? list[^1].Label : null,
            DefaultButton = FluentAvalonia.UI.Controls.FAContentDialogButton.Primary,
        };
        // 只有“确定”时，Esc 也按确定处理（与 WPF 消息框相同）
        await Gate.WaitAsync();
        try
        {
            Current = dialog;
            var result = await dialog.ShowAsync(owner);
            return result switch
            {
                FluentAvalonia.UI.Controls.FAContentDialogResult.Primary => list[0].Result,
                FluentAvalonia.UI.Controls.FAContentDialogResult.Secondary when list.Count > 2 => list[1].Result,
                _ => list.Count > 1 ? list[^1].Result : list[0].Result,
            };
        }
        finally
        {
            Current = null;
            Gate.Release();
        }
    }
}

/// <summary>独立的消息框窗口：主窗口尚未显示时使用（图标 + 文本 + 按钮）</summary>
public sealed class MessageBoxWindow : Window
{
    public DialogResult Result { get; private set; } = DialogResult.None;

    /// <summary>以指定结果关闭（按钮与自动化测试共用）</summary>
    public void Close(DialogResult result)
    {
        Result = result;
        base.Close(result);
    }

    public MessageBoxWindow(string message, string title, DialogKind kind, DialogButtons buttons)
    {
        Title = string.IsNullOrEmpty(title) ? "BetterGI" : title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var (glyph, color) = kind switch
        {
            DialogKind.Error => ("✕", Color.Parse("#E5484D")),
            DialogKind.Warning => ("!", Color.Parse("#F5A524")),
            DialogKind.Success => ("✓", Color.Parse("#46A758")),
            DialogKind.Question => ("?", Color.Parse("#3E9EFF")),
            _ => ("i", Color.Parse("#3E9EFF")),
        };

        var icon = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = glyph, FontWeight = FontWeight.Bold, FontSize = 16, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var text = new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 4, 0, 0) };
        var body = new DockPanel { Margin = new Thickness(20, 20, 20, 12) };
        DockPanel.SetDock(icon, Dock.Left);
        body.Children.Add(icon);
        body.Children.Add(text);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8,
            Margin = new Thickness(20, 0, 20, 16),
        };
        foreach (var (label, result) in ButtonsOf(buttons))
        {
            var button = new Button { Content = label, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
            if (result is DialogResult.Ok or DialogResult.Yes)
            {
                button.Classes.Add("accent");
                button.IsDefault = true;
            }

            if (result is DialogResult.Cancel || (buttons == DialogButtons.YesNo && result == DialogResult.No))
            {
                button.IsCancel = true;
            }

            button.Click += (_, _) => Close(result);
            buttonPanel.Children.Add(button);
        }

        var root = new StackPanel();
        root.Children.Add(body);
        root.Children.Add(buttonPanel);
        Content = root;
    }

    /// <summary>ContentDialog 用的图标（Fluent System Icons 名称）与颜色</summary>
    internal static (string Symbol, Color Color) IconOf(DialogKind kind) => kind switch
    {
        DialogKind.Error => ("DismissCircle24", Color.Parse("#E5484D")),
        DialogKind.Warning => ("Warning24", Color.Parse("#F5A524")),
        DialogKind.Success => ("CheckmarkCircle24", Color.Parse("#46A758")),
        DialogKind.Question => ("QuestionCircle24", Color.Parse("#3E9EFF")),
        _ => ("Info24", Color.Parse("#3E9EFF")),
    };

    internal static IEnumerable<(string Label, DialogResult Result)> ButtonsOf(DialogButtons buttons) => buttons switch
    {
        DialogButtons.OkCancel => [("确定", DialogResult.Ok), ("取消", DialogResult.Cancel)],
        DialogButtons.YesNo => [("是", DialogResult.Yes), ("否", DialogResult.No)],
        DialogButtons.YesNoCancel => [("是", DialogResult.Yes), ("否", DialogResult.No), ("取消", DialogResult.Cancel)],
        _ => [("确定", DialogResult.Ok)],
    };
}
