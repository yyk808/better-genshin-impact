using BetterGenshinImpact.Platform;
using BetterGenshinImpact.GameTask.Runtime;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 游戏进程与窗口的系统操作（查找、启动、激活、关闭游戏等）。Windows 实现为主程序的 SystemControl
/// </summary>
public interface IGameSystemControl
{
    /// <summary>当前平台服务</summary>
    IPlatformServices Platform { get; }

    List<string> GetGenshinGameProcessNameList(IGameWindow? boundWindow);

    GameWindowSearch GetGameWindowSearch();

    nint FindGenshinImpactHandle();

    Task<nint> StartFromLocalAsync(string path);

    string GetActiveByProcess();

    bool IsGameForeground(IGameWindow? window);

    string? GetActiveProcessName();

    Process? GetProcessByHandle(nint hWnd);

    /// <summary>游戏本身的分辨率（窗口客户区大小）</summary>
    PixelSize GetGameScreenSize(nint hWnd);

    void ActivateWindow(nint hWnd);

    void FocusWindow(nint hWnd);

    void MinimizeAndActivateWindow(nint hWnd);

    void RestoreWindow(nint hWnd);

    void CloseGame();

    void CloseGameProcesses(IGameWindow? boundWindow);

    void Shutdown();
}

/// <summary>
/// <see cref="IGameSystemControl"/> 的静态入口，方法名与原 SystemControl 相同，供识别 / 任务代码使用。
/// Windows 主程序在程序集加载时设置 <see cref="Control"/>（转发到 SystemControl，行为不变）
/// </summary>
public static class GameSystem
{
    private static IGameSystemControl? _control;

    public static IGameSystemControl Control
    {
        get => _control ?? throw new InvalidOperationException("宿主尚未设置 GameSystem.Control");
        set => _control = value ?? throw new ArgumentNullException(nameof(value));
    }

    public static IPlatformServices Platform => Control.Platform;

    public static List<string> GetGenshinGameProcessNameList(IGameWindow? boundWindow) => Control.GetGenshinGameProcessNameList(boundWindow);

    public static GameWindowSearch GetGameWindowSearch() => Control.GetGameWindowSearch();

    public static nint FindGenshinImpactHandle() => Control.FindGenshinImpactHandle();

    public static Task<nint> StartFromLocalAsync(string path) => Control.StartFromLocalAsync(path);

    public static string GetActiveByProcess() => Control.GetActiveByProcess();

    public static bool IsGameForeground(IGameWindow? window) => Control.IsGameForeground(window);

    public static string? GetActiveProcessName() => Control.GetActiveProcessName();

    public static Process? GetProcessByHandle(nint hWnd) => Control.GetProcessByHandle(hWnd);

    public static PixelSize GetGameScreenSize(nint hWnd) => Control.GetGameScreenSize(hWnd);

    public static void ActivateWindow(nint hWnd) => Control.ActivateWindow(hWnd);

    public static void FocusWindow(nint hWnd) => Control.FocusWindow(hWnd);

    public static void MinimizeAndActivateWindow(nint hWnd) => Control.MinimizeAndActivateWindow(hWnd);

    public static void RestoreWindow(nint hWnd) => Control.RestoreWindow(hWnd);

    public static void CloseGame() => Control.CloseGame();

    public static void CloseGameProcesses(IGameWindow? boundWindow) => Control.CloseGameProcesses(boundWindow);

    public static void Shutdown() => Control.Shutdown();
}
