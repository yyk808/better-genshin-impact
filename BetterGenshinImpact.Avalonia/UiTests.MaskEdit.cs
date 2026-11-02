using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BetterGenshinImpact.Avalonia.Services;

namespace BetterGenshinImpact.Avalonia;

internal sealed partial class UiTests
{
    /// <summary>
    /// 遮罩布局编辑（--ui-test mask-edit，需 --auto-start 与假游戏；X11 与 Wayland 覆盖层都可跑）：
    /// 设置页开关进入编辑模式 → 提示文字、半透明白底、虚线框；只有状态栏 / 日志框接收鼠标（其余仍穿透到游戏）；
    /// 用 xdotool 发真实鼠标事件：拖动日志框移动、拖状态栏右下角改变大小、改到比最小尺寸还小时停在最小尺寸，松开后比例写回配置；
    /// 右键控件退出编辑模式、输入区域恢复为空；“重置位置”恢复默认比例。
    /// 鼠标所在 X 显示默认取 DISPLAY；Wayland（嵌套 niri）用 BGI_TEST_POINTER_DISPLAY 指向 niri 的宿主 X 显示（输出在其左上角、缩放 1）。
    /// 配置事后还原
    /// </summary>
    private async Task MaskEditAsync()
    {
        var host = vm.Host;
        if (host is null) { Check("宿主", false); return; }
        await Until(() => host.RuntimeService.Current is not null, 30000);
        var game = host.RuntimeService.Current?.Window;
        var mask = await WaitFor(() => MaskWindowController.Current);
        var c = host.Config.MaskWindowConfig;
        var old = (host.LinuxConfig.MaskWindowEnabled, c.ShowLogBox, c.ShowStatus,
            c.StatusListLeftRatio, c.StatusListTopRatio, c.StatusListWidthRatio, c.StatusListHeightRatio,
            c.LogTextBoxLeftRatio, c.LogTextBoxTopRatio, c.LogTextBoxWidthRatio, c.LogTextBoxHeightRatio);
        host.LinuxConfig.MaskWindowEnabled = true;
        c.ShowLogBox = true;
        c.ShowStatus = true;
        try
        {
            Check("启动时不在编辑模式（同 WPF 启动时关闭）", !c.OverlayLayoutEditEnabled && mask?.Editing == false);
            game?.Activate();
            Check("遮罩显示", await Until(() => mask?.IsShown == true && mask.Window is not null, 20000), mask?.IsShown);
            if (mask?.Window is not { } mw) return;
            await MaskEditCoreAsync(mask, mw, c);
        }
        finally
        {
            c.OverlayLayoutEditEnabled = false;
            (host.LinuxConfig.MaskWindowEnabled, c.ShowLogBox, c.ShowStatus,
                c.StatusListLeftRatio, c.StatusListTopRatio, c.StatusListWidthRatio, c.StatusListHeightRatio,
                c.LogTextBoxLeftRatio, c.LogTextBoxTopRatio, c.LogTextBoxWidthRatio, c.LogTextBoxHeightRatio) = old;
        }
    }

    private async Task MaskEditCoreAsync(MaskWindowController mask, Views.MaskWindow mw, BetterGenshinImpact.Core.Config.MaskWindowConfig c)
    {
        var wayland = mask.OverlayPlacement is not null;
        var pointerDisplay = Environment.GetEnvironmentVariable("BGI_TEST_POINTER_DISPLAY") ?? Environment.GetEnvironmentVariable("DISPLAY") ?? ":0";
        string Xdo(string args)
        {
            var psi = new System.Diagnostics.ProcessStartInfo("xdotool", args) { UseShellExecute = false, RedirectStandardOutput = true };
            psi.Environment["DISPLAY"] = pointerDisplay;
            using var proc = System.Diagnostics.Process.Start(psi);
            var output = proc?.StandardOutput.ReadToEnd() ?? "";
            proc?.WaitForExit(10000);
            return output;
        }

        // 遮罩 DIP → 屏幕像素
        PixelPoint Screen(Point p)
        {
            if (mask.OverlayPlacement is { } o) return new PixelPoint(o.X + (int)Math.Round(p.X), o.Y + (int)Math.Round(p.Y));
            var s = mw.RenderScaling;
            return new PixelPoint(mw.Position.X + (int)Math.Round(p.X * s), mw.Position.Y + (int)Math.Round(p.Y * s));
        }

        // X11：指针下的窗口是不是遮罩（SHAPE 输入区域之外的点落到下面的游戏窗口）
        bool MaskUnder(Point p)
        {
            var sp = Screen(p);
            Xdo($"mousemove {sp.X} {sp.Y}");
            var line = Xdo("getmouselocation --shell").Split('\n').FirstOrDefault(l => l.StartsWith("WINDOW="));
            return line is not null && long.TryParse(line[7..], out var id) && id == mask.Handle;
        }

        async Task Drag(Point from, Point to, int button = 1)
        {
            var a = Screen(from);
            var b = Screen(to);
            Xdo($"mousemove {a.X} {a.Y}");
            await Task.Delay(150);
            Xdo($"mousedown {button}");
            await Task.Delay(100);
            for (var i = 1; i <= 8; i++)
            {
                Xdo($"mousemove {a.X + (b.X - a.X) * i / 8} {a.Y + (b.Y - a.Y) * i / 8}");
                await Task.Delay(40);
            }

            Xdo($"mouseup {button}");
            await Task.Delay(300);
            await Dispatcher.UIThread.InvokeAsync(() => { });
        }

        static Point Center(Rect r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);
        var (w, h, _, _) = mask.OverlayLayout;
        var blank = new Point(w * 0.7, h * 0.3);
        if (!wayland)
        {
            Check("编辑前：状态栏 / 日志框也点击穿透", !MaskUnder(Center(mw.LogBounds)) && !MaskUnder(Center(mw.StatusBounds)));
        }

        // 设置页开关
        vm.Navigate("设置");
        await Settle(600);
        foreach (var ex in All<FluentAvalonia.UI.Controls.FASettingsExpander>()) ex.IsExpanded = true;
        await Settle(600);
        var toggle = await WaitFor(() => Named<ToggleSwitch>("MaskLayoutEditToggle"));
        if (toggle is null)
        {
            var any = All<ToggleSwitch>().FirstOrDefault(t => t.Name == "MaskLayoutEditToggle");
            log.Information("UI 自测 mask-edit：开关 {Found} 可见 {Visible}，祖先 {Chain}", any is not null, any?.IsEffectivelyVisible,
                any is null ? "" : string.Join(" < ", any.GetVisualAncestors().OfType<Control>().Take(12).Select(a => $"{a.GetType().Name}{(a.IsVisible ? "" : "(隐藏)")}")));
        }
        Check("设置页有“启用拖拽调整位置大小”开关与“重置位置”按钮", toggle is not null && Named<Button>("ResetMaskLayoutButton") is not null);
        if (toggle is null) return;
        toggle.IsChecked = true;
        Check("打开开关：进入编辑模式", await Until(() => mw.IsEditing && c.OverlayLayoutEditEnabled, 3000), mw.IsEditing);
        await Task.Delay(700);
        Check("BetterGI 在前台时遮罩仍显示（编辑模式强制显示）", mask.IsShown);
        var banner = mw.FindControl<StackPanel>("EditBanner");
        var outline = mw.FindControl<Rectangle>("LogOutline");
        var logBox = mw.FindControl<Border>("LogBox");
        Check("编辑提示、虚线框、半透明白底", banner?.IsVisible == true && outline?.IsVisible == true && outline.StrokeDashArray?.Count == 2
            && logBox?.Background is ISolidColorBrush { Color: var bg, Opacity: var op } && bg == Colors.White && Math.Abs(op - 0.55) < 0.01,
            $"{banner?.IsVisible} {outline?.IsVisible} {logBox?.Background}");
        Check("输入区域 = 状态栏 + 日志框", mw.EditableRegions.Count == 2, string.Join(" ", mw.EditableRegions));
        if (!wayland)
        {
            Check("编辑中：日志框、状态栏接收鼠标，空白处仍穿透", MaskUnder(Center(mw.LogBounds)) && MaskUnder(Center(mw.StatusBounds)) && !MaskUnder(blank));
            Shot(mw, "mask-edit-1-on.png");
        }

        // 拖动日志框
        var log0 = mw.LogBounds;
        await Drag(Center(log0), Center(log0) + new Point(600, -60));
        var log1 = mw.LogBounds;
        bool Near(double a, double b, double tol = 3) => Math.Abs(a - b) <= tol;
        Check("拖动日志框：移动 (+600, -60)、大小不变", Near(log1.X, log0.X + 600) && Near(log1.Y, log0.Y - 60) && Near(log1.Width, log0.Width) && Near(log1.Height, log0.Height),
            $"{log0} → {log1}");
        Check("松开后写回比例（同 WPF：位置 / 遮罩大小）", Near(c.LogTextBoxLeftRatio * w, log1.X, 1) && Near(c.LogTextBoxTopRatio * h, log1.Y, 1)
            && Near(c.LogTextBoxWidthRatio * w, log1.Width, 1), $"{c.LogTextBoxLeftRatio:F4},{c.LogTextBoxTopRatio:F4} 遮罩 {w:F0}x{h:F0}");

        // 状态栏右下角改变大小
        var st0 = mw.StatusBounds;
        await Drag(new Point(st0.Right - 2, st0.Bottom - 2), new Point(st0.Right + 80, st0.Bottom + 20));
        var st1 = mw.StatusBounds;
        Check("拖状态栏右下角：宽 +80、高 +20、位置不变", Near(st1.Width, st0.Width + 80) && Near(st1.Height, st0.Height + 20) && Near(st1.X, st0.X) && Near(st1.Y, st0.Y),
            $"{st0} → {st1}");
        Check("状态栏比例写回", Near(c.StatusListWidthRatio * w, st1.Width, 1) && Near(c.StatusListHeightRatio * h, st1.Height, 1));

        // 左边往右拖过头：停在最小宽度（日志框 240），右边不动
        var l2 = mw.LogBounds;
        await Drag(new Point(l2.X + 2, l2.Y + l2.Height / 2), new Point(l2.Right + 200, l2.Y + l2.Height / 2));
        var l3 = mw.LogBounds;
        Check("改到比最小宽度还小：停在 240、右边不动", Near(l3.Width, 240, 1) && Near(l3.Right, l2.Right, 3), $"{l2} → {l3}");
        if (!wayland)
        {
            Check("输入区域跟随新位置", MaskUnder(Center(mw.LogBounds)) && MaskUnder(Center(mw.StatusBounds)), string.Join(" ", mw.EditableRegions));
            Shot(mw, "mask-edit-2-moved.png");
        }

        // 右键控件退出
        var right = Screen(Center(mw.StatusBounds));
        Xdo($"mousemove {right.X} {right.Y}");
        await Task.Delay(150);
        Xdo("click 3");
        Check("右键控件：退出编辑模式", await Until(() => !c.OverlayLayoutEditEnabled && !mw.IsEditing, 3000), c.OverlayLayoutEditEnabled);
        await Task.Delay(700);
        Check("退出后：提示与虚线框隐藏，输入区域为空", banner?.IsVisible == false && outline?.IsVisible == false && mw.EditableRegions.Count == 0);
        Check("退出后开关同步关闭", toggle.IsChecked == false);
        if (!wayland)
        {
            Check("退出后：日志框重新点击穿透", !MaskUnder(Center(mw.LogBounds)) && !MaskUnder(Center(mw.StatusBounds)));
        }

        // 重置位置
        Named<Button>("ResetMaskLayoutButton")?.Command?.Execute(null);
        await Task.Delay(800);
        Check("重置位置：恢复默认比例", Near(c.LogTextBoxLeftRatio, 20.0 / 1920, 1e-6) && Near(c.LogTextBoxTopRatio, 832.0 / 1080, 1e-6)
            && Near(c.StatusListWidthRatio, 477.0 / 1920, 1e-6) && Near(mw.LogBounds.Y, 832.0 / 1080 * h, 1), $"{mw.LogBounds}");
    }
}
