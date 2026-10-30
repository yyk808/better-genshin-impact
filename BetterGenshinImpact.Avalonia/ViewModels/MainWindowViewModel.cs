using System.Collections.ObjectModel;
using Avalonia.Threading;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Host.Common;
using BetterGenshinImpact.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using Serilog.Events;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>主窗口：左侧导航 + 内容区 + 底部日志面板 + 轻提示</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    // 24×24 图标路径
    private const string IconHome = "M3,11 L12,3 L21,11 L19,11 L19,21 L14,21 L14,15 L10,15 L10,21 L5,21 L5,11 Z";
    private const string IconBolt = "M13,2 L4,14 L11,14 L10,22 L20,9 L13,9 Z";
    private const string IconTask = "M4,4 H20 V7 H4 Z M4,10.5 H20 V13.5 H4 Z M4,17 H20 V20 H4 Z";
    private const string IconCalendar = "F0 M3,5 H21 V21 H3 Z M5,10 H19 V19 H5 Z M7,2 H9 V7 H7 Z M15,2 H17 V7 H15 Z";
    private const string IconDragon = "M12,2 L15,9 L22,9 L16.5,13.5 L18.5,21 L12,16.5 L5.5,21 L7.5,13.5 L2,9 L9,9 Z";
    private const string IconAuto = "M12,4 A8,8 0 1 1 4,12 H7 A5,5 0 1 0 12,7 Z M12,4 L12,1 L16,5 L12,9 Z";
    private const string IconChip = "F0 M6,6 H18 V18 H6 Z M9,9 H15 V15 H9 Z M9,2 H11 V6 H9 Z M13,2 H15 V6 H13 Z M9,18 H11 V22 H9 Z M13,18 H15 V22 H13 Z M2,9 H6 V11 H2 Z M2,13 H6 V15 H2 Z M18,9 H22 V11 H18 Z M18,13 H22 V15 H18 Z";
    private const string IconSettings = "M12,8 A4,4 0 1 1 12,16 A4,4 0 1 1 12,8 Z M11,1 H13 L13.5,4.5 L17,6 L20,4 L21.5,5.5 L19.5,8.5 L21,12 L23,12.5 V14.5 L19.5,15 L18,18.5 L20,21.5 L18.5,23 L15.5,21 L12,22.5 L11,23 L10.5,19.5 L7,18 L4,20 L2.5,18.5 L4.5,15.5 L3,12 L1,11.5 V9.5 L4.5,9 L6,5.5 L4,2.5 L5.5,1 L8.5,3 Z";

    private readonly DispatcherTimer _toastTimer;

    [ObservableProperty] private NavItem _selectedNav;
    [ObservableProperty] private PageViewModel _currentPage;
    [ObservableProperty] private string? _toastText;
    [ObservableProperty] private string _toastKind = "Information";
    [ObservableProperty] private bool _isLogExpanded = true;
    [ObservableProperty] private bool _showDebugLog;

    public MainWindowViewModel(GameHost? host, string? hostError, ILogger log)
    {
        Host = host;
        Home = new HomePageViewModel(host, log);
        Trigger = new TriggerPageViewModel(host?.Config);
        Environment = new EnvironmentPageViewModel(host, hostError, log);
        Tasks = new TaskSettingsPageViewModel(host, log);
        OneDragon = new OneDragonPageViewModel(host, Tasks, log);
        ScriptControl = new ScriptControlPageViewModel(host, log);
        Auto = new AutoPageViewModel(host, ScriptControl, log);
        Settings = new SettingsPageViewModel(host, log,
            () => Home.CanStop ? Home.StopAsync() : Home.CanStart ? Home.StartAsync() : Task.CompletedTask,
            taskId => Tasks.ToggleAsync(taskId));
        NavItems =
        [
            new("启动", IconHome, Home),
            new("实时触发", IconBolt, Trigger),
            new("独立任务", IconTask, Tasks),
            new("任务计划", IconCalendar, new PlaceholderPageViewModel("任务计划",
                "Windows 版的“任务计划”是新的计划系统（计划、执行记录、路线 CD、触发方式），尚未迁移到 Avalonia 宿主。" +
                "配置组（调度器）在“全自动 → 调度器”中，与 Windows 版共用 User/ScriptGroup。")),
            new("一条龙", IconDragon, OneDragon),
            new("全自动", IconAuto, Auto),
        ];
        FooterItems =
        [
            new("环境", IconChip, Environment),
            new("设置", IconSettings, Settings),
        ];
        _selectedNav = NavItems[0];
        _currentPage = Home;
        _toastTimer = new DispatcherTimer(TimeSpan.FromSeconds(4), DispatcherPriority.Normal, (_, _) => HideToast());
    }

    public GameHost? Host { get; }

    public HomePageViewModel Home { get; }

    public TriggerPageViewModel Trigger { get; }

    public EnvironmentPageViewModel Environment { get; }

    public TaskSettingsPageViewModel Tasks { get; }

    public OneDragonPageViewModel OneDragon { get; }

    public ScriptControlPageViewModel ScriptControl { get; }

    public AutoPageViewModel Auto { get; }

    public SettingsPageViewModel Settings { get; }

    public ObservableCollection<NavItem> NavItems { get; }

    public ObservableCollection<NavItem> FooterItems { get; }

    public ObservableCollection<LogLine> LogLines => UiLogSink.Instance.Lines;

    public string Version => typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "";

    public bool HasToast => !string.IsNullOrEmpty(ToastText);

    partial void OnToastTextChanged(string? value) => OnPropertyChanged(nameof(HasToast));

    partial void OnShowDebugLogChanged(bool value) =>
        UiLogSink.Instance.MinimumLevel = value ? LogEventLevel.Debug : LogEventLevel.Information;

    partial void OnSelectedNavChanged(NavItem value)
    {
        if (value is null)
        {
            return;
        }

        CurrentPage = value.Page;
        value.Page.OnNavigatedTo();
    }

    /// <summary>按标题切换页面（命令行 --page 与截图用）</summary>
    public bool Navigate(string title)
    {
        var item = NavItems.Concat(FooterItems).FirstOrDefault(n => n.Title == title);
        if (item is null)
        {
            return false;
        }

        SelectedNav = item;
        return true;
    }

    public void ShowToast(DialogKind kind, string message)
    {
        ToastKind = kind.ToString();
        ToastText = message;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    [RelayCommand]
    private void HideToast()
    {
        _toastTimer.Stop();
        ToastText = null;
    }

    [RelayCommand]
    private void CopyLog()
    {
        var text = string.Join(System.Environment.NewLine, LogLines.Select(l => l.Text));
        UiHost.Clipboard.SetText(text);
        ShowToast(DialogKind.Success, $"已复制 {LogLines.Count} 条日志到剪贴板");
    }

    [RelayCommand]
    private void ClearLog() => LogLines.Clear();

    [RelayCommand]
    private void ToggleLog() => IsLogExpanded = !IsLogExpanded;

    private static PlaceholderPageViewModel Placeholder(string title) =>
        new(title, $"“{title}”尚未迁移到 Avalonia 宿主。当前阶段只提供截图器启动与实时触发（自动拾取、自动剧情、自动开门等），其余页面会按迁移计划逐步加入。");
}
