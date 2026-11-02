using BetterGenshinImpact.Platform;

namespace BetterGenshinImpact.Service.Guide;

/// <summary>
/// 悬浮小窗协调服务的 Linux 部分：界面线程与计时器经 <see cref="UiHost"/>，显示器工作区与缩放由 <see cref="GuideLinuxPlatform"/> 提供，
/// 提示用应用内通知，网页运行时为 WebKitGTK
/// </summary>
public sealed partial class GuideWindowService
{
    /// <summary>
    /// 播放中定时保存进度
    /// </summary>
    private GuideUiTimer? _progressTimer;

    /// <summary>
    /// 交互模式下鼠标离开后自动结束
    /// </summary>
    private GuideUiTimer? _interactionExitTimer;

    /// <summary>
    /// 意外获得焦点后，等鼠标松开再把焦点还给游戏
    /// </summary>
    private GuideUiTimer? _focusRestoreTimer;

    /// <summary>
    /// 创建界面线程计时器：进度保存（创建即开始）、交互模式自动结束、焦点恢复（与 Windows 版相同）
    /// </summary>
    private void CreateTimers()
    {
        _progressTimer = new GuideUiTimer(ProgressSaveInterval, (_, _) => SaveProgress(), start: true);
        _interactionExitTimer = new GuideUiTimer(TimeSpan.Zero);
        _interactionExitTimer.Tick += (_, _) =>
        {
            _interactionExitTimer.Stop();
            EndInteractionCore(returnFocus: true);
        };
        _focusRestoreTimer = new GuideUiTimer(TimeSpan.FromMilliseconds(80), OnFocusRestoreTick);
    }

    /// <summary>
    /// 宿主退出时停止计时器（线程池计时器在界面线程结束后不再投递）
    /// </summary>
    public void StopPlatformTimers()
    {
        _progressTimer?.Dispose();
        _interactionExitTimer?.Dispose();
        _focusRestoreTimer?.Dispose();
        _watcher.Dispose();
    }

    /// <summary>
    /// 网页运行时（WebKitGTK）是否可用，不可用时提示用户
    /// </summary>
    private static bool CheckWebViewRuntime()
    {
        if (!GuideLinuxPlatform.IsWebViewAvailable())
        {
            ToastWarning("悬浮小窗需要 WebKitGTK 4.1（Debian / Ubuntu 的 libwebkit2gtk-4.1-0），请安装后重试");
            return false;
        }

        return true;
    }

    private static void ToastWarning(string message) => UiHost.Dialogs.Toast(DialogKind.Warning, message);

    private static void ToastError(string message) => UiHost.Dialogs.Toast(DialogKind.Error, message);

    private static void ToastInformation(string message) => UiHost.Dialogs.Toast(DialogKind.Information, message);

    /// <summary>
    /// 读取剪贴板文本，没有文本时为 null
    /// </summary>
    private static string? ReadClipboardText() => UiHost.Clipboard.GetText() is { Length: > 0 } text ? text : null;

    /// <summary>
    /// 当前前台窗口句柄，取不到时为 0
    /// </summary>
    private static nint GetForegroundWindowHandle()
    {
        try
        {
            using var foreground = BetterGenshinImpact.GameTask.GameSystem.Platform.Windows.GetForegroundWindow();
            return foreground?.Handle ?? 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>
    /// 界面单位与物理像素的换算比例
    /// </summary>
    private static double SystemScale
    {
        get
        {
            var scale = GuideLinuxPlatform.Scale();
            return scale > 0 ? scale : 1;
        }
    }

    /// <summary>
    /// 任务模式变化（在任务线程上触发）：投递到界面线程，开始时暂停并隐藏，结束后按设置恢复
    /// </summary>
    private void OnTaskModeChanged(object? sender, bool inTask)
    {
        UiHost.Dispatcher.Post(() => Invoke(() => ApplyTaskMode(inTask)));
    }

    /// <summary>
    /// 在界面线程执行
    /// </summary>
    private void RunOnUi(Action action)
    {
        if (UiHost.Dispatcher.CheckAccess())
        {
            Invoke(action);
            return;
        }

        UiHost.Dispatcher.Post(() => Invoke(action));
    }

    /// <summary>
    /// 窗口所在显示器的工作区（物理像素），找不到窗口时为主显示器
    /// </summary>
    private static System.Drawing.Rectangle WorkingAreaOf(nint handle)
    {
        var areas = AllWorkingAreas();
        var index = GuideLinuxPlatform.MonitorOf(handle);
        return index >= 0 && index < areas.Length ? areas[index] : areas.Length > 0 ? areas[0] : new System.Drawing.Rectangle(0, 0, 1920, 1080);
    }

    /// <summary>
    /// 全部显示器的工作区（物理像素），第一个为主显示器
    /// </summary>
    private static System.Drawing.Rectangle[] AllWorkingAreas()
    {
        try
        {
            return GuideLinuxPlatform.WorkingAreas();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// 主显示器的工作区（物理像素），没有显示器时为 null
    /// </summary>
    private static System.Drawing.Rectangle? PrimaryWorkingArea() => AllWorkingAreas() is { Length: > 0 } areas ? areas[0] : null;
}
