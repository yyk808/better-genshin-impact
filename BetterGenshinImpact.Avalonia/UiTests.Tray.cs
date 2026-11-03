using Avalonia.Controls;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Platform.Linux.Desktop;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Avalonia;

internal sealed partial class UiTests
{
    /// <summary>
    /// 托盘图标（--ui-test tray）：外部脚本在私有会话总线上起假 StatusNotifierWatcher（BGI_TEST_SNI_OUT 为其状态 JSON，
    /// 写 OUT.click（菜单标签）/ OUT.activate 让它经 D-Bus 点菜单项或左键）。
    /// 检查托盘菜单同 WPF、经 D-Bus 登记并导出图标与菜单；开启“退出时最小化到系统托盘”后关闭窗口隐藏到托盘，
    /// D-Bus 菜单点击“显示或隐藏此实例窗口”显示、左键再隐藏；探测不存在的宿主名返回 false。不联网
    /// </summary>
    private async Task TrayAsync()
    {
        var tray = TrayIconController.Current;
        if (tray is null) { Check("托盘控制器", false); return; }
        var headers = Headers(tray.Icon.Menu);
        var guideAvailable = vm.Host?.Services.GetService(typeof(BetterGenshinImpact.Service.Guide.IGuideWindowService)) is BetterGenshinImpact.Service.Guide.IGuideWindowService { IsAvailable: true };
        string[] expected = guideAvailable
            ? [TrayIconController.ShowOrHideText, "-", "悬浮小窗", "  显示悬浮小窗", "  打开剪贴板中的链接", "  恢复小窗（关闭穿透、不透明）", "  -", "  小窗设置", "-", TrayIconController.ExitText]
            : [TrayIconController.ShowOrHideText, "-", TrayIconController.ExitText];
        Check("托盘菜单同 WPF（没有桌面分身、检查更新）", guideAvailable && headers.SequenceEqual(expected), string.Join("|", headers));
        Check("托盘提示与图标", tray.Icon.ToolTipText == "BetterGI" && tray.Icon.Icon is not null && window.Icon is not null);

        var outPath = Environment.GetEnvironmentVariable("BGI_TEST_SNI_OUT");
        if (outPath is null) { Check("BGI_TEST_SNI_OUT", false); return; }
        JObject? Read() { try { return JObject.Parse(File.ReadAllText(outPath)); } catch { return null; } }
        JToken? item = null;
        Check("经 D-Bus 登记到托盘宿主", await Until(() => (item = Read()?["items"]?.FirstOrDefault(i => i["menu"] is not null)) is not null, 15000));
        Check("StatusNotifierItem：状态、标题与图标", item?["props"]?["Status"]?.ToString() == "Active"
            && item["props"]?["IconPixmap"] is JArray { Count: > 0 } px && px.All(p => p[2]!.Value<int>() == p[0]!.Value<int>() * p[1]!.Value<int>() * 4),
            item?["props"]?.ToString(Newtonsoft.Json.Formatting.None));
        var labels = item?["menu"]?.Select(m => m["label"]!.ToString().Replace("_", "")).Where(l => l.Length > 0).ToList() ?? [];
        Check("dbusmenu 导出菜单文本", labels.Contains(TrayIconController.ShowOrHideText) &&  labels.Contains("悬浮小窗") && labels.Contains("小窗设置") && labels.Contains(TrayIconController.ExitText),
            string.Join("|", labels));
        Check("探测托盘宿主：有假宿主 true、不存在的名字 false", await StatusNotifierWatcherProbe.IsAvailableAsync()
            && !await StatusNotifierWatcherProbe.IsAvailableAsync("org.kde.StatusNotifierWatcherBgiTestMissing"));

        vm.Navigate("设置");
        await Settle(600);
        var toggle = await WaitFor(() => Named<ToggleSwitch>("ExitToTrayToggle"));
        var cfg = vm.Host!.Config.CommonConfig;
        var saved = cfg.ExitToTray;
        cfg.ExitToTray = false;
        await Settle(200);
        if (toggle is not null) toggle.IsChecked = true;
        await Settle(200);
        Check("设置页开关写入 ExitToTray", toggle is not null && cfg.ExitToTray);

        try
        {
            window.Close();
            Check("关闭窗口隐藏到托盘、程序继续运行", await Until(() => !window.IsVisible, 5000) && tray.LastCloseAction == "tray", tray.LastCloseAction);
            await Settle(400);
            File.WriteAllText(outPath + ".click", TrayIconController.ShowOrHideText);
            Check("D-Bus 菜单点击“显示或隐藏此实例窗口”显示窗口", await Until(() => window.IsVisible, 8000), Read()?["events"]?.ToString(Newtonsoft.Json.Formatting.None));
            await Settle(600);
            File.WriteAllText(outPath + ".activate", "");
            Check("左键托盘图标隐藏窗口", await Until(() => !window.IsVisible, 8000), Read()?["events"]?.ToString(Newtonsoft.Json.Formatting.None));
            await Settle(400);
            tray.ShowOrHide();
            Check("再次显示或隐藏：显示", await Until(() => window.IsVisible, 5000));
        }
        finally
        {
            cfg.ExitToTray = saved;
            if (!window.IsVisible) window.Show();
        }

        await Settle(600);
        Shot("tray-settings.png");
    }

    private static List<string> Headers(NativeMenu? menu, string indent = "")
    {
        var list = new List<string>();
        if (menu is null) return list;
        foreach (var i in menu.Items)
        {
            if (i is NativeMenuItemSeparator) list.Add(indent + "-");
            else if (i is NativeMenuItem m)
            {
                list.Add(indent + m.Header);
                list.AddRange(Headers(m.Menu, indent + "  "));
            }
        }

        return list;
    }
}
