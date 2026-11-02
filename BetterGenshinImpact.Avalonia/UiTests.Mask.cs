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
}
