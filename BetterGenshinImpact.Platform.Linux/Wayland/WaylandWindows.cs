using BetterGenshinImpact.Platform.Linux.X11;

namespace BetterGenshinImpact.Platform.Linux.Wayland;

/// <summary>
/// Wayland 会话里通过 xwayland-satellite / XWayland 运行的 X 窗口（Wine 游戏就是这种）。
/// 截图、XTest 输入、<see cref="Viewport"/> 都在 X 服务器坐标系内，与 X11 后端相同；
/// 置前与前台判断交给合成器（niri IPC），因为无根 XWayland 里 _NET_ACTIVE_WINDOW 不能让合成器切换焦点
/// </summary>
public sealed class XWaylandWindow(X11Window inner, NiriIpc? niri) : IPlatformWindow
{
    public X11Window Inner { get; } = inner;

    public nint Handle => Inner.Handle;
    public int ProcessId => Inner.ProcessId;
    public string Title => Inner.Title;
    public string ProcessName => Inner.ProcessName;
    public string ClassName => Inner.ClassName;
    public GameViewport Viewport => Inner.Viewport;
    public bool IsAlive => Inner.IsAlive;
    public bool IsMinimized => Inner.IsMinimized;
    public bool RequiresForeground => true;

    /// <summary>对应的 niri 窗口（按标题匹配，X 类名作补充）；没有 niri 或找不到时为 null</summary>
    public NiriWindowInfo? FindNiriWindow()
    {
        if (niri is null) return null;
        try
        {
            var title = Title;
            var cls = ClassName.Split('/');
            var candidates = niri.Windows().Where(w => w.Title == title).ToList();
            return candidates.FirstOrDefault(w => cls.Any(c => c.Length > 0 && w.AppId.Contains(c, StringComparison.OrdinalIgnoreCase)))
                   ?? candidates.FirstOrDefault();
        }
        catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or InvalidOperationException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 游戏客户区在合成器全局坐标中的位置（遮罩、点击换算用）；不可见或无 niri 时为 null。
    /// xwayland-satellite 会给没有声明“无装饰”的 X 窗口在顶部加一条标题栏，niri 报告的窗口比 X 客户区高出这一截，
    /// 所以按 X 客户区大小对齐到 niri 窗口的底部（水平居中）
    /// </summary>
    public PixelRect? ScreenRect => FindNiriWindow()?.ScreenRect is { } r ? ClientRect(r, Inner.Viewport.ScreenRect) : null;

    internal static PixelRect ClientRect(PixelRect niriRect, PixelRect client)
    {
        if (client.Width <= 0 || client.Height <= 0 || client.Width > niriRect.Width || client.Height > niriRect.Height) return niriRect;
        return new PixelRect(niriRect.X + (niriRect.Width - client.Width) / 2, niriRect.Y + niriRect.Height - client.Height, client.Width, client.Height);
    }

    public bool IsForeground => niri is not null ? FindNiriWindow()?.IsFocused ?? false : Inner.IsForeground;

    public void Activate()
    {
        if (FindNiriWindow() is { } w)
        {
            niri!.FocusWindow(w.Id);
        }

        Inner.Activate();
    }

    public void RequestClose() => Inner.RequestClose();

    public event EventHandler? ViewportChanged
    {
        add => Inner.ViewportChanged += value;
        remove => Inner.ViewportChanged -= value;
    }

    public void Dispose() => Inner.Dispose();
}

/// <summary>
/// niri 原生 Wayland 窗口（不经过 X）。<see cref="Handle"/> = niri 窗口 id 加上 <see cref="HandleTag"/>，避免与 XID 冲突；
/// <see cref="Viewport"/> 为合成器全局物理像素坐标，截图只能走 ScreenCast portal，输入走 uinput
/// </summary>
public sealed class NiriWindow(NiriIpc niri, ulong id) : IPlatformWindow
{
    public const long HandleTag = 1L << 40;

    public ulong Id { get; } = id;

    private NiriWindowInfo? Info()
    {
        try
        {
            return niri.Windows().FirstOrDefault(w => w.Id == Id);
        }
        catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or InvalidOperationException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    public nint Handle => (nint)((long)Id | HandleTag);
    public int ProcessId => Info()?.Pid ?? 0;
    public string Title => Info()?.Title ?? "";

    public string ProcessName
    {
        get
        {
            var pid = ProcessId;
            try
            {
                return pid > 0 ? File.ReadAllText($"/proc/{pid}/comm").Trim() : "";
            }
            catch (IOException)
            {
                return "";
            }
        }
    }

    /// <summary>Wayland app_id</summary>
    public string ClassName => Info()?.AppId ?? "";

    public GameViewport Viewport => Info()?.ScreenRect is { } r ? new GameViewport(r, 1f) : default;
    public bool IsAlive => Info() is not null;
    public bool IsForeground => Info()?.IsFocused ?? false;

    /// <summary>niri 没有最小化；不在当前可见区域（其它工作区 / 滚出屏幕）时视为最小化</summary>
    public bool IsMinimized => Info() is { } i && i.ScreenRect is null;

    public bool RequiresForeground => true;

    public void Activate() => niri.FocusWindow(Id);

    public void RequestClose() => niri.CloseWindow(Id);

    public event EventHandler? ViewportChanged
    {
        add { }
        remove { }
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// Wayland 会话的窗口管理：X 显示（xwayland-satellite / XWayland）上的窗口 + niri 上不属于 X 的原生窗口
/// </summary>
public sealed class WaylandWindowManager(X11WindowManager? x11, NiriIpc? niri) : IWindowManager
{
    public IReadOnlyList<IPlatformWindow> EnumerateWindows()
    {
        var list = new List<IPlatformWindow>();
        var xTitles = new HashSet<string>();
        if (x11 is not null)
        {
            foreach (var w in x11.EnumerateWindows().OfType<X11Window>())
            {
                list.Add(new XWaylandWindow(w, niri));
                xTitles.Add(w.Title);
            }
        }

        if (niri is not null)
        {
            try
            {
                // xwayland-satellite 把每个 X 窗口变成一个 Wayland 窗口：标题相同的视为同一个，不重复列出
                list.AddRange(niri.Windows().Where(w => !xTitles.Contains(w.Title)).Select(w => new NiriWindow(niri, w.Id)));
            }
            catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or InvalidOperationException or System.Text.Json.JsonException)
            {
            }
        }

        return list;
    }

    public IReadOnlyList<IPlatformWindow> FindWindows(WindowQuery query) =>
        EnumerateWindows().Where(w => query.Matches(w.Title, w.ClassName, w.ProcessId, w.ProcessName)).ToList();

    public IPlatformWindow? GetForegroundWindow()
    {
        if (niri is not null)
        {
            try
            {
                if (niri.Windows().FirstOrDefault(w => w.IsFocused) is { } focused)
                {
                    return EnumerateWindows().FirstOrDefault(w => w is NiriWindow n ? n.Id == focused.Id : w.Title == focused.Title);
                }
            }
            catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or InvalidOperationException or System.Text.Json.JsonException)
            {
            }
        }

        return x11?.GetForegroundWindow() is X11Window xw ? new XWaylandWindow(xw, niri) : null;
    }

    public IPlatformWindow? FromHandle(nint handle)
    {
        if (handle == 0) return null;
        if (((long)handle & NiriWindow.HandleTag) != 0)
        {
            return niri is null ? null : new NiriWindow(niri, (ulong)((long)handle & ~NiriWindow.HandleTag));
        }

        return x11?.FromHandle(handle) is X11Window w ? new XWaylandWindow(w, niri) : null;
    }

    public void Dispose() => x11?.Dispose();
}
