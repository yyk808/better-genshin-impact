using System.Collections.Generic;
using System.Linq;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Platform.Windows;

/// <summary>Win32 窗口枚举：EnumWindows 可见顶层窗口</summary>
public sealed class Win32WindowManager : IWindowManager
{
    public IReadOnlyList<IPlatformWindow> EnumerateWindows()
    {
        var list = new List<IPlatformWindow>();
        User32.EnumWindows((hWnd, _) =>
        {
            if (User32.IsWindowVisible(hWnd))
            {
                list.Add(new Win32PlatformWindow((nint)hWnd));
            }

            return true;
        }, default);
        return list;
    }

    public IReadOnlyList<IPlatformWindow> FindWindows(WindowQuery query) =>
        EnumerateWindows()
            .Where(w => query.Matches(w.Title, w.ClassName, w.ProcessId, (w as Win32PlatformWindow)?.ProcessName))
            .ToList();

    public IPlatformWindow? GetForegroundWindow()
    {
        var hWnd = (nint)User32.GetForegroundWindow();
        return hWnd == 0 ? null : new Win32PlatformWindow(hWnd);
    }

    public IPlatformWindow? FromHandle(nint handle) => User32.IsWindow(handle) ? new Win32PlatformWindow(handle) : null;

    public void Dispose()
    {
    }
}
