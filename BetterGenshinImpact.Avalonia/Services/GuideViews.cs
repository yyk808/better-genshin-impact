using System.Runtime.InteropServices;
using Avalonia.Controls;
using BetterGenshinImpact.Avalonia.Views.Guide;
using BetterGenshinImpact.Host.Common;
using BetterGenshinImpact.Service.Guide;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.ViewModel.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace BetterGenshinImpact.Avalonia.Services;

/// <summary>
/// 悬浮小窗 / 字幕浮窗的窗口工厂（GuideWindowService 经依赖注入的 Func 调用）与 Linux 平台信息（显示器工作区、缩放、WebKitGTK）。
/// 自测可替换工厂
/// </summary>
public static class GuideViews
{
    /// <summary>
    /// 创建悬浮小窗窗口
    /// </summary>
    public static Func<IGuideWindowView> CreateWindow { get; set; } =
        () => throw new NotSupportedException("悬浮小窗窗口工厂尚未配置");

    /// <summary>
    /// 创建字幕浮窗
    /// </summary>
    public static Func<IGuideSubtitleView> CreateSubtitle { get; set; } =
        () => throw new NotSupportedException("字幕浮窗窗口工厂尚未配置");

    /// <summary>
    /// 用宿主的服务创建 Avalonia 小窗与字幕浮窗（与 WPF 的 GuideWindow / GuideSubtitleWindow 相同的 ViewModel），
    /// 并按 WebKitGTK 是否可用决定能否打开小窗
    /// </summary>
    /// <param name="host">宿主</param>
    /// <param name="openSettings">窗口栏“全部设置…”：显示主界面并切到悬浮小窗设置页</param>
    public static void Configure(GameHost host, Action openSettings)
    {
        CreateWindow = () => new GuideWindow(new GuideWindowViewModel(host.Guide, host.Services.GetRequiredService<IConfigService>(),
            host.Services.GetRequiredService<GuideLibraryStore>(), openSettings));
        CreateSubtitle = () => new GuideSubtitleWindow(new GuideSubtitleWindowViewModel(host.Guide));
        GuideLinuxPlatform.IsWebViewAvailable = WebKitAvailable;
    }

    private static bool? _webKitAvailable;

    /// <summary>WebKitGTK 4.1 是否可用（与 HTML 遮罩、云原神相同的检测）</summary>
    public static bool WebKitAvailable() => _webKitAvailable ??=
        NativeLibrary.TryLoad("libwebkit2gtk-4.1.so.0", out var handle) && NativeLibrary.TryGetExport(handle, "webkit_web_view_evaluate_javascript", out _);

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
