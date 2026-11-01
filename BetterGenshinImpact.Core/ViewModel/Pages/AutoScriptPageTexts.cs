namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>JS 脚本 / 地图追踪 / 录制回放页的页头文字与教程链接（与 WPF 页面相同，Avalonia 共用）</summary>
public static class AutoScriptPageTexts
{
    public const string JsTitle = "自定义 Javascript 脚本（实验功能）";
    public const string JsDescription = "可以通过 Javascript 调用 BetterGI 在原神中的各项能力。请在调度器中使用！";
    public const string JsTutorialText = "点击查看 Javascript 脚本使用与编写教程";
    public const string JsTutorialUrl = "https://www.bettergi.com/feats/autos/jsscript.html";

    public const string PathingTitle = "地图追踪（实验功能）";
    public const string PathingDescription = "可以实现自动采集、自动挖矿、自动锄地等功能。请在调度器中使用！";
    public const string PathingTutorialText = "点击查看地图追踪与录制使用教程";
    public const string PathingTutorialUrl = "https://www.bettergi.com/feats/autos/pathing.html";

    public const string KmTitle = "键鼠录制回放功能（实验功能）";
    public const string KmDescription = "建议在游戏内使用快捷键进行录制，录制完成后在调度器中使用。";
    public const string KmTutorialText = "点击查看键鼠录制回放功能教程";
    public const string KmTutorialUrl = "https://www.bettergi.com/feats/autos/kmscript.html";

    /// <summary>直接执行带配置的 JS 脚本时的提示（与 WPF 相同）</summary>
    public const string JsHasSettingsHint = "此脚本存在配置，不配置可能无法正常运行，建议请添加至【调度器】，并右键修改配置后使用！";

    public const string RepoUnavailable = "脚本仓库（依赖 WebView2，Linux 版不可用）";
}
