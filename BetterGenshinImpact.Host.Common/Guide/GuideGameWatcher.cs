using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Runtime;

namespace BetterGenshinImpact.Service.Guide;

/// <summary>
/// 监听游戏窗口的前台、最小化与位置变化（Linux 版，与 Windows 版 GuideGameWatcher 的公开成员相同）。
/// X11 / Wayland 没有对应 SetWinEventHook 的跨进程前台事件，改为在界面线程每 200 毫秒比较一次前台窗口与最小化状态，变化时触发
/// <see cref="StateChanged"/>；位置变化直接用游戏窗口的 ViewportChanged
/// </summary>
public sealed class GuideGameWatcher : IDisposable
{
    /// <summary>
    /// 轮询间隔
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    private readonly GuideUiTimer _pollTimer;

    private IGameWindow? _window;

    /// <summary>上次轮询时的前台窗口句柄</summary>
    private nint _lastForeground;

    /// <summary>上次轮询时游戏是否最小化</summary>
    private bool _lastMinimized;

    public GuideGameWatcher()
    {
        _pollTimer = new GuideUiTimer(PollInterval, (_, _) => Poll());
    }

    /// <summary>
    /// 最近一次 BetterGI 以外的前台窗口是否是游戏
    /// </summary>
    public bool LastExternalForegroundWasGame { get; private set; }

    /// <summary>
    /// 是否已挂接游戏窗口
    /// </summary>
    public bool IsAttached => _window is not null;

    /// <summary>
    /// 游戏窗口句柄，未挂接时为 0
    /// </summary>
    public nint GameHandle => _window?.Handle ?? 0;

    /// <summary>
    /// 游戏是否在前台
    /// </summary>
    public bool IsGameForeground => _window is { } window && window.IsForeground;

    /// <summary>
    /// 游戏是否最小化
    /// </summary>
    public bool IsGameMinimized => _window is { } window && window.IsMinimized;

    /// <summary>
    /// 游戏画面区域（物理像素），未挂接或无法获取时返回 false
    /// </summary>
    public bool TryGetGameRect(out System.Drawing.Rectangle rect)
    {
        rect = default;
        if (_window is not { IsAlive: true } window)
        {
            return false;
        }

        var screen = window.Viewport.ScreenRect;
        rect = new System.Drawing.Rectangle(screen.X, screen.Y, screen.Width, screen.Height);
        return rect.Width > 0 && rect.Height > 0;
    }

    /// <summary>
    /// 前台窗口或游戏最小化状态变化
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// 游戏窗口移动或缩放
    /// </summary>
    public event EventHandler? GameMoved;

    /// <summary>
    /// 挂接游戏窗口。在界面线程调用
    /// </summary>
    public void Attach(IGameWindow window)
    {
        Detach();
        _window = window;
        LastExternalForegroundWasGame = window.IsForeground;
        _lastForeground = ForegroundHandle(out _);
        _lastMinimized = window.IsMinimized;
        window.ViewportChanged += OnViewportChanged;
        _pollTimer.Start();
    }

    /// <summary>
    /// 解除挂接。在界面线程调用，重复调用无副作用
    /// </summary>
    public void Detach()
    {
        _pollTimer.Stop();
        if (_window is { } window)
        {
            window.ViewportChanged -= OnViewportChanged;
            _window = null;
        }
    }

    /// <summary>
    /// 前台窗口是否属于 BetterGI 自身（主窗口、交互模式下的小窗等）
    /// </summary>
    public static bool IsForegroundOwnedByBetterGi()
    {
        return ForegroundHandle(out var pid) != 0 && pid == Environment.ProcessId;
    }

    /// <summary>
    /// 把焦点还给游戏窗口。游戏已最小化或窗口已不存在时不处理
    /// </summary>
    public void FocusGame()
    {
        if (_window is not { IsAlive: true } window || window.IsMinimized)
        {
            return;
        }

        window.Activate();
    }

    /// <summary>
    /// 两个窗口是否在同一显示器上（按窗口中心所在的工作区判断）
    /// </summary>
    public static bool IsOnSameMonitor(nint first, nint second)
    {
        if (first == 0 || second == 0)
        {
            return true;
        }

        var a = GuideLinuxPlatform.MonitorOf(first);
        var b = GuideLinuxPlatform.MonitorOf(second);
        return a < 0 || b < 0 || a == b;
    }

    /// <summary>
    /// 当前前台窗口的句柄与进程号，取不到时为 0
    /// </summary>
    private static nint ForegroundHandle(out int processId)
    {
        processId = 0;
        try
        {
            using var foreground = GameSystem.Platform.Windows.GetForegroundWindow();
            if (foreground is null)
            {
                return 0;
            }

            processId = foreground.ProcessId;
            return foreground.Handle;
        }
        catch (Exception)
        {
            // 宿主尚未设置平台服务，或显示服务器连接异常
            return 0;
        }
    }

    /// <summary>
    /// 比较前台窗口与最小化状态，变化时触发 StateChanged（对应 Windows 版的前台 / 最小化系统事件）
    /// </summary>
    private void Poll()
    {
        if (_window is not { } window)
        {
            return;
        }

        var foreground = ForegroundHandle(out var pid);
        var minimized = window.IsMinimized;
        if (foreground == _lastForeground && minimized == _lastMinimized)
        {
            return;
        }

        if (foreground != _lastForeground && foreground != 0 && pid != Environment.ProcessId)
        {
            LastExternalForegroundWasGame = foreground == window.Handle || pid == window.ProcessId;
        }

        _lastForeground = foreground;
        _lastMinimized = minimized;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 游戏窗口移动或缩放：平台层可能在事件线程上触发，统一转到界面线程
    /// </summary>
    private void OnViewportChanged(object? sender, EventArgs e)
    {
        if (BetterGenshinImpact.Platform.UiHost.Dispatcher.CheckAccess())
        {
            GameMoved?.Invoke(this, EventArgs.Empty);
            return;
        }

        BetterGenshinImpact.Platform.UiHost.Dispatcher.Post(() => GameMoved?.Invoke(this, EventArgs.Empty));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Detach();
        _pollTimer.Dispose();
    }
}
