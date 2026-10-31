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
    // 导航图标：Fluent System Icons（Regular）码位，与 WPF 版 MainWindow.xaml 的 SymbolRegular 相同
    private const string IconHome = "\uF606"; // Play24
    private const string IconBolt = "\uF827"; // Timer24
    private const string IconTask = "\uEC9A"; // TaskListSquareLtr24
    private const string IconCalendar = "\uE1DF"; // BranchFork24
    private const string IconDragon = "\uE056"; // AnimalTurtle24
    private const string IconAuto = "\uF1F9"; // Bot24
    private const string IconChip = "\uF35C"; // DeveloperBoard24（WPF 版没有“环境”页）
    private const string IconSettings = "\uF6AA"; // Settings24

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
        Trigger = new TriggerPageViewModel(host?.Config, host?.QuestVoice, host?.Services);
        Environment = new EnvironmentPageViewModel(host, hostError, log);
        Tasks = new TaskSettingsPageViewModel(host, log);
        OneDragon = new OneDragonPageViewModel(host, Tasks, log);
        ScriptControl = new ScriptControlPageViewModel(host, log);
        Auto = new AutoPageViewModel(host, ScriptControl, log);
        TaskPlan = new TaskPlanPageViewModel(host, log);
        Settings = new SettingsPageViewModel(host, log,
            () => Home.CanStop ? Home.StopAsync() : Home.CanStart ? Home.StartAsync() : Task.CompletedTask,
            taskId => Tasks.ToggleAsync(taskId));
        NavItems =
        [
            new("启动", IconHome, Home),
            new("实时触发", IconBolt, Trigger),
            new("独立任务", IconTask, Tasks),
            new("任务计划", IconCalendar, TaskPlan),
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

    public TaskPlanPageViewModel TaskPlan { get; }

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
