using BetterGenshinImpact.GameTask.Runtime;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask;

/// <summary>把 <see cref="IGameSystemControl"/> 转发到 Windows 的 <see cref="SystemControl"/> 静态方法（实现未改）</summary>
public sealed class SystemControlAdapter : IGameSystemControl
{
    public static SystemControlAdapter Instance { get; } = new();

    public IPlatformServices Platform => SystemControl.Platform;

    public List<string> GetGenshinGameProcessNameList(IGameWindow? boundWindow) => SystemControl.GetGenshinGameProcessNameList(boundWindow);

    public GameWindowSearch GetGameWindowSearch() => SystemControl.GetGameWindowSearch();

    public nint FindGenshinImpactHandle() => SystemControl.FindGenshinImpactHandle();

    public Task<nint> StartFromLocalAsync(string path) => SystemControl.StartFromLocalAsync(path);

    public string GetActiveByProcess() => SystemControl.GetActiveByProcess();

    public bool IsGameForeground(IGameWindow? window) => SystemControl.IsGameForeground(window);

    public string? GetActiveProcessName() => SystemControl.GetActiveProcessName();

    public Process? GetProcessByHandle(nint hWnd) => SystemControl.GetProcessByHandle(hWnd);

    public PixelSize GetGameScreenSize(nint hWnd)
    {
        var rect = SystemControl.GetGameScreenRect(hWnd);
        return new PixelSize(rect.Width, rect.Height);
    }

    public PixelRect GetCaptureRect(nint hWnd)
    {
        var rect = SystemControl.GetCaptureRect(hWnd);
        return PixelRect.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    public void ActivateWindow(nint hWnd) => SystemControl.ActivateWindow(hWnd);

    public void FocusWindow(nint hWnd) => SystemControl.FocusWindow(hWnd);

    public void MinimizeAndActivateWindow(nint hWnd) => SystemControl.MinimizeAndActivateWindow(hWnd);

    public void RestoreWindow(nint hWnd) => SystemControl.RestoreWindow(hWnd);

    public void CloseGame() => SystemControl.CloseGame();

    public void CloseGameProcesses(IGameWindow? boundWindow) => SystemControl.CloseGameProcesses(boundWindow);

    public void Shutdown() => SystemControl.Shutdown();
}
