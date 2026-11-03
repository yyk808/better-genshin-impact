using System.Collections.Generic;
using System.IO;
using BetterGenshinImpact.Infrastructure.Storage;

namespace BetterGenshinImpact.Host.Common.Storage;

/// <summary>
/// Linux 版网页（WebKitGTK）的数据目录。与 Windows 版各 WebView2 配置一一对应，都放在程序数据目录 Data/WebKit 下，
/// 每个目录下有 data（网页数据、登录状态）与 cache（网页缓存）两部分
/// </summary>
public static class LinuxWebDataPaths
{
    /// <summary>Data/WebKit</summary>
    public static string Root => Path.Combine(AppPaths.Root, "WebKit");

    /// <summary>软件内网页（脚本仓库等），对应 Windows 版 App 环境的 Default 配置</summary>
    public static string App => Path.Combine(Root, "App");

    /// <summary>悬浮小窗，对应 Windows 版独立的 Guide 环境</summary>
    public static string Guide => Path.Combine(Root, "Guide");

    /// <summary>脚本 HTML 遮罩，对应 Windows 版 HtmlMask 配置</summary>
    public static string HtmlMask => Path.Combine(Root, "HtmlMask");

    /// <summary>云原神网页版</summary>
    public static string CloudGame => Path.Combine(Root, "CloudGame");

    /// <summary>网页数据目录（登录状态、本地存储）</summary>
    public static string Data(string profile) => Path.Combine(profile, "data");

    /// <summary>网页缓存目录</summary>
    public static string Cache(string profile) => Path.Combine(profile, "cache");
}

/// <summary>
/// 存储空间中“网页数据”分组的 Linux 版来源（Windows 版为 WebViewEnvironmentProvider 与 CloudGameStorageAreaProvider）。
/// 网页进程长期占用这些文件，与 Windows 版相同，统一推迟到下次启动、任何网页创建之前删除
/// </summary>
public sealed class LinuxWebDataStorageAreaProvider : IStorageAreaProvider
{
    public IEnumerable<StorageArea> GetAreas()
    {
        yield return new StorageArea("webview:app:Default", "软件内网页 · 网页缓存", StorageGroup.WebData,
            [LinuxWebDataPaths.Cache(LinuxWebDataPaths.App)])
        {
            Description = "脚本仓库等网页的缓存，保留网页登录与设置",
            CleanupEffect = "下次启动时清理",
            Timing = StorageCleanupTiming.NextStartup,
        };

        yield return new StorageArea("webview:app:HtmlMask", "软件内网页 · 脚本遮罩数据", StorageGroup.WebData,
            [LinuxWebDataPaths.HtmlMask])
        {
            Description = "脚本 HTML 遮罩网页的缓存和网页数据",
            CleanupEffect = "会清除网页中保存的设置，下次启动时清理",
            Timing = StorageCleanupTiming.NextStartup,
            RequiresConfirmation = true,
            IncludedInQuickCleanup = false,
        };

        yield return new StorageArea("webview:guide:Default", "悬浮小窗 · 网页缓存", StorageGroup.WebData,
            [LinuxWebDataPaths.Cache(LinuxWebDataPaths.Guide)])
        {
            Description = "悬浮小窗中网页的缓存，保留 B 站等网站的登录和网页设置",
            CleanupEffect = "下次启动时清理",
            Timing = StorageCleanupTiming.NextStartup,
        };

        yield return new StorageArea("webview:cloud-game", "云原神 · 网页缓存", StorageGroup.WebData,
            [LinuxWebDataPaths.Cache(LinuxWebDataPaths.CloudGame)])
        {
            Description = "云原神网页版的缓存，保留登录状态",
            CleanupEffect = "下次启动时清理，之后首次打开需要重新加载页面资源",
            Timing = StorageCleanupTiming.NextStartup,
            IncludedInQuickCleanup = false,
        };
    }
}
