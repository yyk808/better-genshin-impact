using System;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.Service.Guide;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Pages;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui;
using FormsScreen = System.Windows.Forms.Screen;

namespace BetterGenshinImpact.ViewModel.Windows;

/// <summary>
/// 悬浮小窗 ViewModel 的 Windows 部分：WPF 计时器、剪贴板、显示器列表、主界面导航
/// </summary>
public partial class GuideWindowViewModel
{
    private readonly INavigationService _navigationService;

    /// <summary>鼠标进入计时</summary>
    private readonly DispatcherTimer _enterTimer;

    /// <summary>鼠标离开计时</summary>
    private readonly DispatcherTimer _leaveTimer;

    /// <summary>鼠标静止计时</summary>
    private readonly DispatcherTimer _idleTimer;

    /// <summary>热键反馈显示计时</summary>
    private readonly DispatcherTimer _osdTimer;

    /// <param name="service">小窗服务</param>
    /// <param name="configService">配置服务</param>
    /// <param name="library">收藏与观看记录</param>
    /// <param name="navigationService">主窗口导航</param>
    public GuideWindowViewModel(IGuideWindowService service, IConfigService configService, GuideLibraryStore library,
        INavigationService navigationService)
    {
        _service = service;
        _library = library;
        _navigationService = navigationService;
        _hotKeyConfig = configService.Get().HotKeyConfig;
        var dispatcher = Application.Current.Dispatcher;
        _enterTimer = new DispatcherTimer(HoverEnterDelay, DispatcherPriority.Normal, OnEnterTimer, dispatcher);
        _leaveTimer = new DispatcherTimer(HoverLeaveDelay, DispatcherPriority.Normal, OnLeaveTimer, dispatcher);
        _idleTimer = new DispatcherTimer(IdleHideDelay, DispatcherPriority.Background, OnIdleTimer, dispatcher);
        _osdTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(1200), DispatcherPriority.Normal, OnOsdTimer, dispatcher);
        _enterTimer.Stop();
        _leaveTimer.Stop();
        _idleTimer.Stop();
        _osdTimer.Stop();
        State.PropertyChanged += OnStatePropertyChanged;
        Config.PropertyChanged += OnConfigPropertyChanged;
    }

    /// <summary>
    /// 打开 BetterGI 主界面的“悬浮小窗”设置页
    /// </summary>
    [RelayCommand]
    private void OpenSettings()
    {
        var main = Application.Current.MainWindow;
        if (main is null)
        {
            return;
        }

        main.Show();
        if (main.WindowState == WindowState.Minimized)
        {
            main.WindowState = WindowState.Normal;
        }

        main.Activate();
        _navigationService.Navigate(typeof(GuideWindowSettingsPage));
    }

    /// <summary>写入剪贴板</summary>
    private static void SetClipboardText(string text) => Clipboard.SetText(text);

    /// <summary>显示器列表（顺序与服务移动到显示器时一致）</summary>
    private static (int Width, int Height, bool Primary)[] ScreenInfos() =>
        Array.ConvertAll(FormsScreen.AllScreens, s => (s.Bounds.Width, s.Bounds.Height, s.Primary));
}
