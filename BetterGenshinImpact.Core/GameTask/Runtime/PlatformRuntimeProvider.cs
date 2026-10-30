using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.Runtime;

/// <summary>
/// 只依赖 <see cref="IPlatformServices"/> 的运行环境提供者：用平台窗口查找器找游戏窗口，
/// 用平台截图器和输入后端组成运行环境。供非 Windows 宿主（Linux X11 / Wine）使用；
/// Windows 主程序仍使用 Win32RuntimeProvider（含联动启动、HDR、截图方式回退等逻辑）。
/// </summary>
public sealed class PlatformRuntimeProvider(
    IPlatformServices platform,
    IConfigService configService,
    IMaskWindowDrawingBoard maskWindowDrawingBoard,
    IMaskWindowMapState maskWindowMapState,
    ILogger<PlatformRuntimeProvider> logger,
    Func<GameWindowSearch>? windowSearch = null,
    Func<string?>? captureMethod = null,
    Func<IPlatformWindow?>? windowOverride = null)
    : IGameRuntimeProvider
{
    public GameRuntimeKind Kind => GameRuntimeKind.PlatformWindow;

    public Task<GameRuntime?> AcquireAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // 宿主手动选择的窗口优先（找不到原神窗口时使用）
        if (windowOverride?.Invoke() is { } manual)
        {
            if (manual.IsAlive)
            {
                logger.LogInformation("使用手动选择的窗口：{Title}（{Class}，进程 {Pid}）", manual.Title, manual.ClassName, manual.ProcessId);
                return Task.FromResult<GameRuntime?>(AttachTo(manual));
            }

            manual.Dispose();
            logger.LogWarning("手动选择的窗口已关闭，改为按进程名查找");
        }

        var search = (windowSearch ?? GameSystem.GetGameWindowSearch)();
        var found = platform.GameWindows.FindGameWindow(search);
        if (found is null)
        {
            logger.LogError("未找到游戏窗口（进程名：{Names}）", string.Join(", ", search.ProcessNames));
            UiHost.Dialogs.Error("未找到原神窗口，请先启动原神！");
            return Task.FromResult<GameRuntime?>(null);
        }

        return Task.FromResult<GameRuntime?>(AttachTo(found));
    }

    /// <summary>绑定到指定窗口（取得窗口的所有权，失败时释放）</summary>
    public GameRuntime AttachTo(IPlatformWindow platformWindow)
    {
        var window = new PlatformGameWindow(platformWindow);
        IGameCapture? capture = null;
        try
        {
            if (window.IsMinimized)
            {
                throw new ArgumentException("游戏窗口不能最小化");
            }

            window.Activate();
            var systemInfo = new SystemInfo(window.Viewport, maskWindowDrawingBoard);
            var method = captureMethod?.Invoke();
            capture = platform.CreateCapture(method);
            capture.Start(window, GameCaptureSettings.From(configService.Get()));
            logger.LogInformation("已绑定游戏窗口（{Platform}）：{Title}（{Class}，进程 {Pid}），画面 {Rect}，截图方式 {Capture}",
                platform.Name, window.Title, window.ClassName, window.ProcessId, window.Viewport.ScreenRect, capture.Capabilities.Method);
            return new GameRuntime(Kind, window, capture, platform.CreateInput(window), systemInfo,
                maskWindowDrawingBoard, maskWindowMapState);
        }
        catch
        {
            capture?.Dispose();
            window.Dispose();
            throw;
        }
    }

    public void CloseGame(GameRuntime? current) => current?.Window.RequestClose();
}

/// <summary>把平台窗口作为游戏窗口使用（成员全部转发）</summary>
public sealed class PlatformGameWindow(IPlatformWindow inner) : IGameWindow
{
    public IPlatformWindow Inner => inner;

    public nint Handle => inner.Handle;

    public int ProcessId => inner.ProcessId;

    public string Title => inner.Title;

    public string ProcessName => inner.ProcessName;

    public string ClassName => inner.ClassName;

    public GameViewport Viewport => inner.Viewport;

    public bool IsAlive => inner.IsAlive;

    public bool IsForeground => inner.IsForeground;

    public bool IsMinimized => inner.IsMinimized;

    public bool RequiresForeground => inner.RequiresForeground;

    public void Activate() => inner.Activate();

    public void RequestClose() => inner.RequestClose();

    public event EventHandler? ViewportChanged
    {
        add => inner.ViewportChanged += value;
        remove => inner.ViewportChanged -= value;
    }

    public void Dispose() => inner.Dispose();
}
