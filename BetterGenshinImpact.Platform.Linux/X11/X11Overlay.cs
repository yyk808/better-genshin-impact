using BetterGenshinImpact.Platform.Linux.X11.Native;

namespace BetterGenshinImpact.Platform.Linux.X11;

/// <summary>
/// 遮罩窗口辅助：把窗口的 SHAPE 输入区域设为空，鼠标点击穿透到下面的游戏窗口（对应 Windows 的 WS_EX_TRANSPARENT）。
/// 窗口由界面框架（Avalonia）创建，这里另开一个 X 连接，只修改服务器端的窗口属性。
/// </summary>
public static class X11Overlay
{
    /// <summary>设置点击穿透；失败时返回原因</summary>
    public static string? SetClickThrough(string? display, nint window)
    {
        var d = Xlib.XOpenDisplay(display);
        if (d == 0)
        {
            return $"无法连接 X 显示 {display ?? Environment.GetEnvironmentVariable("DISPLAY")}";
        }

        try
        {
            if (!Xlib.XShapeQueryExtension(d, out _, out _))
            {
                return "X 服务器不支持 SHAPE 扩展";
            }

            // 空矩形列表 + ShapeSet：输入区域为空
            Xlib.XShapeCombineRectangles(d, window, Xlib.ShapeInput, 0, 0, 0, 0, Xlib.ShapeSet, Xlib.Unsorted);
            Xlib.XSync(d, false);
            return null;
        }
        finally
        {
            Xlib.XCloseDisplay(d);
        }
    }

    /// <summary>
    /// 是否有合成器（_NET_WM_CM_S&lt;屏幕&gt; 选择有所有者）。没有合成器时透明窗口显示为黑色，会挡住游戏画面
    /// </summary>
    public static bool HasCompositor(string? display)
    {
        var d = Xlib.XOpenDisplay(display);
        if (d == 0)
        {
            return false;
        }

        try
        {
            var atom = Xlib.XInternAtom(d, $"_NET_WM_CM_S{Xlib.XDefaultScreen(d)}", false);
            return Xlib.XGetSelectionOwner(d, atom) != 0;
        }
        finally
        {
            Xlib.XCloseDisplay(d);
        }
    }
}
