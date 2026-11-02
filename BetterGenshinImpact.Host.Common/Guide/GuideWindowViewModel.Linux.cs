using System;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Service.Guide;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Windows;

/// <summary>
/// 悬浮小窗 ViewModel 的 Linux 部分：界面线程计时器、剪贴板、显示器列表、打开主界面设置页
/// （与 Windows 部分 GuideWindowViewModel.Wpf.cs 对应）
/// </summary>
public partial class GuideWindowViewModel
{
    /// <summary>打开主界面“悬浮小窗”设置页，由宿主提供</summary>
    private readonly Action? _openSettings;

    /// <summary>鼠标进入计时</summary>
    private readonly GuideUiTimer _enterTimer;

    /// <summary>鼠标离开计时</summary>
    private readonly GuideUiTimer _leaveTimer;

    /// <summary>鼠标静止计时</summary>
    private readonly GuideUiTimer _idleTimer;

    /// <summary>热键反馈显示计时</summary>
    private readonly GuideUiTimer _osdTimer;

    /// <param name="service">小窗服务</param>
    /// <param name="configService">配置服务</param>
    /// <param name="library">收藏与观看记录</param>
    /// <param name="openSettings">打开主界面设置页</param>
    public GuideWindowViewModel(IGuideWindowService service, IConfigService configService, GuideLibraryStore library,
        Action? openSettings = null)
    {
        _service = service;
        _library = library;
        _openSettings = openSettings;
        _hotKeyConfig = configService.Get().HotKeyConfig;
        _enterTimer = new GuideUiTimer(HoverEnterDelay, OnEnterTimer, start: false);
        _leaveTimer = new GuideUiTimer(HoverLeaveDelay, OnLeaveTimer, start: false);
        _idleTimer = new GuideUiTimer(IdleHideDelay, OnIdleTimer, start: false);
        _osdTimer = new GuideUiTimer(TimeSpan.FromMilliseconds(1200), OnOsdTimer, start: false);
        State.PropertyChanged += OnStatePropertyChanged;
        Config.PropertyChanged += OnConfigPropertyChanged;
    }

    /// <summary>
    /// 打开 BetterGI 主界面的“悬浮小窗”设置页
    /// </summary>
    [RelayCommand]
    private void OpenSettings() => _openSettings?.Invoke();

    /// <summary>写入剪贴板</summary>
    private static void SetClipboardText(string text) => UiHost.Clipboard.SetText(text);

    /// <summary>显示器列表（顺序与服务移动到显示器时一致：主显示器在前）</summary>
    private static (int Width, int Height, bool Primary)[] ScreenInfos()
    {
        var areas = GuideLinuxPlatform.ScreenBounds();
        var result = new (int Width, int Height, bool Primary)[areas.Length];
        for (var i = 0; i < areas.Length; i++)
        {
            result[i] = (areas[i].Width, areas[i].Height, i == 0);
        }

        return result;
    }
}
