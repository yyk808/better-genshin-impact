using Avalonia.Controls;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Platform;

namespace BetterGenshinImpact.Avalonia;

internal sealed partial class UiTests
{
    /// <summary>
    /// 遮罩随游戏焦点显示 / 隐藏（--ui-test mask-focus，需 --auto-start 与假游戏“原神”，另一个窗口标题由环境变量 BGI_TEST_OTHER_WINDOW 给出）：
    /// 规则同 WPF——游戏在前台显示，其他进程在前台隐藏，BetterGI 自身在前台保持；防抖：焦点短暂切走再回来不闪烁
    /// </summary>
    private async Task MaskFocusAsync()
    {
        var host = vm.Host;
        if (host is null) { Check("宿主", false); return; }
        var otherTitle = Environment.GetEnvironmentVariable("BGI_TEST_OTHER_WINDOW") ?? "其他窗口";
        IPlatformWindow? Find(string title) => host.Platform.Windows.FindWindows(new WindowQuery(Title: title)).FirstOrDefault();
        // 游戏窗口取截图器正在使用的窗口（按标题找可能匹配到 BetterGI 自己标题含“原神”的窗口）
        await Until(() => host.RuntimeService.Current is not null, 30000);
        IPlatformWindow? game = host.RuntimeService.Current?.Window;
        var other = Find(otherTitle);
        Check("找到游戏窗口和另一个窗口", game is not null && other is not null, $"{game?.Handle} {other?.Handle}");
        if (game is null || other is null) return;
        var self = window.TryGetPlatformHandle() is { } h ? host.Platform.Windows.FromHandle(h.Handle) : null;

        var mask = await WaitFor(() => MaskWindowController.Current);
        var oldEnabled = host.LinuxConfig.MaskWindowEnabled;
        host.LinuxConfig.MaskWindowEnabled = true;
        try { await MaskFocusCoreAsync(mask, game, other, self, otherTitle); }
        finally { host.LinuxConfig.MaskWindowEnabled = oldEnabled; }
    }

    private async Task MaskFocusCoreAsync(MaskWindowController? mask, IPlatformWindow game, IPlatformWindow other, IPlatformWindow? self, string otherTitle)
    {
        game.Activate();
        Check("游戏在前台：遮罩显示", await Until(() => mask?.IsShown == true, 20000), mask?.IsShown);
        if (mask is null) return;
        Shot("mask-focus-1-game.png");

        var hides = mask.FocusHideCount;
        other.Activate();
        await Task.Delay(120);
        Check("防抖：切到其他窗口 120 ms 内遮罩仍显示", mask.IsShown, $"前台={(other.IsForeground ? otherTitle : "?")}");
        Check("其他进程在前台：遮罩隐藏", await Until(() => !mask.IsShown, 2000) && mask.FocusHideCount == hides + 1, $"{mask.IsShown} hides={mask.FocusHideCount - hides}");
        Shot("mask-focus-2-other.png");

        game.Activate();
        Check("游戏重新获得焦点：遮罩显示", await Until(() => mask.IsShown, 2000), $"{mask.IsShown} 游戏前台={game.IsForeground} 前台进程={PlatformHost.ForegroundProcessId()} 焦点可见={mask.FocusVisible}");

        // 焦点短暂切走（小于防抖时间）再回来：不隐藏
        hides = mask.FocusHideCount;
        other.Activate();
        await Task.Delay(80);
        game.Activate();
        await Task.Delay(900);
        Check("焦点短暂切走再回来：遮罩不闪烁", mask.IsShown && mask.FocusHideCount == hides, $"hides={mask.FocusHideCount - hides}");

        // BetterGI 自身在前台：保持（显示时继续显示）
        if (self is not null)
        {
            self.Activate();
            await Task.Delay(900);
            Check("BetterGI 主窗口在前台：遮罩保持显示", mask.IsShown, self.IsForeground);
            other.Activate();
            await Until(() => !mask.IsShown, 2000);
            self.Activate();
            await Task.Delay(900);
            Check("其他窗口之后切到 BetterGI 主窗口：遮罩保持隐藏（同 WPF）", !mask.IsShown, self.IsForeground);
        }
        else Check("BetterGI 主窗口句柄", false);

        game.Activate();
        Check("最后回到游戏：遮罩显示", await Until(() => mask.IsShown, 2000), mask.IsShown);
        Shot("mask-focus-3-back.png");
    }

    /// <summary>
    /// 遮罩外观与 WPF 一致（--ui-test mask-layout，需 --auto-start 与假游戏，X11 / Wayland 都可跑）：
    /// 状态栏、日志框位置大小 = 配置比例 × 遮罩大小（不小于 WPF 的 MinWidth / MinHeight），圆角 4；
    /// 日志字号 = ComputeEffectiveSize(LogFontSize, LogFontScale, 1080P 比例, 显示缩放)；固定行距（首选字体行距）、最新一行完整显示在框内；
    /// 改字号比例、面板颜色后随即生效；配置事后还原
    /// </summary>
    private async Task MaskLayoutAsync()
    {
        var host = vm.Host;
        if (host is null) { Check("宿主", false); return; }
        await Until(() => host.RuntimeService.Current is not null, 30000);
        var game = host.RuntimeService.Current?.Window;
        var mask = await WaitFor(() => MaskWindowController.Current);
        var c = host.Config.MaskWindowConfig;
        var old = (host.LinuxConfig.MaskWindowEnabled, c.LogFontScale, c.LogPanelBackgroundColor, c.LogPanelBorderThickness, c.OverlayScalingEnabled);
        host.LinuxConfig.MaskWindowEnabled = true;
        try
        {
            game?.Activate();
            Check("遮罩显示", await Until(() => mask?.IsShown == true && mask.Window is not null, 20000), mask?.IsShown);
            if (mask?.Window is not { } mw) return;
            log.Information("UI 自测 mask-layout：遮罩 {Layout}", mask.OverlayLayout);
            await CheckLayout("默认配置");
            Shot("mask-layout-1-default.png");

            c.OverlayScalingEnabled = true;
            c.LogFontScale = 1.5;
            c.LogPanelBackgroundColor = "#90000000";
            c.LogPanelBorderThickness = 2;
            Check("字号比例 1.5 生效", await Until(() => Math.Abs(mw.LogLines.Pitch - Expected().pitch) < 0.01, 3000), $"{mw.LogLines.Pitch:F2} / {Expected().pitch:F2}");
            await CheckLayout("字号 1.5 + 边框 2");
            Shot("mask-layout-2-styled.png");

            (double size, double pitch) Expected()
            {
                var l = mask.OverlayLayout;
                var size = BetterGenshinImpact.Core.Config.MaskWindowConfig.ComputeEffectiveSize(c.LogFontSize, c.LogFontScale, l.ScaleTo1080P, l.DpiScale, c.OverlayScalingEnabled);
                return (size, Views.MaskWindow.PrimaryLineSpacing(Views.MaskWindow.LogFontFamily(c.LogFontFamily)) * size);
            }

            async Task CheckLayout(string name)
            {
                await Settle(400);
                await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { });
                var l = mask.OverlayLayout;
                bool Near(double a, double b) => Math.Abs(a - b) < 0.6;
                var s = mw.StatusBounds;
                Check($"{name}：状态栏位置大小", Near(s.X, c.StatusListLeftRatio * l.Width) && Near(s.Y, c.StatusListTopRatio * l.Height)
                    && Near(s.Width, Math.Max(160, c.StatusListWidthRatio * l.Width)) && Near(s.Height, Math.Max(24, c.StatusListHeightRatio * l.Height)), $"{s} 遮罩 {l.Width:F0}x{l.Height:F0}");
                var g = mw.LogBounds;
                Check($"{name}：日志框位置大小", Near(g.X, c.LogTextBoxLeftRatio * l.Width) && Near(g.Y, c.LogTextBoxTopRatio * l.Height)
                    && Near(g.Width, Math.Max(240, c.LogTextBoxWidthRatio * l.Width)) && Near(g.Height, Math.Max(80, c.LogTextBoxHeightRatio * l.Height)), g);
                var box = mw.FindControl<Border>("LogBox");
                Check($"{name}：圆角 4、边框粗细", box?.CornerRadius.TopLeft == 4 && box.BorderThickness.Left == Math.Max(0, c.LogPanelBorderThickness), $"{box?.CornerRadius} {box?.BorderThickness}");
                var e = Expected();
                var view = mw.LogLines;
                Check($"{name}：行距 = 首选字体行距 × 字号", Near(view.Pitch, e.pitch), $"{view.Pitch:F2} 字号 {e.size:F2}");
                var inner = view.Bounds.Height;
                var full = (int)Math.Floor((inner - Math.Min(5, Math.Max(0, inner - view.TotalLines * view.Pitch))) / view.Pitch + 0.001);
                Check($"{name}：日志有内容、最新行完整显示", view.TotalLines > 0 && view.VisibleLines >= Math.Min(view.TotalLines, full) && view.VisibleLines > 0,
                    $"总 {view.TotalLines} 可见 {view.VisibleLines} 框内可容 {full}");
            }
        }
        finally
        {
            (host.LinuxConfig.MaskWindowEnabled, c.LogFontScale, c.LogPanelBackgroundColor, c.LogPanelBorderThickness, c.OverlayScalingEnabled) = old;
        }
    }
}
