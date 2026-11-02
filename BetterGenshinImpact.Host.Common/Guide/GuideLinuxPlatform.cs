using System.Drawing;

namespace BetterGenshinImpact.Service.Guide;

/// <summary>
/// 悬浮小窗在 Linux 上需要界面框架提供的信息（显示器工作区、缩放、网页运行时），由 Avalonia 宿主启动时设置。
/// 没有界面时（无界面宿主、单元测试）使用默认值：一块 1920×1080 的屏幕、缩放 1、网页不可用
/// </summary>
public static class GuideLinuxPlatform
{
    /// <summary>
    /// 全部显示器的工作区（物理像素），第一个为主显示器
    /// </summary>
    public static Func<Rectangle[]> WorkingAreas { get; set; } = () => [new Rectangle(0, 0, 1920, 1080)];

    /// <summary>
    /// 全部显示器的整块区域（物理像素），顺序与 <see cref="WorkingAreas"/> 一致
    /// </summary>
    public static Func<Rectangle[]> ScreenBounds { get; set; } = () => [new Rectangle(0, 0, 1920, 1080)];

    /// <summary>
    /// 界面单位与物理像素的换算比例
    /// </summary>
    public static Func<double> Scale { get; set; } = () => 1;

    /// <summary>
    /// 网页运行时（WebKitGTK）是否可用
    /// </summary>
    public static Func<bool> IsWebViewAvailable { get; set; } = () => false;

    /// <summary>
    /// 包含该点的工作区下标，都不包含时取最近的一个；没有显示器时为 -1
    /// </summary>
    public static int IndexOf(Rectangle[] areas, int x, int y)
    {
        var best = -1;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < areas.Length; i++)
        {
            var area = areas[i];
            if (area.Contains(x, y))
            {
                return i;
            }

            var dx = x < area.Left ? area.Left - x : x > area.Right ? x - area.Right : 0d;
            var dy = y < area.Top ? area.Top - y : y > area.Bottom ? y - area.Bottom : 0d;
            var distance = dx * dx + dy * dy;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// 窗口中心所在的工作区下标，找不到窗口时为 -1
    /// </summary>
    public static int MonitorOf(nint handle)
    {
        if (handle == 0)
        {
            return -1;
        }

        try
        {
            using var window = BetterGenshinImpact.GameTask.GameSystem.Platform.Windows.FromHandle(handle);
            if (window is null)
            {
                return -1;
            }

            var rect = window.Viewport.ScreenRect;
            return IndexOf(WorkingAreas(), rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        }
        catch (Exception)
        {
            // 宿主尚未设置平台服务，或窗口已不存在
            return -1;
        }
    }
}
