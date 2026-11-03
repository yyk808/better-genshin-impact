using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.GameTask.AutoSkip;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.Avalonia;

internal sealed partial class UiTests
{
    /// <summary>
    /// 画中画（--ui-test picture-in-picture，X11 与 Wayland 同一组；xdotool 真实鼠标）：实时触发页开关与画面来源写入配置；
    /// 游戏在前台时不显示；游戏失焦（焦点给主界面）后由截图调度驱动显示 420×240 置顶、不在任务栏、不抢焦点的小窗，画面为游戏截图；
    /// X11 检查位置在游戏画面右上角（边距 16）；两种画面来源都在出帧；滚轮放大 ×1.08；右键关闭后失焦期间不再显示，游戏回前台后重置；
    /// 左键单击切回游戏、小窗随即隐藏。配置事后还原（外部脚本另有备份恢复）
    /// </summary>
    private async Task PictureInPictureAsync()
    {
        var host = vm.Host;
        if (host is null) { Check("宿主", false); return; }
        await Until(() => host.RuntimeService.Current is not null, 30000);
        var runtime = host.RuntimeService.Current;
        Check("截图器已绑定游戏", runtime is not null);
        if (runtime is null) return;
        var game = runtime.Window;
        var pip = PictureInPictureHost.Current as AvaloniaPictureInPictureHost;
        Check("画中画宿主为 Avalonia 实现", pip is not null, PictureInPictureHost.Current.GetType().Name);
        if (pip is null) return;
        var cfg = host.Config.AutoSkipConfig;
        var old = (cfg.Enabled, cfg.PictureInPictureEnabled, cfg.PictureInPictureSourceType);
        var niri = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NIRI_SOCKET"));
        var pointerDisplay = Environment.GetEnvironmentVariable("BGI_TEST_POINTER_DISPLAY") ?? Environment.GetEnvironmentVariable("DISPLAY") ?? ":0";

        string Run(string file, string args, string? display = null)
        {
            var psi = new System.Diagnostics.ProcessStartInfo(file, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            if (display is not null) psi.Environment["DISPLAY"] = display;
            using var proc = System.Diagnostics.Process.Start(psi);
            var output = proc?.StandardOutput.ReadToEnd() ?? "";
            proc?.WaitForExit(10000);
            return output;
        }

        string Xdo(string args) => Run("xdotool", args, pointerDisplay);

        // 把焦点给主界面（游戏失焦）：niri 用 IPC 聚焦，X11 没有窗口管理器时 windowfocus
        async Task<bool> FocusMain()
        {
            window.Activate();
            if (await Until(() => !game.IsForeground, 1500)) return true;
            if (niri)
            {
                var json = Newtonsoft.Json.Linq.JArray.Parse(Run("niri", "msg --json windows"));
                if (json.FirstOrDefault(w => (w["title"]?.ToString() ?? "").StartsWith("BetterGI")) is { } w) Run("niri", $"msg action focus-window --id {w["id"]}");
            }
            else if (window.TryGetPlatformHandle()?.Handle is { } xid)
            {
                Xdo($"windowfocus --sync {xid}");
            }

            return await Until(() => !game.IsForeground, 5000);
        }

        async Task<bool> FocusGame()
        {
            game.Activate();
            return await Until(() => game.IsForeground, 5000);
        }

        bool Shown() => pip.Window is { IsVisible: true } w && w.Frame is not null;

        try
        {
            vm.Navigate("实时触发");
            await Settle(600);
            foreach (var ex in All<Expander>()) ex.IsExpanded = true;
            await Settle(600);
            var toggle = await WaitFor(() => Named<ToggleSwitch>("PipSwitch"));
            var source = Named<ComboBox>("PipSourceBox");
            Check("实时触发页有画中画开关与画面来源", toggle is { IsEnabled: true } && source is { ItemCount: 2 }, source?.ItemCount);
            if (toggle is null || source is null) return;
            cfg.Enabled = true;
            toggle.IsChecked = true;
            source.SelectedValue = nameof(PictureSourceType.TriggerDispatcher);
            await Settle();
            Check("开关与画面来源写入配置", cfg.PictureInPictureEnabled && cfg.PictureInPictureSourceType == nameof(PictureSourceType.TriggerDispatcher),
                cfg.PictureInPictureSourceType);
            toggle.BringIntoView();
            await Settle(300);
            Shot("pip-settings.png");

            Check("游戏在前台", await FocusGame());
            await Task.Delay(800);
            Check("游戏在前台时不显示画中画", !Shown());

            Check("游戏失焦（焦点给主界面）", await FocusMain());
            Check("失焦后显示画中画", await Until(Shown, 8000), pip.Window?.IsVisible);
            if (pip.Window is not { } w) return;
            var capture = runtime.SystemInfo.CaptureAreaRect;
            Check("置顶、不在任务栏、无边框", w is { Topmost: true, ShowInTaskbar: false, ShowActivated: false, WindowDecorations: global::Avalonia.Controls.WindowDecorations.None });
            Check("显示时不抢焦点（主界面仍为焦点）", await Until(() => !w.IsActive && window.IsActive && !game.IsForeground, 3000), $"pip {w.IsActive} main {window.IsActive}");
            Check("初始大小 420×240", Math.Abs(w.Width - 420) < 0.5 && Math.Abs(w.Height - 240) < 0.5, $"{w.Width}x{w.Height}");
            Check("画面为游戏截图尺寸", w.Frame!.PixelSize.Width == capture.Width && w.Frame.PixelSize.Height == capture.Height,
                $"{w.Frame.PixelSize} / {capture.Width}x{capture.Height}");
            using var niriIpc = niri ? BetterGenshinImpact.Platform.Linux.Wayland.NiriIpc.FromEnvironment() : null;
            if (niriIpc is not null) niriIpc.CacheDisabled = true;
            BetterGenshinImpact.Platform.PixelRect? NiriRect() => niriIpc?.Windows().FirstOrDefault(x => x.Title == w.Title)?.ScreenRect;
            if (niriIpc is not null)
            {
                // niri 不采用 X 客户端的位置，窗口经 IPC 移到游戏画面右上角；游戏画面区域与 niri 报告的位置为同一坐标系（物理像素）
                var ex = (X: capture.Left + capture.Width - 420 - 16, Y: capture.Top + 16);
                Check("位于游戏画面右上角（边距 16，niri IPC 移动）", await Until(() => NiriRect() is { } r && Math.Abs(r.X - ex.X) <= 1 && Math.Abs(r.Y - ex.Y) <= 1, 3000),
                    $"{NiriRect()} 期望 {ex}");
            }
            else
            {
                var s = w.RenderScaling;
                var ex = new PixelPoint((int)Math.Max(0, Math.Round(capture.Left + capture.Width - (w.Width + 16) * s)), (int)Math.Max(0, Math.Round(capture.Top + 16 * s)));
                // 窗口位置在 X 服务器确认（ConfigureNotify）后才更新
                Check("位于游戏画面右上角（边距 16）", await Until(() => w.Position == ex, 3000), $"{w.Position} 期望 {ex}");
            }

            var n0 = w.PresentedFrames;
            await Task.Delay(1500);
            Check("截图器供图：持续出帧", w.PresentedFrames - n0 >= 5, w.PresentedFrames - n0);
            await Task.Delay(300);
            Shot(w, "pip-window.png");

            source.SelectedValue = nameof(PictureSourceType.CaptureLoop);
            await Settle();
            n0 = w.PresentedFrames;
            await Task.Delay(1500);
            Check("60帧模式：窗口自己截图出帧", cfg.PictureInPictureSourceType == nameof(PictureSourceType.CaptureLoop) && w.PresentedFrames - n0 >= 15, w.PresentedFrames - n0);

            // 窗口在屏幕上的中心（物理像素）：X11 用窗口位置；niri 用 IPC 报告的位置（指针经宿主显示上的 niri 窗口送入，输出在 0,0、缩放 1）
            PixelPoint Center() => NiriRect() is { } r
                ? new(r.X + r.Width / 2, r.Y + r.Height / 2)
                : new(w.Position.X + (int)(w.Width * w.RenderScaling / 2), w.Position.Y + (int)(w.Height * w.RenderScaling / 2));
            var c = Center();
            Xdo($"mousemove {c.X} {c.Y}");
            await Task.Delay(300);
            Xdo("click 4");
            // X11 窗口尺寸按整像素取整
            // niri 经 xwayland-satellite 转发时一格滚轮可能变成多次滚动事件：检查放大了整数格（每格 ×1.08）
            bool Steps() { var k = Math.Log(w.Width / 420) / Math.Log(1.08); return k > 0.5 && Math.Abs(k - Math.Round(k)) < 0.02 && (niri || Math.Round(k) == 1); }
            Check("滚轮放大（每格 ×1.08）、按画面比例定高", await Until(Steps, 3000) && Math.Abs(w.Height - w.Width / w.AspectRatio) < 1, $"{w.Width:F1}x{w.Height:F1}");

            c = Center();
            Xdo($"mousemove {c.X} {c.Y}");
            await Task.Delay(300);
            Xdo("click 3");
            Check("右键关闭", await Until(() => pip.IsManuallyClosed && pip.Window is null, 3000));
            await Task.Delay(1200);
            Check("关闭后失焦期间不再显示", !Shown() && !game.IsForeground, $"shown {Shown()} game {game.IsForeground} main {window.IsActive} manual {pip.IsManuallyClosed}");

            Check("游戏回前台", await FocusGame());
            Check("回前台后重置手动关闭", await Until(() => !pip.IsManuallyClosed, 3000));
            Check("再次失焦", await FocusMain());
            Check("再次显示画中画（新窗口）", await Until(Shown, 8000));
            if (pip.Window is not { } w2) return;
            w = w2;
            await Task.Delay(500);
            c = Center();
            Xdo($"mousemove {c.X} {c.Y}");
            await Task.Delay(300);
            Xdo("click 1");
            Check("左键单击切回游戏", await Until(() => game.IsForeground, 5000));
            Check("游戏回前台后画中画隐藏", await Until(() => pip.Window is not { IsVisible: true }, 3000));
        }
        finally
        {
            (cfg.Enabled, cfg.PictureInPictureEnabled, cfg.PictureInPictureSourceType) = old;
            await Task.Delay(300);
            Dispatcher.UIThread.Post(() => pip.Window?.Close());
        }
    }
}
