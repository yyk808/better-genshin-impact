using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.GameTask.MapMask;
using BetterGenshinImpact.Model.MaskMap;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.ViewModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterGenshinImpact.Avalonia;

internal sealed partial class UiTests
{
    /// <summary>
    /// 地图点位的界面模型（与 WPF 共用 MaskWindowViewModel.MapPoints.cs）：
    /// 宿主注册了点位服务和模型；用假的点位服务走一遍 进入大地图 → 打开选择面板 → 分类/搜索 → 选中标签出点位 →
    /// 隐藏/全部隐藏/全部显示 → 点击点位出信息弹窗 → 离开大地图关闭面板和弹窗 → 新实例从存储恢复选择和隐藏 → 切换数据源。
    /// 不联网。配置事后还原，User/MapMask 由 restore.py 清理
    /// </summary>
    private async Task MapPointsModelAsync()
    {
        var host = vm.Host;
        if (host is null) { Check("宿主", false); return; }
        Check("宿主注册点位服务", host.Services.GetService<IMaskMapPointService>() is not null);
        Check("宿主注册点位界面模型", host.Services.GetService<MaskWindowViewModel>() is not null);

        // 上一次自测留下的选择/隐藏状态先清掉（同一份 User/MapMask，restore.py 事后清理）
        foreach (var key in new[] { "MihoyoMap", "KongyingTavern" })
        {
            MapMaskStateStorage.Update(key, s => { s.SelectedLabelItems = []; s.HiddenMapPointKeys = []; });
        }

        var cfg = host.Config;
        var old = (cfg.MapMaskConfig.MapPointApiProvider, cfg.MapMaskConfig.HoYoLabLanguage);
        cfg.MapMaskConfig.MapPointApiProvider = MapPointApiProvider.MihoyoMap;
        try
        {
            var fake = new FakeMapPointService();
            var m = new MaskWindowViewModel(cfg, fake, NullLogger<MaskWindowViewModel>.Instance);
            var big = new MaskRect(100, 100, 1000, 600);

            Check("初始不在大地图", !m.IsInBigMapUi && m.IsClickThrough);
            await m.ToggleMapPointPickerCommand.ExecuteAsync(null);
            Check("不在大地图时不能打开选择面板", !m.IsMapPointPickerOpen);

            m.ApplyMapSnapshot(new MaskWindowMapSnapshot(true, big, default));
            Check("进入大地图", m.IsInBigMapUi && !m.IsClickThrough && m.BigMapViewport == big, (m.IsInBigMapUi, m.IsClickThrough));
            await m.ToggleMapPointPickerCommand.ExecuteAsync(null);
            Check("打开选择面板", m.IsMapPointPickerOpen);
            Check("加载分类", await Until(() => m.MapLabelCategories.Count == 2 && m.MapLabelItems.Count == 2, 5000),
                (m.MapLabelCategories.Count, m.MapLabelItems.Count));
            Check("默认选第一个分类", m.SelectedMapLabelCategory?.Name == "宝箱");

            m.MapLabelSearchText = "精致";
            Check("搜索过滤", await Until(() => m.MapLabelItems.Count == 1 && m.MapLabelItems[0].Name == "精致宝箱", 5000),
                string.Join(",", m.MapLabelItems.Select(x => x.Name)));
            m.MapLabelSearchText = "";
            await Until(() => m.MapLabelItems.Count == 2, 5000);

            var item = m.MapLabelItems.First(x => x.Name == "普通宝箱");
            await m.SelectMapLabelItemCommand.ExecuteAsync(item);
            Check("选中标签", m.SelectedMapLabelItems.Count == 1 && m.SelectedMapLabelItems[0].Id == "a");
            Check("加载点位", await Until(() => m.MapPoints.Count == 3, 5000), m.MapPoints.Count);
            Check("可见点位数", m.VisibleMapPointCount == 3, m.VisibleMapPointCount);

            var p0 = m.MapPoints[0];
            m.ToggleMapPointHiddenCommand.Execute(p0);
            Check("隐藏一个点位", m.HiddenMapPointCount == 1 && m.VisibleMapPointCount == 2 && p0.IsHidden, m.HiddenMapPointCount);
            m.HideAllMapPointsCommand.Execute(null);
            Check("全部隐藏", m.HiddenMapPointCount == 3, m.HiddenMapPointCount);
            m.ShowAllMapPointsCommand.Execute(null);
            Check("全部显示", m.HiddenMapPointCount == 0, m.HiddenMapPointCount);
            m.ToggleMapPointHiddenCommand.Execute(m.MapPoints[1]);

            await m.PointClickCommand.ExecuteAsync(new MaskMapPointClickArgs(m.MapPoints[2], new System.Drawing.PointF(300, 200)));
            var pop = m.PointInfoPopup;
            Check("点击点位打开信息弹窗", await Until(() => pop.IsOpen && pop.Text == "说明 1002", 5000), (pop.IsOpen, pop.Text));
            Check("弹窗位置在点位处", Math.Abs(pop.PlacementRect.X + MaskMapPointStatic.Width / 2.0 - 300) < 0.01
                                      && Math.Abs(pop.PlacementRect.Y + MaskMapPointStatic.Height / 2.0 - 200) < 0.01, pop.PlacementRect);
            Check("弹窗标题", pop.Title.Length > 0, pop.Title);

            m.ApplyMapSnapshot(new MaskWindowMapSnapshot(false, default, default));
            Check("离开大地图关闭面板和弹窗", !m.IsInBigMapUi && !m.IsMapPointPickerOpen && !pop.IsOpen && m.IsClickThrough,
                (m.IsMapPointPickerOpen, pop.IsOpen));

            var fake2 = new FakeMapPointService();
            var m2 = new MaskWindowViewModel(cfg, fake2, NullLogger<MaskWindowViewModel>.Instance);
            m2.ApplyMapSnapshot(new MaskWindowMapSnapshot(true, big, default));
            Check("新实例进入大地图后恢复选择和点位",
                await Until(() => m2.SelectedMapLabelItems.Count == 1 && m2.MapPoints.Count == 3, 8000),
                (m2.SelectedMapLabelItems.Count, m2.MapPoints.Count));
            Check("新实例恢复隐藏点位", m2.HiddenMapPointCount == 1 && m2.MapPoints[1].IsHidden, m2.HiddenMapPointCount);

            var kong = m2.MapPointApiProviderOptions.First(x => x.Provider == MapPointApiProvider.KongyingTavern);
            m2.SelectedMapPointApiProviderOption = kong;
            Check("切换数据源写入配置", cfg.MapMaskConfig.MapPointApiProvider == MapPointApiProvider.KongyingTavern);
            Check("切换数据源后选择清空", await Until(() => m2.SelectedMapLabelItems.Count == 0 && m2.MapPoints.Count == 0, 5000),
                (m2.SelectedMapLabelItems.Count, m2.MapPoints.Count));
        }
        finally
        {
            cfg.MapMaskConfig.MapPointApiProvider = old.MapPointApiProvider;
            cfg.MapMaskConfig.HoYoLabLanguage = old.HoYoLabLanguage;
        }
    }

    private sealed class FakeMapPointService : IMaskMapPointService
    {
        private static MaskMapPointLabel L(string id, string name, int count) =>
            new() { LabelId = id, LabelIds = [id], Name = name, PointCount = count };

        public Task<IReadOnlyList<MaskMapPointLabel>> GetLabelCategoriesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MaskMapPointLabel>>(
            [
                new MaskMapPointLabel { LabelId = "c1", Name = "宝箱", Children = [L("a", "普通宝箱", 3), L("b", "精致宝箱", 2)] },
                new MaskMapPointLabel { LabelId = "c2", Name = "神瞳", Children = [L("c", "风神瞳", 1), L("d", "岩神瞳", 1)] },
            ]);

        public Task<MaskMapPointsResult> GetPointsAsync(IReadOnlyList<MaskMapPointLabel> selectedItems, CancellationToken ct = default)
        {
            var pts = selectedItems.SelectMany(l => Enumerable.Range(0, l.PointCount).Select(i => new MaskMapPoint
            {
                Id = $"{l.LabelId}{i}", LabelId = l.LabelId, X = 100 * i, Y = 50 * i, GameX = 100 * i, GameY = 50 * i,
            })).ToList();
            // 米游社点位 Id 是数字：1000、1001、1002……
            for (var i = 0; i < pts.Count; i++) pts[i].Id = $"{1000 + i}";
            return Task.FromResult(new MaskMapPointsResult { Labels = selectedItems, Points = pts });
        }

        public Task<MaskMapPointInfo> GetPointInfoAsync(MaskMapPoint point, CancellationToken ct = default) =>
            Task.FromResult(new MaskMapPointInfo { Text = "说明 " + point.Id });
    }
}
