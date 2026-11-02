using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Core.Config;
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
    /// 大地图点位选择（--ui-test map-points-picker，X11 与 Wayland 同一组，真实鼠标用 xdotool；Wayland 设 BGI_TEST_POINTER_DISPLAY）：
    /// 地球按钮的位置（同 WPF 网格）与输入区域；点按钮打开面板（位置、大小、画出）；分类与标签（图标）、选中标签出点位与已选列、
    /// 已选列悬停出减号并点击取消、全部隐藏 / 全部显示与计数、数据源与 HoYoLab 语言下拉框（写入配置、外部按下收起）、滚轮滚动标签列表、
    /// 重置选择、关闭；离开大地图后地球按钮消失、完全点击穿透。假点位服务 + 本地图标，不联网；配置事后还原
    /// </summary>
    private async Task MapPointsPickerAsync()
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
        var old = (host.LinuxConfig.MaskWindowEnabled, c.ShowLogBox, c.ShowStatus, cfg.MapMaskConfig.MiniMapMaskEnabled, cfg.MapMaskConfig.MapPointApiProvider, cfg.MapMaskConfig.HoYoLabLanguage);
        var hostMap = mask.MapViewModel;
        var iconDir = "/tmp/bgi-m64-mapicons";
        foreach (var key in new[] { "MihoyoMap", "KongyingTavern", "HoYoLab" })
        {
            MapMaskStateStorage.Update(key, s => { s.SelectedLabelItems = []; s.HiddenMapPointKeys = []; });
        }

        host.LinuxConfig.MaskWindowEnabled = true;
        c.ShowLogBox = false;
        c.ShowStatus = false;
        cfg.MapMaskConfig.MiniMapMaskEnabled = false;
        cfg.MapMaskConfig.MapPointApiProvider = MapPointApiProvider.MihoyoMap;
        cfg.MapMaskConfig.HoYoLabLanguage = MapMaskConfig.HoYoLabLanguageEnUs;
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
            var iconB = Path.Combine(iconDir, "b.png");
            SaveSolidPng(iconA, 0xFF00C000);
            SaveSolidPng(iconB, 0xFFE02020);
            var fake = new FakeMapPointService { ExtraItems = 20 };
            fake.Icons["a"] = iconA;
            fake.Icons["b"] = iconB;
            var map = new MaskWindowViewModel(cfg, fake, NullLogger<MaskWindowViewModel>.Instance);
            mask.UseMapViewModel(map);
            game?.Activate();
            Check("遮罩显示", await Until(() => mask.IsShown && mask.Window is not null, 20000), mask.IsShown);
            if (mask.Window is not { } mw) return;
            await Until(() => mask.OverlayLayout.Width > 0, 5000);
            var (w, h, _, _) = mask.OverlayLayout;
            var wayland = mask.OverlayPlacement is not null;
            var scale = wayland ? 1 : mw.RenderScaling;
            var picker = mw.PickerPanel;

            PixelPoint Screen(Point p)
            {
                if (mask.OverlayPlacement is { } o) return new PixelPoint(o.X + (int)Math.Round(p.X), o.Y + (int)Math.Round(p.Y));
                var s = mw.RenderScaling;
                return new PixelPoint(mw.Position.X + (int)Math.Round(p.X * s), mw.Position.Y + (int)Math.Round(p.Y * s));
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
            Rect? BoundsOf(Control? b) => b?.TransformToVisual(MaskRoot(mw)) is { } m ? new Rect(b.Bounds.Size).TransformToAABB(m) : null;
            async Task ClickButton(Views.MaskButton? b, int button = 1)
            {
                if (BoundsOf(b) is { } r) await Click(Center(r), button);
            }

            bool RegionCovers(Rect r) => mask.InputRegion.Any(x => Math.Abs(x.X / scale - r.X) < 2 && Math.Abs(x.Y / scale - r.Y) < 2
                                                                  && Math.Abs(x.Width / scale - r.Width) < 3 && Math.Abs(x.Height / scale - r.Height) < 3);

            Check("进入大地图前没有地球按钮、完全点击穿透", mw.PickerToggleButton is null && mask.InputRegion.Count == 0, mask.InputRegion.Count);
            state.Update(isInBigMap: true, bigMapViewport: new MaskRect(0, 0, w, h), miniMapViewport: default);
            Check("进入大地图", await Until(() => map.IsInBigMapUi && mw.PickerToggleButton is not null, 5000));

            // 地球按钮：WPF 网格 x = 108/1920、y = 990/1080，格子 58/1920 × 58/1080，等比缩放后居中
            var cellX = 108 / 1920d * w;
            var cellY = 990 / 1080d * h;
            var side = Math.Min(58 / 1920d * w, 58 / 1080d * h);
            var ring = BoundsOf(mw.PickerToggleButton?.Parent as Control);
            Check("地球按钮位置与大小（同 WPF 网格）", ring is { } rr && Math.Abs(rr.Width - side) < 1 && Math.Abs(rr.X - (cellX + (58 / 1920d * w - side) / 2)) < 1
                                                    && Math.Abs(rr.Y - (cellY + (58 / 1080d * h - side) / 2)) < 1, (ring, side));
            Check("输入区域 = 地球按钮", await Until(() => mask.InputRegion.Count == 1 && ring is { } r1 && RegionCovers(r1), 3000), string.Join(";", mask.InputRegion));
            var tb = BoundsOf(mw.PickerToggleButton);
            Check("地球按钮画出（#ECE5D8）", tb is { } t && Pixel(mask, mw, t.X + 3, t.Center.Y) is { } tp && ((tp >> 16) & 0xFF) > 0xD0 && (tp & 0xFF) > 0xC0,
                tb is { } t2 ? $"{Pixel(mask, mw, t2.X + 3, t2.Center.Y):X8}" : "");

            await ClickButton(mw.PickerToggleButton);
            Check("点地球按钮打开选择面板", await Until(() => map.IsMapPointPickerOpen && picker.IsVisible && picker.CategoryButtons.Count == 2 && picker.ItemButtons.Count == 22, 5000),
                (map.IsMapPointPickerOpen, picker.CategoryButtons.Count, picker.ItemButtons.Count));
            await Settle(300);
            var panel = BoundsOf(picker.PanelBorderControl);
            Check("面板位置与大小（底边在按钮格子上方 12，高为遮罩高的 0.62，宽 540）",
                panel is { } pr && Math.Abs(pr.X - cellX) < 1 && Math.Abs(pr.Bottom - (cellY - 12)) < 1 && Math.Abs(pr.Height - 0.62 * h) < 1 && Math.Abs(pr.Width - 540) < 1, panel);
            Check("输入区域加上面板", await Until(() => panel is { } p1 && RegionCovers(p1) && mask.InputRegion.Count == 2, 3000), string.Join(";", mask.InputRegion));
            Check("面板画出（不透明深色）", panel is { } p2 && Pixel(mask, mw, p2.X + 6, p2.Bottom - 4) is { } pp && pp >> 24 > 0x80 && (pp & 0xFF) < 0x60,
                panel is { } p3 ? $"{Pixel(mask, mw, p3.X + 6, p3.Bottom - 4):X8}" : "");
            Check("默认选中第一个分类、标签标题", picker.ItemsHeader == "宝箱" && map.SelectedMapLabelCategory?.Name == "宝箱", picker.ItemsHeader);
            Check("计数 0/0、全部隐藏 / 全部显示不可用", picker.SummaryText == "0/0" && !picker.HideAllButton!.Enabled && !picker.ShowAllButton!.Enabled, picker.SummaryText);
            var itemA = picker.ItemButtons.First(b => (string?)b.Tag == "a");
            Check("标签图标（本地 PNG，绿）", await Until(() =>
            {
                var ia = picker.ItemButtons.First(b => (string?)b.Tag == "a");
                return BoundsOf(ia) is { } ir && Pixel(mask, mw, ir.X + 8 + 18, ir.Center.Y) is { } ip && ((ip >> 8) & 0xFF) > 0x90 && ((ip >> 16) & 0xFF) < 0x60;
            }, 5000), BoundsOf(itemA) is { } ia2 ? $"{Pixel(mask, mw, ia2.X + 26, ia2.Center.Y):X8}" : "");
            if (!wayland) Shot(mw, "map-points-picker.png");

            // 选中标签 a、b → 点位与已选列
            await ClickButton(picker.ItemButtons.First(b => (string?)b.Tag == "a"));
            Check("点标签：选中、出点位", await Until(() => map.SelectedMapLabelItems.Count == 1 && map.MapPoints.Count == 3, 5000),
                (map.SelectedMapLabelItems.Count, map.MapPoints.Count));
            await ClickButton(picker.ItemButtons.First(b => (string?)b.Tag == "b"));
            Check("再选一个标签", await Until(() => map.SelectedMapLabelItems.Count == 2 && map.MapPoints.Count == 5 && picker.SelectedButtons.Count == 2, 5000),
                (map.SelectedMapLabelItems.Count, map.MapPoints.Count, picker.SelectedButtons.Count));
            Check("计数 5/5", await Until(() => picker.SummaryText == "5/5", 3000), picker.SummaryText);
            await Settle(200);
            var column = BoundsOf(picker.SelectedColumn);
            Check("已选列在面板右侧 12、宽 64、与面板同高", column is { } cr && panel is { } pr2 && Math.Abs(cr.X - (pr2.Right + 12)) < 1 && Math.Abs(cr.Width - 64) < 1
                                                      && Math.Abs(cr.Height - pr2.Height) < 1, column);
            Check("输入区域加上已选列", await Until(() => column is { } c1 && RegionCovers(c1), 3000), string.Join(";", mask.InputRegion));
            var sel0 = picker.SelectedButtons[0];
            Check("已选图标（绿）", await Until(() => BoundsOf(picker.SelectedButtons[0]) is { } sr && Pixel(mask, mw, sr.Center.X, sr.Y + 24) is { } sp && ((sp >> 8) & 0xFF) > 0x90, 5000),
                BoundsOf(picker.SelectedButtons[0]) is { } sr2 ? $"{Pixel(mask, mw, sr2.Center.X, sr2.Y + 24):X8}" : "");

            // 全部隐藏 / 全部显示
            await ClickButton(picker.HideAllButton);
            Check("全部隐藏：计数 0/5、全部显示可用", await Until(() => map.HiddenMapPointCount == 5 && picker.SummaryText == "0/5" && picker.ShowAllButton!.Enabled, 3000), picker.SummaryText);
            await ClickButton(picker.ShowAllButton);
            Check("全部显示", await Until(() => map.HiddenMapPointCount == 0 && picker.SummaryText == "5/5" && !picker.ShowAllButton!.Enabled, 3000), picker.SummaryText);

            // 已选列：悬停出减号，点击取消选择
            var selB = picker.SelectedButtons.First(b => (string?)b.Tag == "b");
            if (BoundsOf(selB) is { } sbr)
            {
                await Hover(Center(sbr));
                var hovered = picker.SelectedButtons.First(b => (string?)b.Tag == "b");
                Check("已选标签悬停：减号出现", hovered.IsHovered && hovered.HoverOnly is { IsVisible: true });
                await Click(Center(sbr));
            }

            Check("点已选标签：取消选择", await Until(() => map.SelectedMapLabelItems.Count == 1 && map.MapPoints.Count == 3 && picker.SelectedButtons.Count == 1, 5000),
                (map.SelectedMapLabelItems.Count, map.MapPoints.Count));

            // 分类
            await ClickButton(picker.CategoryButtons[1]);
            Check("点分类：右侧换成该分类的标签", await Until(() => picker.ItemsHeader == "神瞳" && picker.ItemButtons.Count == 2 && picker.ItemButtons.All(b => (string?)b.Tag is "c" or "d"), 5000),
                (picker.ItemsHeader, picker.ItemButtons.Count));
            await ClickButton(picker.CategoryButtons[0]);
            await Until(() => picker.ItemButtons.Count == 22, 5000);

            // 滚轮滚动标签列表（22 个标签，两列，超出面板）
            var items = picker.ItemsScroller;
            Check("标签列表超出可见范围", items is { MaxOffset: > 0 }, items?.MaxOffset);
            if (BoundsOf(items) is { } ir2)
            {
                var sp = Screen(Center(ir2));
                Xdo($"mousemove {sp.X} {sp.Y}");
                await Task.Delay(200);
                Xdo("click --repeat 3 --delay 80 5");
                Check("滚轮向下：列表滚动", await Until(() => picker.ItemsScroller is { Offset: > 0 }, 3000), picker.ItemsScroller?.Offset);
                var offset = picker.ItemsScroller?.Offset ?? 0;
                // 滚动后，被滚出可见范围的第一个标签不能点
                var first = picker.ItemButtons[0];
                Check("滚动后第一个标签移出可见范围", BoundsOf(first) is { } fr && fr.Bottom < ir2.Y + 1, BoundsOf(first));
                Xdo("click --repeat 3 --delay 80 4");
                Check("滚轮向上：回到顶部", await Until(() => picker.ItemsScroller is { Offset: 0 }, 3000), (offset, picker.ItemsScroller?.Offset));
            }

            // 数据源下拉框：外部按下收起；选 HoYoLab → 语言下拉框
            await ClickButton(picker.ProviderButton);
            Check("打开数据源下拉框", await Until(() => picker.OpenDropDown == "provider" && picker.DropDownOptions.Count == 3, 3000), picker.DropDownOptions.Count);
            if (panel is { } pr3) await Click(new Point(pr3.Right - 30, pr3.Bottom - 30));
            Check("下拉框外按下：收起", await Until(() => picker.OpenDropDown is null && picker.DropDownOptions.Count == 0, 3000));
            await ClickButton(picker.ProviderButton);
            await Until(() => picker.DropDownOptions.Count == 3, 3000);
            await ClickButton(picker.DropDownOptions[2]);
            Check("选 HoYoLab：写入配置、选择清空、出现语言下拉框",
                await Until(() => cfg.MapMaskConfig.MapPointApiProvider == MapPointApiProvider.HoYoLab && map.SelectedMapLabelItems.Count == 0 && picker.LanguageButton is not null
                                  && picker.CategoryButtons.Count == 2, 5000),
                (cfg.MapMaskConfig.MapPointApiProvider, map.SelectedMapLabelItems.Count, picker.LanguageButton is not null));
            await ClickButton(picker.LanguageButton);
            await Until(() => picker.OpenDropDown == "language" && picker.DropDownOptions.Count == 3, 3000);
            await ClickButton(picker.DropDownOptions.ElementAtOrDefault(1));
            Check("选语言：写入配置", await Until(() => cfg.MapMaskConfig.HoYoLabLanguage == MapMaskConfig.HoYoLabLanguagePtPt && picker.OpenDropDown is null, 3000),
                cfg.MapMaskConfig.HoYoLabLanguage);
            await ClickButton(picker.ProviderButton);
            await Until(() => picker.DropDownOptions.Count == 3, 3000);
            await ClickButton(picker.DropDownOptions[0]);
            Check("切回米游社大地图：语言下拉框消失", await Until(() => cfg.MapMaskConfig.MapPointApiProvider == MapPointApiProvider.MihoyoMap && picker.LanguageButton is null
                                                                  && picker.ItemButtons.Count == 22, 5000));

            // 重置选择
            await ClickButton(picker.ItemButtons.First(b => (string?)b.Tag == "a"));
            await Until(() => map.SelectedMapLabelItems.Count == 1 && picker.ResetButton is not null, 5000);
            await ClickButton(picker.ResetButton);
            Check("重置选择：已选列消失、点位清空", await Until(() => map.SelectedMapLabelItems.Count == 0 && picker.SelectedColumn is null && map.MapPoints.Count == 0, 5000),
                (map.SelectedMapLabelItems.Count, map.MapPoints.Count));

            // 关闭
            await ClickButton(picker.CloseButton);
            Check("关闭按钮：面板关闭、输入区域只剩地球按钮", await Until(() => !map.IsMapPointPickerOpen && !picker.IsVisible && mask.InputRegion.Count == 1, 3000),
                mask.InputRegion.Count);
            await ClickButton(mw.PickerToggleButton);
            Check("再点地球按钮：重新打开", await Until(() => map.IsMapPointPickerOpen && picker.IsVisible, 3000));
            await ClickButton(mw.PickerToggleButton);
            Check("再点：关闭", await Until(() => !map.IsMapPointPickerOpen && !picker.IsVisible, 3000));

            state.Update(isInBigMap: false);
            Check("离开大地图：地球按钮消失、完全点击穿透", await Until(() => !map.IsInBigMapUi && mw.PickerToggleButton is null && mask.InputRegion.Count == 0, 5000), mask.InputRegion.Count);
        }
        finally
        {
            state.Reset();
            mask.UseMapViewModel(hostMap);
            (host.LinuxConfig.MaskWindowEnabled, c.ShowLogBox, c.ShowStatus, cfg.MapMaskConfig.MiniMapMaskEnabled, cfg.MapMaskConfig.MapPointApiProvider, cfg.MapMaskConfig.HoYoLabLanguage) = old;
            try { Directory.Delete(iconDir, true); } catch (IOException) { }
        }
    }
}
