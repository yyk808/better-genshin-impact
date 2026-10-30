using System;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Platform;

/// <summary>UI 线程调度（WPF Dispatcher / Avalonia Dispatcher.UIThread）</summary>
public interface IUiDispatcher
{
    /// <summary>当前线程是否为 UI 线程</summary>
    bool CheckAccess();

    /// <summary>同步在 UI 线程上执行（已在 UI 线程时直接执行）</summary>
    void Invoke(Action action);

    T Invoke<T>(Func<T> func);

    /// <summary>排队到 UI 线程执行，不等待</summary>
    void Post(Action action);

    /// <summary>在 UI 线程上执行并返回任务（已在 UI 线程时也排队执行）</summary>
    Task<T> InvokeAsync<T>(Func<T> func);
}

/// <summary>对话框图标 / 提示类型</summary>
public enum DialogKind
{
    Information,
    Warning,
    Error,
    Success,
    Question,
}

public enum DialogButtons
{
    Ok,
    OkCancel,
    YesNo,
    YesNoCancel,
}

public enum DialogResult
{
    None,
    Ok,
    Cancel,
    Yes,
    No,
}

/// <summary>
/// 消息框与轻提示（Toast）。WPF 实现包装 ThemedMessageBox / Wpf.Ui.Violeta 的 Toast；
/// 线程要求与被包装的实现相同（消息框内部会切回 UI 线程；Toast 由调用方保证在 UI 线程）
/// </summary>
public interface IDialogService
{
    DialogResult Show(string message, string title, DialogKind kind, DialogButtons buttons = DialogButtons.Ok);

    Task<DialogResult> ShowAsync(string message, string title, DialogKind kind, DialogButtons buttons = DialogButtons.Ok);

    /// <summary>不打断操作的轻提示</summary>
    void Toast(DialogKind kind, string message);
}

/// <summary>文本剪贴板。WPF 实现在 UI 线程上访问剪贴板</summary>
public interface IClipboardService
{
    void SetText(string text);

    /// <summary>剪贴板中没有文本时返回 null</summary>
    string? GetText();

    void Clear();
}

/// <summary>常用的消息框写法，标题默认值与 ThemedMessageBox 相同</summary>
public static class DialogServiceExtensions
{
    public static void Error(this IDialogService d, string message, string title = "错误") => d.Show(message, title, DialogKind.Error);
    public static Task<DialogResult> ErrorAsync(this IDialogService d, string message, string title = "错误") => d.ShowAsync(message, title, DialogKind.Error);
    public static void Warning(this IDialogService d, string message, string title = "警告") => d.Show(message, title, DialogKind.Warning);
    public static Task<DialogResult> WarningAsync(this IDialogService d, string message, string title = "警告") => d.ShowAsync(message, title, DialogKind.Warning);
    public static void Information(this IDialogService d, string message, string title = "信息") => d.Show(message, title, DialogKind.Information);
    public static Task<DialogResult> InformationAsync(this IDialogService d, string message, string title = "信息") => d.ShowAsync(message, title, DialogKind.Information);
}

/// <summary>
/// UI 相关服务的静态入口，供不经依赖注入创建的任务代码使用。宿主（WPF / Avalonia）启动时设置；
/// 未设置时为无界面实现：调度直接在当前线程执行，消息框只写日志并返回 None，剪贴板为进程内存
/// </summary>
public static class UiHost
{
    private static IUiDispatcher _dispatcher = new InlineUiDispatcher();
    private static IDialogService _dialogs = new HeadlessDialogService();
    private static IClipboardService _clipboard = new InMemoryClipboard();
    private static IAppLifetime _lifetime = new HeadlessAppLifetime();

    public static IUiDispatcher Dispatcher
    {
        get => _dispatcher;
        set => _dispatcher = value ?? throw new ArgumentNullException(nameof(value));
    }

    public static IDialogService Dialogs
    {
        get => _dialogs;
        set => _dialogs = value ?? throw new ArgumentNullException(nameof(value));
    }

    public static IClipboardService Clipboard
    {
        get => _clipboard;
        set => _clipboard = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>应用程序生命周期（一条龙完成后“关闭软件”等）</summary>
    public static IAppLifetime Lifetime
    {
        get => _lifetime;
        set => _lifetime = value ?? throw new ArgumentNullException(nameof(value));
    }
}

/// <summary>应用程序生命周期。WPF 实现在 UI 线程调用 Application.Current.Shutdown()</summary>
public interface IAppLifetime
{
    /// <summary>请求退出程序（可从任意线程调用）</summary>
    void Shutdown();
}

/// <summary>无界面宿主：只记录请求（由宿主自己决定何时退出）</summary>
public sealed class HeadlessAppLifetime : IAppLifetime
{
    public event Action? ShutdownRequested;

    public void Shutdown() => ShutdownRequested?.Invoke();
}

/// <summary>无 UI 线程时的调度：全部在当前线程执行</summary>
public sealed class InlineUiDispatcher : IUiDispatcher
{
    public bool CheckAccess() => true;
    public void Invoke(Action action) => action();
    public T Invoke<T>(Func<T> func) => func();
    public void Post(Action action) => action();

    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        try
        {
            return Task.FromResult(func());
        }
        catch (Exception e)
        {
            return Task.FromException<T>(e);
        }
    }
}

/// <summary>无界面时的消息框：记录最近的消息（便于测试），结果为 None</summary>
public sealed class HeadlessDialogService : IDialogService
{
    public event Action<DialogKind, string, string>? Shown;

    public DialogResult Show(string message, string title, DialogKind kind, DialogButtons buttons = DialogButtons.Ok)
    {
        Shown?.Invoke(kind, title, message);
        return DialogResult.None;
    }

    public Task<DialogResult> ShowAsync(string message, string title, DialogKind kind, DialogButtons buttons = DialogButtons.Ok) =>
        Task.FromResult(Show(message, title, kind, buttons));

    public void Toast(DialogKind kind, string message) => Shown?.Invoke(kind, "", message);
}

/// <summary>进程内的剪贴板（无界面 / 测试用）</summary>
public sealed class InMemoryClipboard : IClipboardService
{
    private volatile string? _text;
    public void SetText(string text) => _text = text;
    public string? GetText() => _text;
    public void Clear() => _text = null;
}
