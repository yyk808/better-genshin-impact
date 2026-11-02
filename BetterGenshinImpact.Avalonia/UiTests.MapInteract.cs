using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.GameTask.MapMask;
using BetterGenshinImpact.Model.MaskMap;
using BetterGenshinImpact.ViewModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterGenshinImpact.Avalonia;

internal sealed partial class UiTests
{
    /// <summary>
    /// 大地图交互（--ui-test map-points-interact，X11 与 Wayland 同一组，真实鼠标用 xdotool；Wayland 设 BGI_TEST_POINTER_DISPLAY 为 niri 的宿主 X 显示）：
    /// 输入区域只含点位（X11 还检查指针下的窗口：点位上是遮罩、空白处是游戏）；左键点位 → 信息弹窗（标题、说明、本地图片、链接、位置在点位上方居中）；
    /// 弹窗里“隐藏”/“显示”、关闭；右键点位隐藏 / 显示；地图移动后弹窗关闭；离开大地图后完全点击穿透。假点位服务，不联网；配置事后还原
    /// </summary>
    private async Task MapPointsInteractAsync()
    {
        var host = vm.Host;
        if (host is null) { Check("宿主", false); return; }
        await Until(() => host.RuntimeService.Current is not null, 30000);
        var game = host.RuntimeService.Current?.Window;
        var mask = await WaitFor(() => MaskWindowController.Current);
        if (mask is null) { Check("遮罩控制器", false); return; }
        var state = host.Services.GetRequiredService<IMaskWindowMapState>();
        var cfg = host.Config;
        var c = cfg.MaskWindowConfig;
        var old = (host.LinuxConfig.MaskWindowEnabled, c.ShowLogBox, c.ShowStatus, cfg.MapMaskConfig.MiniMapMaskEnabled, cfg.MapMaskConfig.MapPointApiProvider);
        var hostMap = mask.MapViewModel;
        var iconDir = "/tmp/bgi-m64-mapicons";
        foreach (var key in new[] { "MihoyoMap", "KongyingTavern" })
        {
            MapMaskStateStorage.Update(key, s => { s.SelectedLabelItems = []; s.HiddenMapPointKeys = []; });
        }

        host.LinuxConfig.MaskWindowEnabled = true;
        c.ShowLogBox = false;
        c.ShowStatus = false;
        cfg.MapMaskConfig.MiniMapMaskEnabled = false;
        cfg.MapMaskConfig.MapPointApiProvider = MapPointApiProvider.MihoyoMap;
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

        try
        {
            Directory.CreateDirectory(iconDir);
            var iconA = Path.Combine(iconDir, "a.png");
            var photo = Path.Combine(iconDir, "photo.png");
            SaveSolidPng(iconA, 0xFF00C000);
            SaveSolidPng(photo, 0xFF2040E0);
            var fake = new FakeMapPointService { InfoImage = photo, InfoLinks = [new MaskMapLink { Text = "视频攻略", Url = "https://example.invalid/video" }] };
            fake.Icons["a"] = iconA;
            var map = new MaskWindowViewModel(cfg, fake, NullLogger<MaskWindowViewModel>.Instance);
            mask.UseMapViewModel(map);
            game?.Activate();
            Check("遮罩显示", await Until(() => mask.IsShown && mask.Window is not null, 20000), mask.IsShown);
            if (mask.Window is not { } mw) return;
            await Until(() => mask.OverlayLayout.Width > 0, 5000);
            var (w, h, _, _) = mask.OverlayLayout;
            var wayland = mask.OverlayPlacement is not null;

            PixelPoint Screen(Point p)
            {
                if (mask.OverlayPlacement is { } o) return new PixelPoint(o.X + (int)Math.Round(p.X), o.Y + (int)Math.Round(p.Y));
                var s = mw.RenderScaling;
                return new PixelPoint(mw.Position.X + (int)Math.Round(p.X * s), mw.Position.Y + (int)Math.Round(p.Y * s));
            }

            bool MaskUnder(Point p)
            {
                var sp = Screen(p);
                Xdo($"mousemove {sp.X} {sp.Y}");
                var line = Xdo("getmouselocation --shell").Split('\n').FirstOrDefault(l => l.StartsWith("WINDOW="));
                return line is not null && long.TryParse(line[7..], out var id) && id == mask.Handle;
            }

            async Task Click(Point p, int button = 1)
            {
                var sp = Screen(p);
                Xdo($"mousemove {sp.X} {sp.Y}");
                await Task.Delay(150);
                Xdo($"click {button}");
                await Task.Delay(400);
                await Dispatcher.UIThread.InvokeAsync(() => { });
            }

            async Task Hover(Point p)
            {
                var sp = Screen(p);
                Xdo($"mousemove {sp.X} {sp.Y}");
                await Task.Delay(300);
                await Dispatcher.UIThread.InvokeAsync(() => { });
            }

            static Point Center(Rect r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);
            Point? ButtonCenter(Views.MaskButton? b) =>
                b?.TransformToVisual(MaskRoot(mw)) is { } m ? Center(new Rect(b.Bounds.Size).TransformToAABB(m)) : null;

            Check("进入大地图前完全点击穿透", mask.InputRegion.Count == 0, mask.InputRegion.Count);
            state.Update(isInBigMap: true, bigMapViewport: new MaskRect(0, 0, w, h), miniMapViewport: default);
            Check("进入大地图", await Until(() => map.IsInBigMapUi, 5000));
            await map.ToggleMapPointPickerCommand.ExecuteAsync(null);
            await Until(() => map.MapLabelItems.Count == 2, 5000);
            await map.SelectMapLabelItemCommand.ExecuteAsync(map.MapLabelItems.First(x => x.Id == "a"));
            await map.SelectMapLabelItemCommand.ExecuteAsync(map.MapLabelItems.First(x => x.Id == "b"));
            map.IsMapPointPickerOpen = false;
            Check("5 个点位", await Until(() => mw.BigMapLayer.LastDrawnCount == 5, 5000), mw.BigMapLayer.LastDrawnCount);
            var scale = wayland ? 1 : mw.RenderScaling;
            // 5 个点位 + 左下角打开点位选择的地球按钮
            Check("输入区域 = 5 个点位 + 地球按钮", await Until(() => mask.InputRegion.Count == 6, 3000), string.Join(";", mask.InputRegion));
            var r0 = mask.InputRegion.OrderBy(r => Math.Abs(r.X + r.Width / 2.0 - 101 * scale) + Math.Abs(r.Y + r.Height / 2.0 - 101 * scale)).First();
            Check("点位 0 的输入范围（32 + 阴影，居中于点位）", Math.Abs((r0.X + r0.Width / 2.0) / scale - 101) < 2 && Math.Abs((r0.Y + r0.Height / 2.0) / scale - 101) < 2 && r0.Width / scale >= 32,
                r0);
            if (!wayland)
            {
                Check("X11：点位上是遮罩、空白处是游戏", MaskUnder(new Point(500, 300)) && !MaskUnder(new Point(400, 250)));
            }

            Check("点位上为手形光标", mw.MapCursorAt(new Point(500, 300)) == StandardCursorType.Hand && mw.MapCursorAt(new Point(400, 250)) == StandardCursorType.Arrow);

            // 左键点位 2（500,300）→ 信息弹窗
            await Click(new Point(500, 300));
            var pop = map.PointInfoPopup;
            Check("左键点位打开信息弹窗", await Until(() => mw.PointInfoPanel.IsVisible && mw.PointInfoPanel.BodyText == "说明 1002" && mw.PointInfoPanel.ShowsImage, 5000),
                (pop.IsOpen, mw.PointInfoPanel.BodyText, pop.ImageError));
            Check("弹窗标题为标签名", mw.PointInfoPanel.TitleText == "普通宝箱", mw.PointInfoPanel.TitleText);
            Check("弹窗链接", mw.PointInfoPanel.LinkButtons.Count == 1);
            await Settle(300);
            var pb = mw.PointInfoBounds!.Value;
            // 同 WPF Popup Placement=Top：上方放得下时底边在点位上方 8，放不下时翻到点位下方；水平居中于点位
            var above = 300 - 16 - 8 - pb.Height >= 0;
            Check(above ? "弹窗在点位上方 8、水平居中" : "上方放不下：弹窗翻到点位下方 8、水平居中",
                (above ? Math.Abs(pb.Bottom - (300 - 16 - 8)) < 1.5 : Math.Abs(pb.Y - (300 + 16 + 8)) < 1.5) && Math.Abs(pb.X + 180 - 500) < 1.5, pb);
            Check("输入区域加上弹窗", await Until(() => mask.InputRegion.Count == 7, 3000), mask.InputRegion.Count);
            var imgPixel = Pixel(mask, mw, pb.X + 180, pb.Y + 200);
            Check("弹窗图片（本地 PNG，蓝）", imgPixel is { } ip && (ip & 0xFF) > 0x90 && ((ip >> 16) & 0xFF) < 0x60, $"{imgPixel:X8}");
            Check("弹窗画出（不透明）", Pixel(mask, mw, pb.X + 20, pb.Y + 20) is { } pp && pp >> 24 > 0x80, $"{Pixel(mask, mw, pb.X + 20, pb.Y + 20):X8}");
            if (!wayland) Shot(mw, "map-points-popup.png");

            // 弹窗里的“隐藏”
            var hide = mw.PointInfoPanel.HideButton;
            var hc = ButtonCenter(hide);
            Check("隐藏按钮位置", hc is not null, hc);
            if (hc is { } hidePos)
            {
                await Hover(hidePos);
                Check("悬停按钮：高亮、手形光标", hide!.IsHovered && mw.MapCursorAt(hidePos) == StandardCursorType.Hand);
                await Click(hidePos);
                Check("弹窗“隐藏”：点位隐藏、按钮变为“显示”", await Until(() => map.MapPoints[2].IsHidden && pop.IsCurrentPointHidden, 3000));
                await Settle(200);
                var showPos = ButtonCenter(mw.PointInfoPanel.HideButton);
                if (showPos is { } sp2) await Click(sp2);
                Check("弹窗“显示”：点位恢复", await Until(() => !map.MapPoints[2].IsHidden && !pop.IsCurrentPointHidden, 3000));
            }

            var closePos = ButtonCenter(mw.PointInfoPanel.CloseButton);
            if (closePos is { } cp) await Click(cp);
            Check("关闭弹窗", await Until(() => !pop.IsOpen && !mw.PointInfoPanel.IsVisible, 3000));
            Check("关闭后输入区域只剩点位与地球按钮", await Until(() => mask.InputRegion.Count == 6, 3000), mask.InputRegion.Count);

            // 右键点位 3（700,400）
            await Click(new Point(700, 400), 3);
            Check("右键点位：隐藏", await Until(() => map.MapPoints[3].IsHidden, 3000));
            await Click(new Point(700, 400), 3);
            Check("再右键：显示", await Until(() => !map.MapPoints[3].IsHidden, 3000));
            Check("右键不打开弹窗", !pop.IsOpen);

            // 地图移动 → 弹窗关闭
            await Click(new Point(300, 200));
            Check("打开点位 1 的弹窗", await Until(() => pop.IsOpen && mw.PointInfoPanel.IsVisible, 3000));
            state.Update(bigMapViewport: new MaskRect(10, 0, w, h));
            Check("地图移动后弹窗关闭（同 WPF ViewportChanged）", await Until(() => !pop.IsOpen && !mw.PointInfoPanel.IsVisible, 3000));

            // 空白处点击穿透：遮罩不处理
            await Click(new Point(400, 250));
            Check("空白处点击不打开弹窗", !pop.IsOpen);

            state.Update(isInBigMap: false);
            Check("离开大地图后完全点击穿透", await Until(() => !map.IsInBigMapUi && mask.InputRegion.Count == 0, 5000), mask.InputRegion.Count);
            if (!wayland) Check("X11：离开大地图后点位处是游戏", !MaskUnder(new Point(500, 300)));
        }
        finally
        {
            state.Reset();
            mask.UseMapViewModel(hostMap);
            (host.LinuxConfig.MaskWindowEnabled, c.ShowLogBox, c.ShowStatus, cfg.MapMaskConfig.MiniMapMaskEnabled, cfg.MapMaskConfig.MapPointApiProvider) = old;
            try { Directory.Delete(iconDir, true); } catch (IOException) { }
        }
    }

    /// <summary>遮罩内容的根（X11 为窗口内容，Wayland 为离屏渲染的内容）</summary>
    private static Visual MaskRoot(Views.MaskWindow mw) => mw.ContentRootVisual;
}
