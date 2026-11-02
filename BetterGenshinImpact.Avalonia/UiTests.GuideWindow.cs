using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Avalonia.Views.Guide;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Guide;

namespace BetterGenshinImpact.Avalonia;

/// <summary>
/// 悬浮小窗真实窗口（--ui-test guide-window，需要 WebKitGTK 4.1；测试页地址由环境变量 BGI_TEST_GUIDE_URL 给出，本机 HTTP 服务提供带 &lt;video&gt; 的页面）：
/// 打开后显示在主显示器右上角、页面脚本经 WebKit 消息处理器上报状态（标题、有视频）、命令经 window.__bgiGuide.exec 执行（倍速）、
/// 非 http(s) 导航被拦截、窗口栏随鼠标进入显示、字幕浮窗显示页面字幕并以游戏画面为参照、鼠标穿透开关、隐藏与关闭、设置页状态说明
/// </summary>
internal sealed partial class UiTests
{
    private static IEnumerable<Window> AppWindows() =>
        (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows ?? [];

    private async Task GuideWindowAsync()
    {
        var host = vm.Host;
        var url = Environment.GetEnvironmentVariable("BGI_TEST_GUIDE_URL");
        Check("宿主与测试页地址", host is not null && !string.IsNullOrEmpty(url), url);
        if (host is null || string.IsNullOrEmpty(url)) return;
        Check("WebKitGTK 4.1 可用", GuideViews.WebKitAvailable());
        var config = ConfigHost.Config!.GuideWindowConfig;
        var saved = (config.PositionMode, config.FreeLeft, config.FreeTop, config.WindowWidth, config.WindowHeight, config.AutoOpenOnGameStart,
            config.SubtitleWindowEnabled, config.WasOpen, config.HideWhenGameInactive, config.Opacity, config.IsClickThrough);
        var guide = host.Guide;
        try
        {
            config.AutoOpenOnGameStart = GuideAutoOpenMode.Never;
            config.PositionMode = GuidePositionMode.Free;
            config.FreeLeft = null;
            config.FreeTop = null;
            config.WindowWidth = null;
            config.WindowHeight = null;
            config.HideWhenGameInactive = false;
            config.SubtitleWindowEnabled = false;
            config.IsClickThrough = false;
            await Until(() => host.RuntimeService.IsRunning, 30000);
            host.StartGuide();

            // 设置页：WebKitGTK 可用时不显示提示，打开按钮可用
            vm.Navigate("悬浮小窗");
            await Settle(500);
            Check("设置页：可以打开小窗、不显示不支持提示", vm.Guide.CanOpenWindow && Named<Border>("UnsupportedBanner") is null, vm.Guide.CanOpenWindow);
            Check("设置页：状态“小窗未打开”", vm.Guide.WindowStatus == "小窗未打开", vm.Guide.WindowStatus);

            // 1. 打开
            guide.Open(new GuideOpenRequest(url));
            var window = await WaitFor(() => AppWindows().OfType<GuideWindow>().FirstOrDefault(w => w.IsVisible));
            Check("打开后显示 Avalonia 小窗", window is not null);
            if (window is null) return;
            await Settle(500);
            var area = GuideLinuxPlatform.WorkingAreas()[0];
            var scale = GuideLinuxPlatform.Scale();
            Check("默认位置：主显示器右上角、480×270", window.TryGetBounds(out var bounds) && bounds.Right == area.Right - (int)Math.Round(16 * scale)
                                                     && bounds.Top == area.Top + (int)Math.Round(16 * scale) && bounds.Width == (int)Math.Round(480 * scale),
                $"{bounds} 工作区 {area}");
            Check("显示时不抢焦点（主窗口仍在前台或游戏在前台）", !window.IsActive);

            // 2. 页面脚本上报状态
            Check("页面脚本上报状态：标题", await Until(() => guide.State.Title == "BGI 小窗测试页", 15000), guide.State.Title);
            Check("页面脚本上报状态：有视频", window.Bridge.State?.HasVideo == true, window.Bridge.State?.Href);
            Check("当前网址", window.Bridge.CurrentUrl?.StartsWith(url.TrimEnd('/')) == true, window.Bridge.CurrentUrl);
            Check("设置页状态“正在显示”", await Until(() => vm.Guide.WindowStatus == "正在显示", 3000), vm.Guide.WindowStatus);

            // 3. 命令：倍速（JSON 参数经 window.__bgiGuide.exec 执行，页面再上报新的倍速）
            guide.SetRate(1.5);
            Check("命令：倍速 1.5x", await Until(() => window.Bridge.State is { PlaybackRate: 1.5 }, 5000), window.Bridge.State?.PlaybackRate);
            var rate = await window.Bridge.EvalAsync("String(document.querySelector('video').playbackRate)");
            Check("页面中视频倍速已改变", rate?.Contains("1.5") == true, rate);
            var result = await window.Bridge.ExecuteAsync(GuidePlayerCommand.RequestState);
            Check("命令返回结果（ok）", result.Ok, result.Reason);

            // 4. 只允许 http(s) / about: 导航
            await window.Bridge.EvalAsync("location.href = 'file:///etc/hostname'; 'x'");
            await Task.Delay(1500);
            Check("拦截非 http(s) 导航", window.Bridge.CurrentUrl?.StartsWith("http") == true, window.Bridge.CurrentUrl);

            // 5. 窗口栏：鼠标进入后显示
            window.ViewModel.PointerEnterCommand.Execute(null);
            Check("鼠标进入后显示窗口栏", await Until(() => window.ViewModel.IsBarVisible, 3000));
            await Settle(300);
            Check("小窗截图（XGetImage）", ShotX11(window, "guide-window.png"));
            window.ViewModel.PointerLeaveCommand.Execute(null);

            // 6. 字幕浮窗：页面上报字幕后显示，参照区域为游戏画面
            config.SubtitleWindowEnabled = true;
            await window.Bridge.EvalAsync("window.chrome.webview.postMessage({ type: 'subtitle', text: '测试字幕 Subtitle' }); 'ok'");
            var subtitle = await WaitFor(() => AppWindows().OfType<GuideSubtitleWindow>().FirstOrDefault(w => w.IsVisible));
            var game = host.RuntimeService.Current?.Window.Viewport.ScreenRect;
            Check("字幕浮窗显示页面字幕", subtitle is not null && guide.State.SubtitleText == "测试字幕 Subtitle", guide.State.SubtitleText);
            Check("字幕浮窗参照游戏画面", subtitle is not null && game is { } g && subtitle.Anchor == new System.Drawing.Rectangle(g.X, g.Y, g.Width, g.Height), subtitle?.Anchor);
            if (subtitle is not null)
            {
                await Settle(300);
                Check("字幕浮窗截图（XGetImage）", ShotX11(subtitle, "guide-subtitle.png"));
            }

            // 7. 鼠标穿透
            guide.ToggleClickThrough();
            Check("鼠标穿透开启", await Until(() => guide.State.IsClickThrough, 2000));
            guide.ToggleClickThrough();
            Check("鼠标穿透关闭", await Until(() => !guide.State.IsClickThrough, 2000));

            // 8. 隐藏、显示、关闭
            guide.Hide();
            Check("隐藏小窗与字幕浮窗", await Until(() => !window.IsVisible && subtitle?.IsVisible != true, 3000));
            Check("设置页状态“已隐藏”", await Until(() => vm.Guide.WindowStatus == "已隐藏", 3000), vm.Guide.WindowStatus);
            guide.ToggleVisible();
            Check("再次显示", await Until(() => window.IsVisible, 3000));
            guide.Close();
            Check("关闭小窗", await Until(() => !AppWindows().OfType<GuideWindow>().Any(), 5000) && !guide.State.IsOpen);
        }
        finally
        {
            if (guide.State.IsOpen) guide.Close();
            (config.PositionMode, config.FreeLeft, config.FreeTop, config.WindowWidth, config.WindowHeight, config.AutoOpenOnGameStart,
                config.SubtitleWindowEnabled, config.WasOpen, config.HideWhenGameInactive, config.Opacity, config.IsClickThrough) = saved;
        }
    }
}
