using BetterGenshinImpact.Core.Input.Backends.WebSdk;
using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Host.Common;

/// <summary>
/// 云原神页面宿主（Linux：Avalonia 宿主里的 WebKitGTK 窗口）。无界面宿主没有实现，云原神模式不可用
/// </summary>
public interface ICloudGamePageHost
{
    /// <summary>
    /// 打开（或复用）云原神窗口并等待进入游戏（SDK 状态：RTC 通道 open 且游戏数据已开始）。
    /// 窗口被关闭时返回 null。可在任意线程调用
    /// </summary>
    Task<CloudGamePage?> OpenAndWaitReadyAsync(string url, CancellationToken ct);

    /// <summary>显示云原神窗口，供用户登录或直接操作页面</summary>
    void ShowForInteraction(string url);

    /// <summary>关闭云原神窗口</summary>
    void Close();
}

/// <summary>已进入游戏的云原神页面</summary>
/// <param name="Handle">承载页面的顶层窗口句柄（X11 窗口 ID），用于截图和窗口几何</param>
/// <param name="Bridge">页面 SDK 调用桥</param>
/// <param name="IsReady">页面仍处于游戏中（窗口未关闭且 SDK 状态就绪）</param>
public sealed record CloudGamePage(nint Handle, IWebInputBridge Bridge, Func<bool> IsReady);

/// <summary>
/// Linux 上的云原神运行环境（对应 Windows 的 WebPageRuntimeProvider）：截图用平台截图器截取承载页面的窗口，
/// 输入用 <see cref="WebSdkInputBackend"/> 调用页面 SDK，不经 XTest，也不要求窗口在前台
/// </summary>
public sealed class CloudWebRuntimeProvider(
    ICloudGamePageHost pageHost,
    IPlatformServices platform,
    IConfigService configService,
    IMaskWindowDrawingBoard maskWindowDrawingBoard,
    IMaskWindowMapState maskWindowMapState,
    ILogger<CloudWebRuntimeProvider> logger,
    Func<string> url,
    Func<string?>? captureMethod = null) : IGameRuntimeProvider
{
    public GameRuntimeKind Kind => GameRuntimeKind.WebPage;

    public async Task<GameRuntime?> AcquireAsync(CancellationToken ct)
    {
        logger.LogInformation("等待云原神就绪：请在云原神窗口中完成登录、排队并进入游戏");
        var page = await pageHost.OpenAndWaitReadyAsync(url(), ct);
        if (page is null)
        {
            logger.LogInformation("云原神窗口已关闭，取消启动截图器");
            return null;
        }

        var inner = platform.Windows.FromHandle(page.Handle) ?? throw new InvalidOperationException("无法打开云原神窗口");
        var window = new CloudWebGameWindow(inner, page.IsReady);
        IGameCapture? capture = null;
        try
        {
            window.Activate();
            var systemInfo = new SystemInfo(window.Viewport, maskWindowDrawingBoard);
            capture = platform.CreateCapture(captureMethod?.Invoke());
            capture.Start(window, GameCaptureSettings.From(configService.Get()));
            var input = new WebSdkInputBackend(page.Bridge, () => window.Viewport.ScreenRect, DesktopSize);
            logger.LogInformation("已绑定云原神窗口：0x{Handle:x}，画面 {Rect}，截图方式 {Capture}，输入 页面 SDK",
                page.Handle, window.Viewport.ScreenRect, capture.Capabilities.Method);
            return new GameRuntime(Kind, window, capture, input, systemInfo, maskWindowDrawingBoard, maskWindowMapState);
        }
        catch
        {
            capture?.Dispose();
            window.Dispose();
            throw;
        }
    }

    /// <summary>与 ClickExtension 等换算 0~65535 绝对坐标时使用的屏幕尺寸一致</summary>
    private static System.Drawing.Size DesktopSize()
    {
        var size = PlatformHost.Screen.PrimaryScreenSize;
        return new System.Drawing.Size(size.Width, size.Height);
    }

    /// <summary>关闭云原神窗口</summary>
    public void CloseGame(GameRuntime? current) => pageHost.Close();
}

/// <summary>
/// 云原神窗口：几何、最小化等转发给平台窗口；存活 = 页面仍在游戏中；输入经页面 SDK，不需要前台；激活只从最小化还原
/// </summary>
public sealed class CloudWebGameWindow(IPlatformWindow inner, Func<bool> isReady) : IGameWindow
{
    public nint Handle => inner.Handle;
    public int ProcessId => inner.ProcessId;
    public string Title => inner.Title;
    public string ProcessName => inner.ProcessName;
    public string ClassName => inner.ClassName;
    public GameViewport Viewport => new(inner.Viewport.ScreenRect, 1f);
    public bool IsAlive => inner.IsAlive && isReady();
    public bool IsForeground => inner.IsForeground;
    public bool IsMinimized => inner.IsMinimized;
    public bool RequiresForeground => false;

    public void Activate()
    {
        if (inner.IsMinimized)
        {
            inner.Activate();
        }
    }

    public void RequestClose() => inner.RequestClose();

    public event EventHandler? ViewportChanged
    {
        add => inner.ViewportChanged += value;
        remove => inner.ViewportChanged -= value;
    }

    public void Dispose() => inner.Dispose();
}

/// <summary>
/// 按 Linux 宿主设置在本机窗口与云原神之间选择运行环境（每次启动截图器时读取设置）
/// </summary>
public sealed class SwitchingRuntimeProvider(IGameRuntimeProvider local, IGameRuntimeProvider? cloud, Func<bool> useCloud) : IGameRuntimeProvider
{
    private IGameRuntimeProvider? _last;

    private IGameRuntimeProvider Current => useCloud() && cloud is not null ? cloud : local;

    public GameRuntimeKind Kind => (_last ?? Current).Kind;

    public Task<GameRuntime?> AcquireAsync(CancellationToken ct)
    {
        if (useCloud() && cloud is null)
        {
            throw new InvalidOperationException("当前宿主不支持云原神（需要图形界面宿主和 WebKitGTK）");
        }

        _last = Current;
        return _last.AcquireAsync(ct);
    }

    public void CloseGame(GameRuntime? current) => (_last ?? Current).CloseGame(current);
}
