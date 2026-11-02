using Avalonia.Controls;
using BetterGenshinImpact.Service.Guide;

namespace BetterGenshinImpact.Avalonia.Services;

/// <summary>
/// 悬浮小窗 / 字幕浮窗的窗口工厂（Core 的 GuideWindowService 经依赖注入的 Func 调用）与 Linux 平台信息（显示器工作区、缩放）。
/// 自测可替换工厂
/// </summary>
public static class GuideViews
{
    /// <summary>
    /// 创建悬浮小窗窗口
    /// </summary>
    public static Func<IGuideWindowView> CreateWindow { get; set; } =
        () => throw new NotSupportedException("Linux 版悬浮小窗窗口尚未实现");

    /// <summary>
    /// 创建字幕浮窗
    /// </summary>
    public static Func<IGuideSubtitleView> CreateSubtitle { get; set; } =
        () => throw new NotSupportedException("Linux 版字幕浮窗尚未实现");

    /// <summary>
    /// 用主窗口的屏幕信息提供显示器工作区、整块区域与缩放（物理像素；主显示器排在第一个）。
    /// 在界面线程读取一次，屏幕变化时刷新，其他线程读到的是快照
    /// </summary>
    public static void AttachScreens(TopLevel topLevel)
    {
        System.Drawing.Rectangle[] fallback = [new System.Drawing.Rectangle(0, 0, 1920, 1080)];
        var areas = fallback;
        var bounds = fallback;
        var scale = 1.0;

        void Refresh()
        {
            if (topLevel.Screens is not { } screens || screens.All.Count == 0)
            {
                return;
            }

            var ordered = screens.All.OrderByDescending(screen => screen.IsPrimary).ToArray();
            areas = ordered.Select(screen => ToRect(screen.WorkingArea)).ToArray();
            bounds = ordered.Select(screen => ToRect(screen.Bounds)).ToArray();
            scale = ordered[0].Scaling;
        }

        Refresh();
        if (topLevel.Screens is { } all)
        {
            all.Changed += (_, _) => Refresh();
        }

        GuideLinuxPlatform.WorkingAreas = () => areas;
        GuideLinuxPlatform.ScreenBounds = () => bounds;
        GuideLinuxPlatform.Scale = () => scale;
    }

    private static System.Drawing.Rectangle ToRect(global::Avalonia.PixelRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
}
