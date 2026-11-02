using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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
    /// 遮罩上画地图点位（--ui-test map-points-draw，X11 与 Wayland 同一组）：
    /// 遮罩换成用假点位服务的界面模型，经宿主的 MaskWindowMapState（与大地图识别相同的入口）进出大地图；
    /// 大地图：点位位置、图标（本地 PNG，不联网）、无图标时画标签颜色、隐藏点位 35% 不透明度、加载提示；
    /// 小地图：位置按 MapAssets 换算、圆形裁剪（圆外的点位不画）。像素：Wayland 读覆盖层提交的位图，X11 把遮罩窗口渲染到位图。
    /// 配置事后还原，图标放在 /tmp/bgi-m64-mapicons（事后删除），User/MapMask 收尾时删除
    /// </summary>
    private async Task MapPointsDrawAsync()
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
        Check("遮罩使用宿主的点位界面模型", hostMap is not null && ReferenceEquals(hostMap, host.Services.GetService<MaskWindowViewModel>()));
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
        try
        {
            Directory.CreateDirectory(iconDir);
            var iconA = Path.Combine(iconDir, "a.png");
            SaveSolidPng(iconA, 0xFF00C000);
            var fake = new FakeMapPointService();
            fake.Icons["a"] = iconA;
            fake.Icons["b"] = Path.Combine(iconDir, "missing.png"); // 不存在：画标签颜色
            var map = new MaskWindowViewModel(cfg, fake, NullLogger<MaskWindowViewModel>.Instance);
            mask.UseMapViewModel(map);

            game?.Activate();
            Check("遮罩显示", await Until(() => mask.IsShown && mask.Window is not null, 20000), mask.IsShown);
            if (mask.Window is not { } mw) return;
            await Until(() => mask.OverlayLayout.Width > 0, 5000);
            var (w, h, _, _) = mask.OverlayLayout;

            // —— 大地图 ——
            state.Update(isInBigMap: true, bigMapViewport: new MaskRect(0, 0, w, h), miniMapViewport: new MaskRect(0, 0, 1000, 1000));
            Check("经 MaskWindowMapState 进入大地图", await Until(() => map.IsInBigMapUi, 5000));
            Check("大地图点位层显示", await Until(() => mw.BigMapLayer.IsVisible, 3000));
            await map.ToggleMapPointPickerCommand.ExecuteAsync(null);
            await Until(() => map.MapLabelItems.Count == 2, 5000);
            fake.PointsDelayMs = 1500;
            await map.SelectMapLabelItemCommand.ExecuteAsync(map.MapLabelItems.First(x => x.Id == "a"));
            Check("加载点位时显示提示", await Until(() => mw.MapLoadingVisible, 1200), map.MapPointsLoadingText);
            Check("加载完成后提示消失、画出 3 个点位", await Until(() => !mw.MapLoadingVisible && mw.BigMapLayer.LastDrawnCount == 3, 5000),
                (mw.MapLoadingVisible, mw.BigMapLayer.LastDrawnCount));
            fake.PointsDelayMs = 0;
            await map.SelectMapLabelItemCommand.ExecuteAsync(map.MapLabelItems.First(x => x.Id == "b"));
            // 关掉选择面板（面板与已选列会盖住点位，同 WPF）
            map.IsMapPointPickerOpen = false;
            Check("两个标签共 5 个点位", await Until(() => mw.BigMapLayer.LastDrawnCount == 5, 5000), mw.BigMapLayer.LastDrawnCount);
            Check("有图标的 3 个点位用图标画", await Until(() => mw.BigMapLayer.LastIconCount == 3, 5000), mw.BigMapLayer.LastIconCount);
            await Settle(300);
            var p0 = Pixel(mask, mw, 100, 100);
            Check("点位 0：图标（绿）", IsGreen(p0), $"{p0:X8}");
            var p3 = Pixel(mask, mw, 700, 400);
            Check("点位 3：没有图标时画标签颜色（不透明、不是图标的绿）", p3 is { } v3 && v3 >> 24 == 0xFF && !IsGreen(p3), $"{p3:X8}");
            // 图标盖住描边的内半边，外半边在半径 16 处
            var ring = Pixel(mask, mw, 100 + 16, 100);
            Check("点位边框（#D3BC8E）", ring is { } vr && Near(vr, 0xD3, 0xBC, 0x8E, 40), $"{ring:X8}");
            var gap = Pixel(mask, mw, 200, 150);
            Check("点位之间透明", gap is { } vg && vg >> 24 == 0, $"{gap:X8}");
            if (mask.OverlayPlacement is null) Shot(mw, "map-points-big.png"); // Wayland 下遮罩窗口不显示，按像素检查

            map.ToggleMapPointHiddenCommand.Execute(map.MapPoints[1]);
            await Settle(300);
            var p1 = Pixel(mask, mw, 300, 200);
            Check("隐藏的点位 35% 不透明度", p1 is { } v1 && (v1 >> 24) is >= 70 and <= 110 && IsGreen(p1), $"{p1:X8}");

            // 可视区域移动：点位跟着移动
            state.Update(bigMapViewport: new MaskRect(-50, -20, w, h));
            await Settle(400);
            var moved = Pixel(mask, mw, 150, 120);
            Check("可视区域移动后点位跟着移动", IsGreen(moved) && Pixel(mask, mw, 200, 150) is { } vg2 && vg2 >> 24 == 0, $"{moved:X8}");

            // —— 小地图 ——
            cfg.MapMaskConfig.MiniMapMaskEnabled = true;
            state.Update(isInBigMap: false);
            Check("离开大地图：大地图点位层隐藏", await Until(() => !map.IsInBigMapUi && !mw.BigMapLayer.IsVisible, 5000));
            Check("小地图点位层显示", await Until(() => mw.MiniMapLayer.IsVisible, 3000));
            var left = map.MiniMapOverlayLeftRatio * w;
            var top = map.MiniMapOverlayTopRatio * h;
            var side = map.MiniMapOverlaySizeRatio * h;
            var b = mw.MiniMapLayer.Bounds;
            Check("小地图位置与大小（MapAssets 比例）", Math.Abs(b.X - left) < 1 && Math.Abs(b.Y - top) < 1 && Math.Abs(b.Width - side) < 1 && Math.Abs(b.Height - side) < 1,
                $"{b} / ({left:F1},{top:F1},{side:F1})");
            // 视口 1000×1000：点位 0 (100,100) 在圆外被裁掉，点位 2 (500,300) 在圆内
            await Until(() => mw.MiniMapLayer.LastDrawnCount > 0, 3000);
            await Settle(300);
            var m2 = Pixel(mask, mw, left + 0.5 * side, top + 0.3 * side);
            Check("小地图：圆内的点位画出（图标）", IsGreen(m2), $"{m2:X8}");
            var m0 = Pixel(mask, mw, left + 0.1 * side, top + 0.1 * side);
            Check("小地图：圆外的点位被裁掉", m0 is { } v0 && v0 >> 24 == 0, $"{m0:X8}");
            var expected = Math.Max(8, Math.Min(16, side / 12.0));
            var edge = Pixel(mask, mw, left + 0.5 * side + expected / 2 + 3, top + 0.3 * side);
            Check("小地图点位大小 = 直径 / 12（8–16）", edge is { } ve && ve >> 24 < 0x40, $"{expected:F1} {edge:X8}");
            if (mask.OverlayPlacement is null) Shot(mw, "map-points-mini.png");

            cfg.MapMaskConfig.MiniMapMaskEnabled = false;
            Check("关闭小地图遮罩后隐藏", await Until(() => !mw.MiniMapLayer.IsVisible, 3000));
        }
        finally
        {
            state.Reset();
            mask.UseMapViewModel(hostMap);
            (host.LinuxConfig.MaskWindowEnabled, c.ShowLogBox, c.ShowStatus, cfg.MapMaskConfig.MiniMapMaskEnabled, cfg.MapMaskConfig.MapPointApiProvider) = old;
            try { Directory.Delete(iconDir, true); } catch (IOException) { }
        }
    }

    private static bool IsGreen(uint? argb) => argb is { } v && ((v >> 8) & 0xFF) > 2 * ((v >> 16) & 0xFF) + 20 && ((v >> 8) & 0xFF) > 2 * (v & 0xFF) + 20;

    private static bool Near(uint argb, int r, int g, int b, int tol) =>
        Math.Abs((int)((argb >> 16) & 0xFF) - r) <= tol && Math.Abs((int)((argb >> 8) & 0xFF) - g) <= tol && Math.Abs((int)(argb & 0xFF) - b) <= tol;

    /// <summary>遮罩上 (x, y)（DIP）的 ARGB（预乘）：Wayland 读覆盖层提交的位图，X11 把遮罩窗口渲染到位图（缩放 1）</summary>
    private static uint? Pixel(MaskWindowController mask, Views.MaskWindow mw, double x, double y)
    {
        if (mask.OverlayPlacement is { } o)
        {
            return mask.OverlayPixel((int)Math.Round(x * o.BufferScale), (int)Math.Round(y * o.BufferScale));
        }

        var size = new PixelSize((int)mw.Bounds.Width, (int)mw.Bounds.Height);
        if (x < 0 || y < 0 || x >= size.Width || y >= size.Height) return null;
        using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
        // 只渲染内容（窗口本身有主题底色，屏幕上是透明的）
        bitmap.Render((Visual?)mw.Content ?? mw);
        var buffer = new byte[4];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect((int)x, (int)y, 1, 1), handle.AddrOfPinnedObject(), 4, 4);
        }
        finally
        {
            handle.Free();
        }

        // BGRA（预乘）
        return (uint)(buffer[3] << 24 | buffer[2] << 16 | buffer[1] << 8 | buffer[0]);
    }

    /// <summary>写一张 64×64 的纯色 PNG（ARGB）</summary>
    private static void SaveSolidPng(string path, uint argb)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(64, 64), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = bitmap.Lock())
        {
            var row = new int[64];
            Array.Fill(row, unchecked((int)argb));
            for (var y = 0; y < 64; y++) Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, 64);
        }

#pragma warning disable CS0618 // 同 Program.SaveScreenshot：PNG 默认编码即可
        bitmap.Save(path);
#pragma warning restore CS0618
    }
}
