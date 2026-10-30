using System.Diagnostics;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Platform;

namespace BetterGenshinImpact.Host.Headless;

/// <summary>
/// 只依赖 <see cref="IPlatformServices"/> 的游戏系统操作（找窗、激活、取画面区域等）。
/// 启动游戏、关机等宿主相关的操作在无界面宿主中不支持。
/// </summary>
public sealed class PlatformGameSystemControl(IPlatformServices platform, IReadOnlyList<string> processNames) : IGameSystemControl
{
    public IPlatformServices Platform => platform;

    public List<string> GetGenshinGameProcessNameList(IGameWindow? boundWindow) => [.. processNames];

    public GameWindowSearch GetGameWindowSearch() => new(processNames);

    public nint FindGenshinImpactHandle()
    {
        using var window = platform.GameWindows.FindGameWindow(GetGameWindowSearch());
        return window?.Handle ?? 0;
    }

    public Task<nint> StartFromLocalAsync(string path) =>
        throw new PlatformNotSupportedException("无界面宿主不支持启动游戏，请先手动启动");

    public string GetActiveByProcess() => GetActiveProcessName() ?? string.Empty;

    public bool IsGameForeground(IGameWindow? window) => window?.IsForeground ?? false;

    public string? GetActiveProcessName()
    {
        using var foreground = platform.Windows.GetForegroundWindow();
        return foreground?.ProcessName;
    }

    public Process? GetProcessByHandle(nint hWnd)
    {
        using var window = platform.Windows.FromHandle(hWnd);
        if (window is null || window.ProcessId <= 0)
        {
            return null;
        }

        try
        {
            return Process.GetProcessById(window.ProcessId);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public PixelSize GetGameScreenSize(nint hWnd)
    {
        var rect = GetCaptureRect(hWnd);
        return new PixelSize(rect.Width, rect.Height);
    }

    public PixelRect GetCaptureRect(nint hWnd)
    {
        using var window = platform.Windows.FromHandle(hWnd);
        return window?.Viewport.ScreenRect ?? default;
    }

    public void ActivateWindow(nint hWnd) => WithWindow(hWnd, static w => w.Activate());

    public void FocusWindow(nint hWnd) => WithWindow(hWnd, static w => w.Activate());

    public void MinimizeAndActivateWindow(nint hWnd) => WithWindow(hWnd, static w => w.Activate());

    public void RestoreWindow(nint hWnd) => WithWindow(hWnd, static w => w.Activate());

    public void CloseGame() => WithWindow(FindGenshinImpactHandle(), static w => w.RequestClose());

    public void CloseGameProcesses(IGameWindow? boundWindow) => boundWindow?.RequestClose();

    public void Shutdown() => throw new PlatformNotSupportedException("无界面宿主不支持关机");

    private void WithWindow(nint hWnd, Action<IPlatformWindow> action)
    {
        if (hWnd == 0)
        {
            return;
        }

        using var window = platform.Windows.FromHandle(hWnd);
        if (window is not null)
        {
            action(window);
        }
    }
}
