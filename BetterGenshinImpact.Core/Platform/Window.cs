using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Platform;

/// <summary>
/// 游戏画面在屏幕上的区域
/// </summary>
/// <param name="ScreenRect">物理像素矩形，尺寸与截图帧一致（即 SystemInfo.CaptureAreaRect）</param>
/// <param name="DpiScale">输入坐标缩放比例：Win32 为显示器缩放，网页版、X11 固定为 1</param>
public readonly record struct GameViewport(PixelRect ScreenRect, float DpiScale)
{
    public int Width => ScreenRect.Width;

    public int Height => ScreenRect.Height;
}

/// <summary>
/// 平台窗口对象，替代裸的 <c>nint hWnd</c>。
/// <para>
/// <see cref="Handle"/> 只作为不透明标识（Win32 为 HWND，X11 为 XID），平台无关代码不能拿它调用本机 API；
/// 需要句柄的 Windows 实现（截图、PostMessage）可以直接取用。成员都是实时查询，可在任意线程调用。
/// </para>
/// </summary>
public interface IPlatformWindow : IDisposable
{
    /// <summary>窗口本机句柄（Win32 HWND / X11 XID），仅作不透明标识</summary>
    nint Handle { get; }

    /// <summary>窗口所属进程。Wine 下为 wine 进程 pid；网页版为当前进程</summary>
    int ProcessId { get; }

    /// <summary>窗口标题</summary>
    string Title { get; }

    /// <summary>
    /// 窗口类：Win32 为窗口类名（原神为 UnityWndClass）；X11 为 WM_CLASS "instance/class"。
    /// Wine 窗口在 X11 侧看不到 Win32 类名，WM_CLASS 是小写 exe 名（如 yuanshen.exe）
    /// </summary>
    string ClassName { get; }

    /// <summary>客户区（游戏画面）在屏幕上的区域</summary>
    GameViewport Viewport { get; }

    /// <summary>游戏是否仍在运行（进程未退出 / 窗口未销毁）</summary>
    bool IsAlive { get; }

    /// <summary>当前前台（获得输入焦点的顶层）窗口就是本窗口</summary>
    bool IsForeground { get; }

    bool IsMinimized { get; }

    /// <summary>
    /// 操作游戏是否依赖前台。Win32 SendInput / X11 XTest 为 true；网页版为 false
    /// </summary>
    bool RequiresForeground { get; }

    /// <summary>让窗口进入可操作状态：Win32 还原并置前；X11 发送 _NET_ACTIVE_WINDOW</summary>
    void Activate();

    /// <summary>请求窗口关闭（Win32 WM_CLOSE / X11 _NET_CLOSE_WINDOW），不强制结束进程</summary>
    void RequestClose();

    /// <summary>窗口移动或缩放。只做通知，最新值以 <see cref="Viewport"/> 为准；不支持监听的实现可以从不触发</summary>
    event EventHandler? ViewportChanged;
}

/// <summary>
/// 窗口查询条件。字段都为空表示匹配全部；字符串为不区分大小写的包含匹配，多个字段同时给出时要求全部满足。
/// <see cref="ClassName"/> 同时匹配 Win32 类名与 X11 WM_CLASS（Wine 下是小写 exe 名）
/// </summary>
public sealed record WindowQuery(
    string? Title = null,
    string? ClassName = null,
    int? ProcessId = null,
    string? ProcessName = null)
{
    /// <summary>
    /// 解析 "title:原神" "class:UnityWndClass" "pid:123" "proc:YuanShen" 形式；不带前缀时按标题匹配
    /// </summary>
    public static WindowQuery Parse(string s)
    {
        var i = s.IndexOf(':');
        if (i > 0)
        {
            var k = s[..i].ToLowerInvariant();
            var v = s[(i + 1)..];
            switch (k)
            {
                case "title": return new(Title: v);
                case "class": return new(ClassName: v);
                case "pid": return new(ProcessId: int.Parse(v));
                case "proc": return new(ProcessName: v);
            }
        }

        return new(Title: s);
    }

    public bool Matches(string title, string className, int processId, string? processName)
    {
        if (Title is not null && !title.Contains(Title, StringComparison.OrdinalIgnoreCase)) return false;
        if (ClassName is not null && !className.Contains(ClassName, StringComparison.OrdinalIgnoreCase)) return false;
        if (ProcessId is not null && processId != ProcessId) return false;
        if (ProcessName is not null && (processName is null || !processName.Contains(ProcessName, StringComparison.OrdinalIgnoreCase))) return false;
        return true;
    }
}

/// <summary>窗口查找与枚举。Windows 上对应 SystemControl 的找窗部分，X11 上用 _NET_CLIENT_LIST</summary>
public interface IWindowManager : IDisposable
{
    /// <summary>枚举可见的顶层窗口</summary>
    IReadOnlyList<IPlatformWindow> EnumerateWindows();

    IReadOnlyList<IPlatformWindow> FindWindows(WindowQuery query);

    IPlatformWindow? GetForegroundWindow();

    /// <summary>根据本机句柄构造窗口对象（不校验是否为顶层窗口），句柄无效时返回 null</summary>
    IPlatformWindow? FromHandle(nint handle);
}
