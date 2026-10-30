using System;
using System.Diagnostics;
using System.Threading;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Platform.Windows;

/// <summary>
/// 按 HWND 操作窗口的 Win32 函数集合，从 SystemControl 迁移而来，实现保持不变。
/// 平台无关代码应改用 <see cref="IPlatformWindow"/> / <see cref="IWindowManager"/>
/// </summary>
public static class Win32WindowOps
{
    public static nint GetForegroundWindowHandle() => (nint)User32.GetForegroundWindow();

    /// <summary>前台窗口所属进程名，失败为 null</summary>
    public static string? GetActiveProcessName()
    {
        try
        {
            var hWnd = User32.GetForegroundWindow();
            _ = User32.GetWindowThreadProcessId(hWnd, out var pid);
            var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    public static Process? GetProcessByHandle(nint hWnd)
    {
        try
        {
            _ = User32.GetWindowThreadProcessId(hWnd, out var pid);
            var p = Process.GetProcessById((int)pid);
            return p;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return null;
        }
    }

    public static nint FindHandleByWindowName()
    {
        var handle = (nint)User32.FindWindow(GameWindowSearch.UnityWindowClass, "原神");
        if (handle != 0)
        {
            return handle;
        }

        handle = (nint)User32.FindWindow(GameWindowSearch.UnityWindowClass, "Genshin Impact");
        if (handle != 0)
        {
            return handle;
        }

        handle = (nint)User32.FindWindow("Qt5152QWindowIcon", "云·原神");
        if (handle != 0)
        {
            return handle;
        }

        return 0;
    }

    /// <summary>窗口外框位置（DWM 扩展边框，不含阴影）</summary>
    public static RECT GetWindowRect(nint hWnd)
    {
        DwmApi.DwmGetWindowAttribute<RECT>(hWnd, DwmApi.DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, out var windowRect);
        return windowRect;
    }

    /// <summary>游戏本身分辨率（客户区）</summary>
    public static RECT GetGameScreenRect(nint hWnd)
    {
        User32.GetClientRect(hWnd, out var clientRect);
        return clientRect;
    }

    /// <summary>按窗口外框与客户区推算的截图区域（屏幕坐标）</summary>
    public static RECT GetCaptureRect(nint hWnd)
    {
        var windowRect = GetWindowRect(hWnd);
        var gameScreenRect = GetGameScreenRect(hWnd);
        var left = windowRect.Left;
        var top = windowRect.Top + windowRect.Height - gameScreenRect.Height;
        var right = left + gameScreenRect.Width;
        var bottom = top + gameScreenRect.Height;
        return new RECT(left, top, right, bottom);
    }

    public static void ActivateWindow(nint hWnd)
    {
        User32.ShowWindow(hWnd, ShowWindowCommand.SW_RESTORE);
        User32.SetForegroundWindow(hWnd);
    }

    public static void FocusWindow(nint hWnd)
    {
        if (User32.IsWindow(hWnd))
        {
            _ = User32.SendMessage(hWnd, User32.WindowMessage.WM_SYSCOMMAND, User32.SysCommand.SC_RESTORE, 0);
            _ = User32.SetForegroundWindow(hWnd);

            while (User32.IsIconic(hWnd))
            {
                continue;
            }

            _ = User32.BringWindowToTop(hWnd);
            _ = User32.SetActiveWindow(hWnd);
        }
    }

    public static void MinimizeAndActivateWindow(nint hWnd)
    {
        HWND hShell = User32.FindWindow("Shell_TrayWnd", null);
        User32.SendMessage(hShell, 0x0111, (IntPtr)419, IntPtr.Zero);
        Thread.Sleep(500);
        FocusWindow(hWnd);
    }

    public static void RestoreWindow(nint hWnd)
    {
        if (User32.IsWindow(hWnd))
        {
            _ = User32.SendMessage(hWnd, User32.WindowMessage.WM_SYSCOMMAND, User32.SysCommand.SC_RESTORE, 0);
            _ = User32.SetForegroundWindow(hWnd);

            if (User32.IsIconic(hWnd))
            {
                _ = User32.ShowWindow(hWnd, ShowWindowCommand.SW_RESTORE);
            }

            _ = User32.BringWindowToTop(hWnd);
            _ = User32.SetActiveWindow(hWnd);
        }
    }

    public static bool IsFullScreenMode(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        var exStyle = User32.GetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE);

        return (exStyle & (int)User32.WindowStylesEx.WS_EX_TOPMOST) != 0;
    }
}
