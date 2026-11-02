using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Infrastructure.WebView;
using Vanara.PInvoke;
using Wpf.Ui.Violeta.Controls;
using FormsScreen = System.Windows.Forms.Screen;

namespace BetterGenshinImpact.Service.Guide;

/// <summary>
/// 悬浮小窗协调服务的 Windows 部分：WPF 界面线程与计时器、显示器工作区、前台窗口、提示、WebView2 运行时检查
/// </summary>
public sealed partial class GuideWindowService
{
    /// <summary>
    /// 播放中定时保存进度
    /// </summary>
    private DispatcherTimer? _progressTimer;

    /// <summary>
    /// 交互模式下鼠标离开后自动结束
    /// </summary>
    private DispatcherTimer? _interactionExitTimer;

    /// <summary>
    /// 意外获得焦点后，等鼠标松开再把焦点还给游戏
    /// </summary>
    private DispatcherTimer? _focusRestoreTimer;

    /// <summary>
    /// 创建界面线程计时器：进度保存（创建即开始）、交互模式自动结束、焦点恢复
    /// </summary>
    private void CreateTimers()
    {
        var dispatcher = Application.Current.Dispatcher;
        _progressTimer = new DispatcherTimer(ProgressSaveInterval, DispatcherPriority.Background, (_, _) => SaveProgress(), dispatcher);
        _interactionExitTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
        _interactionExitTimer.Tick += (_, _) =>
        {
            _interactionExitTimer.Stop();
            EndInteractionCore(returnFocus: true);
        };
        _focusRestoreTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Normal, OnFocusRestoreTick, dispatcher);
        _focusRestoreTimer.Stop();
    }

    /// <summary>
    /// 网页运行时是否可用，不可用时提示用户
    /// </summary>
    private static bool CheckWebViewRuntime()
    {
        if (!WebViewRuntime.IsAvailable)
        {
            Toast.Warning("悬浮小窗需要 Microsoft Edge WebView2 运行时，请安装后重试");
            return false;
        }

        return true;
    }

    private static void ToastWarning(string message) => Toast.Warning(message);

    private static void ToastError(string message) => Toast.Error(message);

    private static void ToastInformation(string message) => Toast.Information(message);

    /// <summary>
    /// 读取剪贴板文本，没有文本时为 null；剪贴板被占用时抛出异常
    /// </summary>
    private static string? ReadClipboardText() => Clipboard.ContainsText() ? Clipboard.GetText() : null;

    /// <summary>
    /// 当前前台窗口句柄
    /// </summary>
    private static nint GetForegroundWindowHandle() => (nint)User32.GetForegroundWindow();

    /// <summary>
    /// 系统 DPI 缩放比例：界面单位与物理像素的换算
    /// </summary>
    private static double SystemScale
    {
        get
        {
            var scale = DpiHelper.GetScale().X;
            return scale > 0 ? scale : 1;
        }
    }

    /// <summary>
    /// 任务模式变化（在任务线程上触发）：开始时暂停并隐藏，结束后按设置恢复
    /// </summary>
    private void OnTaskModeChanged(object? sender, bool inTask)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        // 以最高优先级投递，在任务的第一次点击之前隐藏
        _ = dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => ApplyTaskMode(inTask)));
    }

    /// <summary>
    /// 在界面线程执行
    /// </summary>
    private void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            Invoke(action);
            return;
        }

        _ = dispatcher.BeginInvoke(new Action(() => Invoke(action)));
    }

    /// <summary>
    /// 窗口所在显示器的工作区（物理像素）
    /// </summary>
    private static System.Drawing.Rectangle WorkingAreaOf(nint handle) => FormsScreen.FromHandle(handle).WorkingArea;

    /// <summary>
    /// 全部显示器的工作区（物理像素），按系统顺序
    /// </summary>
    private static System.Drawing.Rectangle[] AllWorkingAreas() => FormsScreen.AllScreens.Select(screen => screen.WorkingArea).ToArray();

    /// <summary>
    /// 主显示器的工作区（物理像素），没有主显示器时为 null
    /// </summary>
    private static System.Drawing.Rectangle? PrimaryWorkingArea() => FormsScreen.PrimaryScreen?.WorkingArea;
}
