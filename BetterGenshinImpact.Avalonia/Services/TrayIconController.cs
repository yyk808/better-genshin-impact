using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using BetterGenshinImpact.Avalonia.ViewModels;
using BetterGenshinImpact.Host.Common;
using BetterGenshinImpact.Platform.Linux.Desktop;
using BetterGenshinImpact.Service.Guide;
using Serilog;

namespace BetterGenshinImpact.Avalonia.Services;

/// <summary>
/// 托盘图标与“退出时最小化到系统托盘”（同 WPF MainWindow 的 NotifyIcon 与 NotifyIconViewModel）。
/// Linux 上 Avalonia 的托盘走 StatusNotifierItem（D-Bus），需要桌面提供托盘宿主（org.kde.StatusNotifierWatcher）；
/// 没有宿主时托盘图标不显示，开启“关闭到托盘”时关闭窗口也正常退出并记日志，避免窗口无处找回。
/// 菜单只放 Linux 版有对应功能的项：没有“桌面分身”“检查更新”
/// </summary>
public sealed class TrayIconController : IDisposable
{
    /// <summary>托盘菜单“显示或隐藏此实例窗口”</summary>
    public const string ShowOrHideText = "显示或隐藏此实例窗口";

    /// <summary>托盘菜单退出项（Linux 版只有主实例）</summary>
    public const string ExitText = "退出 BetterGI";

    private readonly Window _window;
    private readonly GameHost? _host;
    private readonly MainWindowViewModel _vm;
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly ILogger _log;
    private readonly IGuideWindowService? _guide;
    private readonly NativeMenuItem? _guideToggle;
    private bool _disposed;

    /// <summary>托盘图标（自测读取菜单与提示）</summary>
    public TrayIcon Icon { get; }

    /// <summary>最近一次关闭窗口时的处理：隐藏到托盘 / 没有托盘宿主而退出（自测读取）</summary>
    public string? LastCloseAction { get; private set; }

    /// <summary>当前实例（自测用）</summary>
    public static TrayIconController? Current { get; private set; }

    public TrayIconController(Application app, Window window, GameHost? host, MainWindowViewModel vm,
        IClassicDesktopStyleApplicationLifetime desktop, ILogger log)
    {
        _window = window;
        _host = host;
        _vm = vm;
        _desktop = desktop;
        _log = log;
        _guide = host?.Services.GetService(typeof(IGuideWindowService)) as IGuideWindowService;

        var menu = new NativeMenu();
        menu.Items.Add(Item(ShowOrHideText, ShowOrHide));
        menu.Items.Add(new NativeMenuItemSeparator());
        if (_guide is { IsAvailable: true } guide)
        {
            // 悬浮小窗：只在主实例提供（同 WPF）
            var sub = new NativeMenu();
            _guideToggle = Item(GuideToggleText, () => guide.ToggleVisible());
            sub.Items.Add(_guideToggle);
            sub.Items.Add(Item("打开剪贴板中的链接", () => guide.OpenFromClipboard()));
            sub.Items.Add(Item("恢复小窗（关闭穿透、不透明）", () => guide.RestoreEmergency()));
            sub.Items.Add(new NativeMenuItemSeparator());
            sub.Items.Add(Item("小窗设置", OpenGuideSettings));
            menu.Items.Add(new NativeMenuItem("悬浮小窗") { Menu = sub });
            menu.Items.Add(new NativeMenuItemSeparator());
            guide.State.PropertyChanged += OnGuideStateChanged;
        }

        menu.Items.Add(Item(ExitText, Exit));

        Icon = new TrayIcon
        {
            Icon = LoadIcon(),
            ToolTipText = "BetterGI",
            Menu = menu,
        };
        // WPF 是左键双击；StatusNotifierItem 只有 Activate（左键单击）
        Icon.Clicked += (_, _) => ShowOrHide();
        TrayIcon.SetIcons(app, [Icon]);

        window.Closing += OnWindowClosing;
        Current = this;
    }

    /// <summary>程序图标（与 Windows 版相同的 logo.ico）</summary>
    public static WindowIcon LoadIcon()
    {
        using var stream = AssetLoader.Open(new Uri("avares://bgi-avalonia/Assets/logo.ico"));
        return new WindowIcon(stream);
    }

    private string GuideToggleText => _guide?.State.IsShown == true ? "隐藏悬浮小窗" : "显示悬浮小窗";

    private static NativeMenuItem Item(string header, Action action)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => action();
        return item;
    }

    private void OnGuideStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GuideWindowState.IsShown) && _guideToggle is not null)
        {
            Dispatcher.UIThread.Post(() => _guideToggle.Header = GuideToggleText);
        }
    }

    /// <summary>显示或隐藏主窗口（同 WPF NotifyIconViewModel.ShowOrHide）</summary>
    public void ShowOrHide()
    {
        if (_window.IsVisible && _window.WindowState != WindowState.Minimized)
        {
            _window.Hide();
            return;
        }

        ShowWindow();
    }

    private void ShowWindow()
    {
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void OpenGuideSettings()
    {
        ShowWindow();
        _vm.Navigate("悬浮小窗");
    }

    private void Exit()
    {
        _log.Information("托盘菜单：退出 BetterGI");
        _desktop.Shutdown();
    }

    /// <summary>
    /// 点击窗口关闭按钮：开启“退出时最小化到系统托盘”时隐藏到托盘（同 WPF）；
    /// 桌面没有托盘宿主时正常退出并记日志。程序退出（Shutdown）引起的关闭不拦截
    /// </summary>
    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (e.CloseReason != WindowCloseReason.WindowClosing || _host?.Config.CommonConfig.ExitToTray != true) return;
        e.Cancel = true;
        if (await StatusNotifierWatcherProbe.IsAvailableAsync())
        {
            LastCloseAction = "tray";
            _window.Hide();
            _log.Information("已最小化到系统托盘，右键托盘图标退出");
            return;
        }

        LastCloseAction = "exit";
        _log.Warning("已开启“退出时最小化到系统托盘”，但桌面没有系统托盘（{Name}），直接退出", StatusNotifierWatcherProbe.WatcherName);
        _desktop.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.Closing -= OnWindowClosing;
        if (_guide is not null) _guide.State.PropertyChanged -= OnGuideStateChanged;
        Icon.Dispose();
        if (Current == this) Current = null;
    }
}
