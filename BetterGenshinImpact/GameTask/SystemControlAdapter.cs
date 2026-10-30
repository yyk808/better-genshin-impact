using BetterGenshinImpact.GameTask.Runtime;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask;

/// <summary>把 <see cref="IGameSystemControl"/> 转发到 Windows 的 <see cref="SystemControl"/> 静态方法（实现未改）</summary>
public sealed class SystemControlAdapter : IGameSystemControl
{
    public static SystemControlAdapter Instance { get; } = new();

    public IPlatformServices Platform => GameSystem.Platform;

    public List<string> GetGenshinGameProcessNameList(IGameWindow? boundWindow) => GameSystem.GetGenshinGameProcessNameList(boundWindow);

    public GameWindowSearch GetGameWindowSearch() => GameSystem.GetGameWindowSearch();

    public nint FindGenshinImpactHandle() => GameSystem.FindGenshinImpactHandle();

    public Task<nint> StartFromLocalAsync(string path) => GameSystem.StartFromLocalAsync(path);

    public string GetActiveByProcess() => GameSystem.GetActiveByProcess();

    public bool IsGameForeground(IGameWindow? window) => GameSystem.IsGameForeground(window);

    public string? GetActiveProcessName() => GameSystem.GetActiveProcessName();

    public Process? GetProcessByHandle(nint hWnd) => GameSystem.GetProcessByHandle(hWnd);

    public void ActivateWindow(nint hWnd) => GameSystem.ActivateWindow(hWnd);

    public void FocusWindow(nint hWnd) => GameSystem.FocusWindow(hWnd);

    public void MinimizeAndActivateWindow(nint hWnd) => GameSystem.MinimizeAndActivateWindow(hWnd);

    public void RestoreWindow(nint hWnd) => GameSystem.RestoreWindow(hWnd);

    public void CloseGame() => GameSystem.CloseGame();

    public void CloseGameProcesses(IGameWindow? boundWindow) => GameSystem.CloseGameProcesses(boundWindow);

    public void Shutdown() => GameSystem.Shutdown();
}
