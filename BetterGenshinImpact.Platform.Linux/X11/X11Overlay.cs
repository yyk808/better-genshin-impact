using BetterGenshinImpact.Platform.Linux.X11.Native;

namespace BetterGenshinImpact.Platform.Linux.X11;

/// <summary>
/// 遮罩窗口辅助：把窗口的 SHAPE 输入区域设为空，鼠标点击穿透到下面的游戏窗口（对应 Windows 的 WS_EX_TRANSPARENT）。
/// 窗口由界面框架（Avalonia）创建，这里另开一个 X 连接，只修改服务器端的窗口属性。
/// </summary>
public static class X11Overlay
{
    /// <summary>设置点击穿透；失败时返回原因</summary>
    public static string? SetClickThrough(string? display, nint window) => SetClickThrough(display, window, true);

    /// <summary>设置或取消点击穿透（取消时输入区域恢复为整个窗口）；失败时返回原因</summary>
    public static unsafe string? SetClickThrough(string? display, nint window, bool enabled)
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

            if (enabled)
            {
                // 空矩形列表 + ShapeSet：输入区域为空
                Xlib.XShapeCombineRectangles(d, window, Xlib.ShapeInput, 0, 0, 0, 0, Xlib.ShapeSet, Xlib.Unsorted);
            }
            else
            {
                // 一个覆盖整个窗口的矩形（XRectangle：short x, y; ushort width, height）
                var rect = stackalloc ushort[4] { 0, 0, ushort.MaxValue, ushort.MaxValue };
                Xlib.XShapeCombineRectangles(d, window, Xlib.ShapeInput, 0, 0, (nint)rect, 1, Xlib.ShapeSet, Xlib.Unsorted);
            }

            Xlib.XSync(d, false);
            return null;
        }
        finally
        {
            Xlib.XCloseDisplay(d);
        }
    }

    /// <summary>
    /// 把输入区域设为若干矩形（窗口内的物理像素坐标）：矩形内接收鼠标，其余部分点击穿透。
    /// 空列表等同 <see cref="SetClickThrough(string?, nint)"/>。用于遮罩布局编辑（同 WPF：只有可编辑的控件接收鼠标，透明部分仍然穿透）
    /// </summary>
    public static unsafe string? SetInputRegion(string? display, nint window, IReadOnlyList<System.Drawing.Rectangle> rects)
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

            // XRectangle：short x, y; ushort width, height
            var data = new short[Math.Max(1, rects.Count) * 4];
            var n = 0;
            foreach (var r in rects)
            {
                if (r.Width <= 0 || r.Height <= 0) continue;
                data[n * 4] = (short)Math.Clamp(r.X, short.MinValue, short.MaxValue);
                data[n * 4 + 1] = (short)Math.Clamp(r.Y, short.MinValue, short.MaxValue);
                data[n * 4 + 2] = unchecked((short)(ushort)Math.Min(r.Width, ushort.MaxValue));
                data[n * 4 + 3] = unchecked((short)(ushort)Math.Min(r.Height, ushort.MaxValue));
                n++;
            }

            fixed (short* p = data)
            {
                Xlib.XShapeCombineRectangles(d, window, Xlib.ShapeInput, 0, 0, n == 0 ? 0 : (nint)p, n, Xlib.ShapeSet, Xlib.Unsorted);
            }

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
