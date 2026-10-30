using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.View.Windows;
using System;
using System.Threading.Tasks;
using System.Windows;
using Wpf.Ui.Violeta.Controls;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxResult = System.Windows.MessageBoxResult;

namespace BetterGenshinImpact.Service.Ui;

/// <summary>WPF 的 UI 线程调度（Application.Current.Dispatcher；应用尚未创建或已退出时与 UIDispatcherHelper 一样忽略）</summary>
public sealed class WpfUiDispatcher : IUiDispatcher
{
    private static System.Windows.Threading.Dispatcher Dispatcher =>
        Application.Current?.Dispatcher ?? throw new InvalidOperationException("WPF 应用未运行");

    public bool CheckAccess() => Dispatcher.CheckAccess();

    public void Invoke(Action action) => Application.Current?.Dispatcher.Invoke(action);

    public T Invoke<T>(Func<T> func) => Application.Current is { } app ? app.Dispatcher.Invoke(func) : default!;

    public void Post(Action action) => _ = Application.Current?.Dispatcher.BeginInvoke(action);

    public Task<T> InvokeAsync<T>(Func<T> func) => Dispatcher.InvokeAsync(func).Task;
}

/// <summary>包装 ThemedMessageBox 与 Wpf.Ui.Violeta 的 Toast</summary>
public sealed class WpfDialogService : IDialogService
{
    public DialogResult Show(string message, string title, DialogKind kind, DialogButtons buttons = DialogButtons.Ok) =>
        FromWpf(ThemedMessageBox.Show(message, title, ToWpf(buttons), ToIcon(kind)));

    public async Task<DialogResult> ShowAsync(string message, string title, DialogKind kind, DialogButtons buttons = DialogButtons.Ok) =>
        FromWpf(await ThemedMessageBox.ShowAsync(message, title, ToWpf(buttons), ToIcon(kind)));

    public void Toast(DialogKind kind, string message)
    {
        switch (kind)
        {
            case DialogKind.Error: Wpf.Ui.Violeta.Controls.Toast.Error(message); break;
            case DialogKind.Warning: Wpf.Ui.Violeta.Controls.Toast.Warning(message); break;
            case DialogKind.Success: Wpf.Ui.Violeta.Controls.Toast.Success(message); break;
            default: Wpf.Ui.Violeta.Controls.Toast.Information(message); break;
        }
    }

    private static ThemedMessageBox.MessageBoxIcon ToIcon(DialogKind kind) => kind switch
    {
        DialogKind.Error => ThemedMessageBox.MessageBoxIcon.Error,
        DialogKind.Warning => ThemedMessageBox.MessageBoxIcon.Warning,
        DialogKind.Success => ThemedMessageBox.MessageBoxIcon.Success,
        DialogKind.Question => ThemedMessageBox.MessageBoxIcon.Question,
        _ => ThemedMessageBox.MessageBoxIcon.Information,
    };

    private static WpfMessageBoxButton ToWpf(DialogButtons b) => b switch
    {
        DialogButtons.OkCancel => WpfMessageBoxButton.OKCancel,
        DialogButtons.YesNo => WpfMessageBoxButton.YesNo,
        DialogButtons.YesNoCancel => WpfMessageBoxButton.YesNoCancel,
        _ => WpfMessageBoxButton.OK,
    };

    private static DialogResult FromWpf(WpfMessageBoxResult r) => r switch
    {
        WpfMessageBoxResult.OK => DialogResult.Ok,
        WpfMessageBoxResult.Cancel => DialogResult.Cancel,
        WpfMessageBoxResult.Yes => DialogResult.Yes,
        WpfMessageBoxResult.No => DialogResult.No,
        _ => DialogResult.None,
    };
}

/// <summary>WPF 剪贴板（在 UI 线程上访问，与原先 UIDispatcherHelper.Invoke(() => Clipboard...) 相同）</summary>
public sealed class WpfClipboardService : IClipboardService
{
    public void SetText(string text) => UIDispatcherHelper.Invoke(() => Clipboard.SetDataObject(text));

    public string? GetText()
    {
        string? text = null;
        UIDispatcherHelper.Invoke(() => text = Clipboard.ContainsText() ? Clipboard.GetText() : null);
        return text;
    }

    public void Clear() => UIDispatcherHelper.Invoke(Clipboard.Clear);
}
