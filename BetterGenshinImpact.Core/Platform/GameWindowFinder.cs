using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.Platform;

/// <summary>
/// 查找游戏窗口的条件
/// </summary>
/// <param name="ProcessNames">候选进程名（不含扩展名，如 YuanShen、GenshinImpact），按优先级排列</param>
/// <param name="PreferWindowClass">
/// 优先按窗口类名查找（对应设置“窗口类名优先检测”）。Win32 实现关闭时按进程名 + 主窗口查找，与旧版行为一致
/// </param>
public sealed record GameWindowSearch(IReadOnlyList<string> ProcessNames, bool PreferWindowClass = false)
{
    /// <summary>原神窗口类名（Unity 游戏窗口）</summary>
    public const string UnityWindowClass = "UnityWndClass";
}

/// <summary>
/// 按进程名 / 窗口类查找游戏窗口。Windows 实现保留原 SystemControl 的查找顺序；
/// 其它平台用 <see cref="WindowManagerGameWindowFinder"/> 在窗口枚举结果中匹配
/// </summary>
public interface IGameWindowFinder
{
    /// <summary>找不到时返回 null</summary>
    IPlatformWindow? FindGameWindow(GameWindowSearch search);
}

/// <summary>
/// 基于 <see cref="IWindowManager"/> 枚举结果的通用查找：
/// 进程名匹配（忽略 .exe 后缀），或窗口类匹配 UnityWndClass / WM_CLASS 中的 "进程名.exe"（Wine 窗口），
/// 多个命中时取客户区面积最大的窗口
/// </summary>
public sealed class WindowManagerGameWindowFinder(IWindowManager windows) : IGameWindowFinder
{
    public IPlatformWindow? FindGameWindow(GameWindowSearch search)
    {
        ArgumentNullException.ThrowIfNull(search);
        var names = search.ProcessNames.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        if (names.Count == 0)
        {
            return null;
        }

        IPlatformWindow? best = null;
        long bestArea = -1;
        foreach (var w in windows.EnumerateWindows())
        {
            if (!IsGameWindow(w, names))
            {
                w.Dispose();
                continue;
            }

            var r = w.Viewport.ScreenRect;
            var area = (long)r.Width * r.Height;
            if (area > bestArea)
            {
                best?.Dispose();
                best = w;
                bestArea = area;
            }
            else
            {
                w.Dispose();
            }
        }

        return best;
    }

    /// <summary>窗口是否属于候选游戏进程</summary>
    public static bool IsGameWindow(IPlatformWindow window, IReadOnlyList<string> processNames)
    {
        var processName = TrimExe(window.ProcessName);
        var className = window.ClassName;
        foreach (var name in processNames)
        {
            if (string.Equals(processName, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // X11 下 Wine 窗口的 WM_CLASS 为 "yuanshen.exe/YuanShen.exe"
            if (className.Contains(name + ".exe", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string TrimExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
