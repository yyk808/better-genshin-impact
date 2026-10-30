using System;
using System.Diagnostics;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 任务代码用到的桌面系统操作（枚举游戏进程的附属窗口、读取进程映像路径、阻止系统休眠）。
/// Windows 实现为主程序的 Win32GameDesktopInterop（原样调用 User32 / Kernel32）；未设置时为空实现
/// </summary>
public interface IGameDesktopInterop
{
    /// <summary>
    /// 查找属于游戏进程的 B 服协议 / 登录窗口。返回窗口句柄（找不到为 0）与类型（"协议" / "登录" / ""）
    /// </summary>
    /// <param name="process">游戏进程</param>
    /// <param name="onEnumError">枚举某个窗口出错时的回调（调用方各自记录日志）</param>
    (IntPtr windowHandle, string windowType) FindBiliLoginWindow(Process? process, Action<Exception> onEnumError);

    /// <summary>读取窗口所属进程的完整映像路径，失败时返回 null</summary>
    string? QueryProcessImagePathOfWindow(IntPtr windowHandle);

    /// <summary>true：阻止系统休眠与关闭显示器；false：恢复默认</summary>
    void SetKeepSystemAwake(bool keepAwake);
}

public static class GameDesktopInterop
{
    private static IGameDesktopInterop _current = NullGameDesktopInterop.Instance;

    public static IGameDesktopInterop Current
    {
        get => _current;
        set => _current = value ?? throw new ArgumentNullException(nameof(value));
    }
}

/// <summary>没有桌面系统操作的实现：找不到窗口，读不到映像路径，不改变休眠设置</summary>
public sealed class NullGameDesktopInterop : IGameDesktopInterop
{
    public static NullGameDesktopInterop Instance { get; } = new();

    public (IntPtr windowHandle, string windowType) FindBiliLoginWindow(Process? process, Action<Exception> onEnumError) => (IntPtr.Zero, "");

    public string? QueryProcessImagePathOfWindow(IntPtr windowHandle) => null;

    public void SetKeepSystemAwake(bool keepAwake)
    {
    }
}
