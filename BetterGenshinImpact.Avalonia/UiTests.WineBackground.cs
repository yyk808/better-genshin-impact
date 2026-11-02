using Avalonia.Controls;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;

namespace BetterGenshinImpact.Avalonia;

internal sealed partial class UiTests
{
    /// <summary>
    /// Wine 后台输入的 WM_ACTIVATE 开关（--ui-test wine-background）：设置页有开关、默认关闭、说明写明会把游戏切到前台；
    /// 打开后写入 linux-host.json，平台服务（每次操作时读取）立即看到新值；关回后恢复。linux-host.json 由外部脚本备份恢复
    /// </summary>
    private async Task WineBackgroundAsync()
    {
        var host = vm.Host;
        if (host is null) { Check("宿主", false); return; }
        var x11 = BetterGenshinImpact.Platform.Linux.Wayland.LinuxSession.X11Of(host.Platform);
        Check("平台有 X11 部分", x11 is not null, host.Platform.Name);
        vm.Navigate("设置");
        await Settle(800);
        var toggle = await WaitFor(() => Named<ToggleSwitch>("WineActivateToggle"));
        Check("设置页有 WM_ACTIVATE 开关", toggle is not null);
        if (toggle is null || x11 is null) return;
        var expander = toggle.GetVisualAncestors().OfType<FASettingsExpander>().FirstOrDefault();
        var description = expander?.Description?.ToString() ?? "";
        Check("说明写明会把游戏切到前台", description.Contains("切到前台") && description.Contains("WM_ACTIVATE"), description);
        Check("默认关闭", toggle.IsChecked == false && !host.LinuxConfig.WineBackgroundActivate && !x11.WineBackgroundActivate());

        var file = host.LinuxConfigFilePath!;
        toggle.IsChecked = true;
        await Settle(1500);
        Check("打开后写入 linux-host.json", await Until(() => ReadJson(file)["wineBackgroundActivate"]?.ToObject<bool>() == true, 5000),
            ReadJson(file)["wineBackgroundActivate"]?.ToString());
        Check("打开后平台服务立即生效", x11.WineBackgroundActivate());
        toggle.BringIntoView();
        await Settle(400);
        Shot("settings-wine-activate.png");

        toggle.IsChecked = false;
        await Settle(1500);
        Check("关回后写入 linux-host.json", await Until(() => ReadJson(file)["wineBackgroundActivate"]?.ToObject<bool>() == false, 5000));
        Check("关回后平台服务立即生效", !x11.WineBackgroundActivate());
    }
}
