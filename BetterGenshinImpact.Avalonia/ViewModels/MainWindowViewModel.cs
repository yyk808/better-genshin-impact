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
    private readonly DispatcherTimer _toastTimer;

    [ObservableProperty] private NavItem _selectedNav;
    [ObservableProperty] private PageViewModel _currentPage;
    [ObservableProperty] private string? _toastText;
    [ObservableProperty] private string _toastKind = "Information";
    [ObservableProperty] private bool _isLogExpanded; // 与 WPF 版相同默认不显示日志面板（底部按钮展开）
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
        History = new TaskHistoryPageViewModel(host, TaskPlan, log);
        Settings = new SettingsPageViewModel(host, log,
            () => Home.CanStop ? Home.StopAsync() : Home.CanStart ? Home.StartAsync() : Task.CompletedTask,
            taskId => Tasks.ToggleAsync(taskId));
        // 导航栏与 WPF 版 MainWindow.xaml 相同的顺序、标题与图标；尚未迁移的页面为说明页（灰色）
        NavItems =
        [
            new("启动", "Play24", Home),
            new("实时触发", "Timer24", Trigger),
            new("独立任务", "TaskListSquareLtr24", Tasks),
            new("任务计划", "BranchFork24", TaskPlan),
            new("新一条龙", "AnimalTurtle24", Placeholder("新一条龙", "Pulonia 新一条龙（PuloniaOneDragonPage）"), isAvailable: false),
            new("执行记录", "History24", History),
            new("狛荷屋(test)", "Box24", Placeholder("狛荷屋(test)", "账号记录（AccountRecordsPage）"), isAvailable: false),
            new("一条龙", "AnimalTurtle24", OneDragon),
            new("全自动", "Bot24", Auto, () => Auto.SelectedTab = 0)
            {
                Children =
                [
                    new("调度器", "DeveloperBoard24", Auto, () => Auto.SelectedTab = 0),
                    new("JS 脚本", "DocumentJs16", Auto, () => Auto.SelectedTab = 1),
                    new("地图追踪", "Map24", Auto, () => Auto.SelectedTab = 2),
                    new("录制回放", "Record24", Auto, () => Auto.SelectedTab = 3),
                ],
            },
            new("辅助操控", "XboxController24", Placeholder("辅助操控", "宏与辅助操控设置（MacroSettingsPage）"), isAvailable: false),
            new("自动演奏", "MusicNote224", Placeholder("自动演奏", "自动演奏（MusicPage）"), isAvailable: false),
            new("悬浮小窗", "VideoClip24", Placeholder("悬浮小窗", "悬浮小窗设置（GuideWindowSettingsPage）"), isAvailable: false),
            new("快捷键", "Flash24", Settings, () => Settings.SelectedTab = 1),
            new("通知", "Alert24", Settings, () => Settings.SelectedTab = 2),
        ];
        FooterItems =
        [
            new("插件", "PuzzlePiece24", Placeholder("插件", "功能选择与插件管理（PluginsPage）"), isAvailable: false),
            // Linux 版独有：运行环境检查（WPF 版没有此页）
            new("环境", "WindowDevTools24", Environment),
            new("设置", "Settings24", Settings, () => Settings.SelectedTab = 0),
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

    public TaskHistoryPageViewModel History { get; }

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
        value.OnSelected?.Invoke();
        value.Page.OnNavigatedTo();
    }

    /// <summary>按标题切换页面（命令行 --page 与截图用）</summary>
    public bool Navigate(string title)
    {
        var item = AllNavItems().FirstOrDefault(n => n.Title == title);
        if (item is null)
        {
            return false;
        }

        SelectedNav = item;
        return true;
    }

    /// <summary>全部导航项（含子项）</summary>
    public IEnumerable<NavItem> AllNavItems() =>
        NavItems.Concat(FooterItems).SelectMany(n => n.Children.Prepend(n));

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

    private static PlaceholderPageViewModel Placeholder(string title, string wpfPage) =>
        new(title, $"“{title}”尚未迁移到 Linux（Avalonia）版，对应 Windows 版页面：{wpfPage}。可在 Windows 版中使用，Linux 版会按迁移计划逐步加入。");
}
