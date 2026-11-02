using Avalonia;
using Avalonia.Controls;
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
    /// 点位标签搜索窗口（--ui-test map-points-search，X11 与 Wayland 同一组；xdotool 真实鼠标与键盘）：
    /// 点选择面板的搜索框弹出可获得焦点的搜索窗口（X11 检查位置：左边与搜索框对齐、底边在搜索框上方 4）；键入即过滤标签、面板搜索框显示文字；
    /// Esc 隐藏且文字保留；“X”隐藏；清空后恢复全部标签；关闭选择面板时搜索窗口一起隐藏。假点位服务，不联网；配置事后还原
    /// </summary>
    private async Task MapPointsSearchAsync()
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
        foreach (var key in new[] { "MihoyoMap", "KongyingTavern", "HoYoLab" })
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
            var fake = new FakeMapPointService { ExtraItems = 20 };
            var map = new MaskWindowViewModel(cfg, fake, NullLogger<MaskWindowViewModel>.Instance);
            mask.UseMapViewModel(map);
            game?.Activate();
            Check("遮罩显示", await Until(() => mask.IsShown && mask.Window is not null, 20000), mask.IsShown);
            if (mask.Window is not { } mw) return;
            await Until(() => mask.OverlayLayout.Width > 0, 5000);
            var (w, h, _, _) = mask.OverlayLayout;
            var wayland = mask.OverlayPlacement is not null;
            var picker = mw.PickerPanel;

            PixelPoint Screen(Point p)
            {
                if (mask.OverlayPlacement is { } o) return new PixelPoint(o.X + (int)Math.Round(p.X), o.Y + (int)Math.Round(p.Y));
                var s = mw.RenderScaling;
                return new PixelPoint(mw.Position.X + (int)Math.Round(p.X * s), mw.Position.Y + (int)Math.Round(p.Y * s));
            }

            Rect? BoundsOf(Control? b) => b?.TransformToVisual(MaskRoot(mw)) is { } m ? new Rect(b.Bounds.Size).TransformToAABB(m) : null;
            async Task ClickButton(Control? b)
            {
                if (BoundsOf(b) is not { } r) return;
                var sp = Screen(new Point(r.X + r.Width / 2, r.Y + r.Height / 2));
                Xdo($"mousemove {sp.X} {sp.Y}");
                await Task.Delay(150);
                Xdo("click 1");
                await Task.Delay(400);
                await Dispatcher.UIThread.InvokeAsync(() => { });
            }

            state.Update(isInBigMap: true, bigMapViewport: new MaskRect(0, 0, w, h), miniMapViewport: default);
            Check("进入大地图", await Until(() => map.IsInBigMapUi && mw.PickerToggleButton is not null, 5000));
            await ClickButton(mw.PickerToggleButton);
            Check("打开选择面板", await Until(() => picker.IsVisible && picker.ItemButtons.Count == 22, 5000), picker.ItemButtons.Count);
            await Settle(200);
            var box = BoundsOf(picker.SearchBox);
            await ClickButton(picker.SearchBox);
            var sw = mask.SearchWindow;
            // 激活窗口靠窗口管理器（_NET_ACTIVE_WINDOW）；X11 测试显示上没有窗口管理器时由测试代为聚焦（同窗口管理器的行为）
            var noWm = !wayland && string.IsNullOrWhiteSpace(Xdo("getactivewindow"));
            async Task<bool> Opened()
            {
                if (!await Until(() => mask.SearchWindow is { IsVisible: true }, 5000)) return false;
                // 嵌套 niri：键盘事件经宿主 X 显示上 niri 的窗口送进去，宿主显示没有窗口管理器，先把焦点给 niri 的窗口
                if (wayland) Xdo("search --name ^niri$ windowfocus --sync");
                if (noWm && mask.SearchWindow?.TryGetPlatformHandle()?.Handle is { } hw)
                {
                    await Task.Delay(200);
                    Xdo($"windowfocus --sync {hw}");
                }

                return await Until(() => mask.SearchWindow is { IsVisible: true, IsActive: true }, 5000);
            }

            Check(noWm ? "点搜索框弹出搜索窗口（无窗口管理器：测试代为聚焦后获得焦点）" : "点搜索框弹出搜索窗口并获得焦点", await Opened(),
                (mask.SearchWindow?.IsVisible, mask.SearchWindow?.IsActive));
            sw = mask.SearchWindow;
            if (sw is null) return;
            if (!wayland && box is { } b)
            {
                var s = mw.RenderScaling;
                var expect = new PixelPoint(mw.Position.X + (int)Math.Round(b.X * s), mw.Position.Y + (int)Math.Round((b.Y - 4 - 64) * s));
                Check("X11：搜索窗口左边与搜索框对齐、底边在搜索框上方 4", Math.Abs(sw.Position.X - expect.X) <= 1 && Math.Abs(sw.Position.Y - expect.Y) <= 1, (sw.Position, expect));
                Shot(mw, "map-points-search-mask.png");
            }

            Xdo("type --delay 80 19");
            Check("键入即过滤标签", await Until(() => map.MapLabelSearchText == "19" && picker.ItemButtons.Count == 1, 5000),
                (map.MapLabelSearchText, picker.ItemButtons.Count));
            if (!wayland) Shot(sw, "map-points-search.png");
            Check("面板搜索框显示搜索文字", picker.SearchBox?.Child is TextBlock { Text: "19" }, (picker.SearchBox?.Child as TextBlock)?.Text);
            Xdo("key Escape");
            Check("Esc 隐藏搜索窗口、文字保留", await Until(() => !sw.IsVisible, 3000) && map.MapLabelSearchText == "19", (sw.IsVisible, map.MapLabelSearchText));

            await ClickButton(picker.SearchBox);
            Check("再点搜索框：重新弹出并获得焦点", await Opened());
            Xdo("key ctrl+a BackSpace");
            Check("清空后恢复全部标签", await Until(() => map.MapLabelSearchText == "" && picker.ItemButtons.Count == 22, 5000),
                (map.MapLabelSearchText, picker.ItemButtons.Count));
            await Dispatcher.UIThread.InvokeAsync(() => sw.CloseButton.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)));
            Check("“X”隐藏搜索窗口", await Until(() => !sw.IsVisible, 3000));

            await ClickButton(picker.SearchBox);
            await Until(() => sw.IsVisible, 5000);
            await ClickButton(picker.CloseButton);
            Check("关闭选择面板时搜索窗口一起隐藏", await Until(() => !map.IsMapPointPickerOpen && !sw.IsVisible, 3000), (map.IsMapPointPickerOpen, sw.IsVisible));
            state.Update(isInBigMap: false);
            Check("离开大地图：完全点击穿透", await Until(() => mask.InputRegion.Count == 0, 5000), mask.InputRegion.Count);
        }
        finally
        {
            state.Reset();
            mask.UseMapViewModel(hostMap);
            (host.LinuxConfig.MaskWindowEnabled, c.ShowLogBox, c.ShowStatus, cfg.MapMaskConfig.MiniMapMaskEnabled, cfg.MapMaskConfig.MapPointApiProvider) = old;
        }
    }
}
