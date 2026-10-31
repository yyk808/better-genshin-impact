using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.LogParse;

namespace BetterGenshinImpact.ViewModel.Pages.CommonSettings;

/// <summary>
/// 通用设置页的选项与非 UI 逻辑（WPF CommonSettingsPage 与 Avalonia 设置-通用共用）
/// </summary>
public static class CommonSettingsChoices
{
    /// <summary>图标识别模型选项（值 → 显示名）</summary>
    public static IReadOnlyDictionary<ItemIconRecognitionMode, string> ItemIconRecognitionModes { get; } =
        new Dictionary<ItemIconRecognitionMode, string>
        {
            [ItemIconRecognitionMode.Item] = "ItemV2",
            [ItemIconRecognitionMode.GridIcon] = "GridIcon"
        };

    /// <summary>界面语言（与 WPF 相同的固定顺序）</summary>
    public static IReadOnlyList<string> UiCultureNames { get; } = ["zh-Hans", "zh-Hant", "en", "ja", "ru", "it"];

    /// <summary>遮罩准星类型</summary>
    public static IReadOnlyList<CrosshairType> CrosshairTypes { get; } = System.Enum.GetValues<CrosshairType>();

    /// <summary>自定义准星缩放方式</summary>
    public static IReadOnlyList<CrosshairScaleMode> CrosshairScaleModes { get; } = System.Enum.GetValues<CrosshairScaleMode>();

    /// <summary>七天神像所在国家（按点位 Id 排序去重）</summary>
    public static List<string> StatueCountries() =>
        MapLazyAssets.Get().GoddessPositions.Values
            .OrderBy(g => int.TryParse(g.Id, out var id) ? id : int.MaxValue)
            .GroupBy(g => g.Country)
            .Select(grp => grp.Key)
            .Where(c => !string.IsNullOrEmpty(c))
            .ToList();

    /// <summary>国家下的七天神像区域</summary>
    public static List<string> StatueAreas(string country)
    {
        if (string.IsNullOrEmpty(country)) return [];
        return MapLazyAssets.Get().GoddessPositions.Values
            .Where(g => g.Country == country)
            .OrderBy(g => int.TryParse(g.Id, out var id) ? id : int.MaxValue)
            .GroupBy(g => g.Level1Area)
            .Select(grp => grp.Key)
            .Where(a => !string.IsNullOrEmpty(a))
            .ToList();
    }

    /// <summary>国家与区域都有效时写入七天神像国家、区域与点位；返回是否写入</summary>
    public static bool ApplyRevivePoint(TpConfig tpConfig, string country, string area)
    {
        if (string.IsNullOrEmpty(country) || string.IsNullOrEmpty(area)) return false;
        var goddess = MapLazyAssets.Get().GoddessPositions.Values.FirstOrDefault(g => g.Country == country && g.Level1Area == area);
        if (goddess == null) return false;
        tpConfig.ReviveStatueOfTheSevenCountry = country;
        tpConfig.ReviveStatueOfTheSevenArea = area;
        tpConfig.ReviveStatueOfTheSevenPointX = goddess.X;
        tpConfig.ReviveStatueOfTheSevenPointY = goddess.Y;
        tpConfig.ReviveStatueOfTheSeven = goddess;
        return true;
    }

    /// <summary>米游社 cookie 为空且开启同步时，从调度器日志分析配置读取</summary>
    public static void InitializeMiyousheCookie(OtherConfig.Miyoushe config)
    {
        if (config.Cookie == string.Empty && config.LogSyncCookie)
        {
            config.Cookie = LogParse.LoadConfig().Cookie;
        }
    }
}
