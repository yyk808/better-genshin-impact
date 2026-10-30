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
        var window = new MessageBoxWindow(message, title, kind, buttons);
        var parent = owner();
        if (parent is { IsVisible: true })
        {
            return await window.ShowDialog<DialogResult>(parent);
        }

        var tcs = new TaskCompletionSource<DialogResult>();
        window.Closed += (_, _) => tcs.TrySetResult(window.Result);
        window.Show();
        return await tcs.Task;
    }
}

/// <summary>简单的消息框窗口：图标 + 文本 + 按钮</summary>
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

    private static IEnumerable<(string Label, DialogResult Result)> ButtonsOf(DialogButtons buttons) => buttons switch
    {
        DialogButtons.OkCancel => [("确定", DialogResult.Ok), ("取消", DialogResult.Cancel)],
        DialogButtons.YesNo => [("是", DialogResult.Yes), ("否", DialogResult.No)],
        DialogButtons.YesNoCancel => [("是", DialogResult.Yes), ("否", DialogResult.No), ("取消", DialogResult.Cancel)],
        _ => [("确定", DialogResult.Ok)],
    };
}
