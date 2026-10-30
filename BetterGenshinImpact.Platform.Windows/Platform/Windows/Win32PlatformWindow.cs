using System;
using System.Diagnostics;
using System.Text;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Platform.Windows;

/// <summary>
/// 按 HWND 包装的通用 Win32 窗口对象，用于只有句柄的场景（截图测试、手动选窗、枚举窗口）。
/// <para>
/// 不安装 WinEventHook，所以 <see cref="ViewportChanged"/> 不会触发；需要移动/缩放通知的游戏窗口用主程序的 Win32GameWindow。
/// 客户区按 GetClientRect + ClientToScreen 计算，DPI 缩放按 GetDpiForWindow / 96。
/// </para>
/// </summary>
public class Win32PlatformWindow : IPlatformWindow
{
    public Win32PlatformWindow(nint hWnd)
    {
        Handle = hWnd;
        User32.GetWindowThreadProcessId(hWnd, out var pid);
        ProcessId = (int)pid;
    }

    public nint Handle { get; }

    public int ProcessId { get; }

    public string Title => GetTitle(Handle);

    public string ClassName => GetClassName(Handle);

    public virtual GameViewport Viewport => new(GetClientRectOnScreen(Handle), GetDpiScale(Handle));

    public virtual bool IsAlive => User32.IsWindow(Handle);

    public bool IsForeground => (nint)User32.GetForegroundWindow() == Handle;

    public bool IsMinimized => User32.IsIconic(Handle);

    public virtual bool RequiresForeground => true;

    public virtual void Activate()
    {
        if (User32.IsIconic(Handle))
        {
            User32.ShowWindow(Handle, ShowWindowCommand.SW_RESTORE);
        }

        User32.SetForegroundWindow(Handle);
    }

    public virtual void RequestClose() => User32.PostMessage(Handle, User32.WindowMessage.WM_CLOSE, 0, 0);

    public event EventHandler? ViewportChanged
    {
        add { }
        remove { }
    }

    /// <summary>进程名（不含扩展名）；进程已退出时为 null</summary>
    public string? ProcessName
    {
        get
        {
            try
            {
                using var p = Process.GetProcessById(ProcessId);
                return p.ProcessName;
            }
            catch
            {
                return null;
            }
        }
    }

    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public override string ToString() => $"0x{Handle:X} \"{Title}\" class={ClassName} pid={ProcessId}";

    public static string GetTitle(nint hWnd)
    {
        var len = User32.GetWindowTextLength(hWnd);
        if (len <= 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(len + 1);
        User32.GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetClassName(nint hWnd)
    {
        var sb = new StringBuilder(256);
        return User32.GetClassName(hWnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
    }

    /// <summary>客户区在屏幕上的物理像素矩形</summary>
    public static PixelRect GetClientRectOnScreen(nint hWnd)
    {
        if (!User32.GetClientRect(hWnd, out var rc))
        {
            return default;
        }

        var origin = new POINT(0, 0);
        User32.ClientToScreen(hWnd, ref origin);
        return new PixelRect(origin.X, origin.Y, rc.Width, rc.Height);
    }

    public static float GetDpiScale(nint hWnd)
    {
        var dpi = User32.GetDpiForWindow(hWnd);
        return dpi == 0 ? 1f : dpi / 96f;
    }
}
