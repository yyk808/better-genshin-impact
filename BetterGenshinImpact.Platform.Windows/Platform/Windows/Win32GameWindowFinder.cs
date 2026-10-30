using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Platform.Windows;

/// <summary>
/// Win32 游戏窗口查找，从 SystemControl 迁移而来，查找顺序与行为不变：
/// <list type="bullet">
/// <item>默认：按进程名取同会话进程的 MainWindowHandle</item>
/// <item>开启“窗口类名优先检测”时：① 按窗口类名 EnumWindows → ② 白名单进程的最大可见窗口 → ③ 回退默认方式</item>
/// </list>
/// </summary>
public sealed class Win32GameWindowFinder(ILogger? logger = null) : IGameWindowFinder
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    public IPlatformWindow? FindGameWindow(GameWindowSearch search)
    {
        var handle = FindHandle(search);
        return handle == 0 ? null : new Win32PlatformWindow(handle);
    }

    /// <summary>返回窗口句柄，找不到为 0</summary>
    public nint FindHandle(GameWindowSearch search)
    {
        ArgumentNullException.ThrowIfNull(search);
        var processNames = search.ProcessNames;

        if (search.PreferWindowClass)
        {
            // ①优先：按窗口类名 EnumWindows 枚举（不看标题，规避标题变化），同会话优先
            var handle = FindWindowByUnityWndClass(processNames);
            if (handle != 0)
            {
                _logger.LogInformation("[窗口检测] ①按窗口类名枚举命中，句柄={Handle}", handle);
                return handle;
            }

            // ②次选：白名单进程拥有的可见窗口中客户区最大者，同会话优先
            handle = FindLargestVisibleWindowByProcessName(processNames);
            if (handle != 0)
            {
                _logger.LogInformation("[窗口检测] ②按进程枚举最大可见窗口命中，句柄={Handle}", handle);
                return handle;
            }

            // 仍未命中，回退原版逻辑（进程名 + MainWindowHandle）
            handle = FindHandleByProcessName(processNames.ToArray());
            _logger.LogInformation("[窗口检测] 未命中，回退旧逻辑，句柄={Handle}", handle);
            return handle;
        }

        return FindHandleByProcessName(processNames.ToArray());
    }

    /// <summary>
    /// ①优先：按窗口类名 EnumWindows 枚举顶层可见窗口（不看窗口标题，规避标题变化），
    /// 再经 GetWindowThreadProcessId 反查进程名，须在游戏进程名白名单内；
    /// 同会话命中直接返回，否则记住第一个跨会话命中继续枚举（多开/多会话时同会话优先）
    /// </summary>
    private nint FindWindowByUnityWndClass(IEnumerable<string> processNames)
    {
        var nameSet = new HashSet<string>(processNames, StringComparer.OrdinalIgnoreCase);
        var currentSessionId = Process.GetCurrentProcess().SessionId;
        nint found = 0;
        _ = User32.EnumWindows((hWnd, lParam) =>
        {
            if (!User32.IsWindowVisible(hWnd))
            {
                return true;
            }

            var className = Win32PlatformWindow.GetClassName((nint)hWnd);
            if (!string.Equals(className, GameWindowSearch.UnityWindowClass, StringComparison.OrdinalIgnoreCase)
                && !(className.StartsWith("Qt", StringComparison.OrdinalIgnoreCase)
                     && className.EndsWith("QWindowIcon", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            _ = User32.GetWindowThreadProcessId(hWnd, out var pid);
            try
            {
                using var p = Process.GetProcessById((int)pid);
                if (!nameSet.Contains(p.ProcessName))
                {
                    return true;
                }

                if (p.SessionId == currentSessionId)
                {
                    // 同会话命中，直接采用
                    found = (nint)hWnd;
                    return false;
                }
            }
            catch (ArgumentException)
            {
                // pid 已失效（进程已退出），跳过
                return true;
            }
            catch (Exception ex)
            {
                // ArgumentException 已单独接走，落到这里的多为跨会话/提权进程访问被拒
                _logger.LogDebug(ex, "[窗口检测] ①读取窗口进程信息失败（pid={Pid}）", (int)pid);
                return true;
            }

            // 跨会话命中：先记住，继续枚举看有没有同会话的
            if (found == 0)
            {
                found = (nint)hWnd;
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>
    /// ②次选：对游戏进程名白名单内的所有进程（不依赖 MainWindowHandle）
    /// EnumWindows 枚举其名下的可见顶层窗口，取客户区面积最大者；
    /// 存在同会话进程时只在本会话窗口里选（多开/多会话时同会话优先）
    /// </summary>
    private static nint FindLargestVisibleWindowByProcessName(IEnumerable<string> processNames)
    {
        var currentSessionId = Process.GetCurrentProcess().SessionId;
        var pidSet = new HashSet<int>();
        var sameSessionPids = new HashSet<int>();
        foreach (var name in processNames)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    pidSet.Add(p.Id);
                    if (p.SessionId == currentSessionId)
                    {
                        sameSessionPids.Add(p.Id);
                    }
                }
                catch (InvalidOperationException)
                {
                    // 进程已退出，跳过
                }
                finally
                {
                    p.Dispose();
                }
            }
        }

        if (pidSet.Count == 0)
        {
            return 0;
        }

        var effectivePids = sameSessionPids.Count > 0 ? sameSessionPids : pidSet;

        nint best = 0;
        long bestArea = -1;
        _ = User32.EnumWindows((hWnd, lParam) =>
        {
            if (!User32.IsWindowVisible(hWnd))
            {
                return true;
            }

            _ = User32.GetWindowThreadProcessId(hWnd, out var pid);
            if (!effectivePids.Contains((int)pid))
            {
                return true;
            }

            User32.GetClientRect(hWnd, out var rect);
            var area = (long)rect.Right * rect.Bottom;
            if (area > bestArea)
            {
                bestArea = area;
                best = (nint)hWnd;
            }

            return true;
        }, IntPtr.Zero);
        return best;
    }

    /// <summary>按进程名取同会话进程的主窗口句柄（旧版默认方式）</summary>
    public static nint FindHandleByProcessName(params string[] names)
    {
        var currentSessionId = Process.GetCurrentProcess().SessionId;
        foreach (var name in names)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    if (p.SessionId == currentSessionId)
                        return p.MainWindowHandle;
                }
                catch (InvalidOperationException)
                {
                    // 进程已退出，跳过
                }
                finally
                {
                    p.Dispose();
                }
            }
        }

        return 0;
    }
}
