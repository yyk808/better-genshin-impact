using System.Collections.ObjectModel;
using Avalonia.Threading;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Host.Common;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Service.Guide;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ILogger = Serilog.ILogger;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// “悬浮小窗”设置页（对应 WPF GuideWindowSettingsPage）：与 WPF 相同的设置项（直接编辑 config.json 的 GuideWindowConfig）、
/// 收藏与观看记录（Core 的 GuideLibraryStore，与 WPF 同一份 User/GuideWindow 文件）。
/// 小窗本身是 WebView2 置顶窗口，Linux 版暂未实现：打开 / 隐藏 / 关闭不可用，设置会在 Windows 版生效
/// </summary>
public sealed partial class GuideWindowSettingsPageViewModel : PageViewModel
{
    private readonly ILogger _log;
    private readonly Func<string, bool> _navigate;
    private readonly GuideLibraryStore _library;

    [ObservableProperty] private string _librarySummary = "";

    public GuideWindowSettingsPageViewModel(GameHost? host, Func<string, bool> navigate, ILogger log)
    {
        _log = log;
        _navigate = navigate;
        Config = (host?.Config ?? new AllConfig()).GuideWindowConfig;
        _library = new GuideLibraryStore(CoreServices.GetLogger<GuideLibraryStore>());
        _library.Changed += (_, _) => Dispatcher.UIThread.Post(RefreshLibrary);
        RefreshLibrary();
    }

    public override string Title => "悬浮小窗";

    public GuideWindowConfig Config { get; }

    public GuideLibraryStore Library => _library;

    public string StatusText => "Linux 版暂不支持悬浮小窗（需要 WebView2 置顶窗口），这里的设置与收藏会在 Windows 版中生效";

    public ObservableCollection<GuideFavoriteItem> Favorites { get; } = [];

    public ObservableCollection<GuideHistoryItem> History { get; } = [];

    public bool HasFavorites => Favorites.Count > 0;

    public bool HasHistory => History.Count > 0;

    public GuideOption[] AutoOpenOptions { get; } =
    [
        new(GuideAutoOpenMode.RestoreLast, "恢复上次状态"),
        new(GuideAutoOpenMode.Always, "总是打开"),
        new(GuideAutoOpenMode.Never, "不自动打开"),
    ];

    public GuideOption[] PositionModeOptions { get; } =
    [
        new(GuidePositionMode.FollowGame, "跟随游戏窗口"),
        new(GuidePositionMode.Free, "自由放置"),
    ];

    public GuideOption[] ResumeModeOptions { get; } =
    [
        new(GuideResumeMode.FromLibrary, "从收藏或观看记录打开时"),
        new(GuideResumeMode.Always, "打开任何看过的视频时"),
        new(GuideResumeMode.Never, "不续播"),
    ];

    public GuideOption[] InteractionExitOptions { get; } = [new(1, "鼠标离开 1 秒后"), new(2, "鼠标离开 2 秒后"), new(5, "鼠标离开 5 秒后"), new(0, "不自动结束")];

    public GuideOption[] SeekOptions { get; } = [new(3, "3 秒"), new(5, "5 秒"), new(10, "10 秒"), new(15, "15 秒"), new(30, "30 秒")];

    public GuideOption[] RateStepOptions { get; } = [new(0.1, "0.1x"), new(0.25, "0.25x"), new(0.5, "0.5x")];

    public GuideOption[] BoostRateOptions { get; } = [new(1.5, "1.5x"), new(2.0, "2x"), new(2.5, "2.5x"), new(3.0, "3x"), new(4.0, "4x")];

    public GuideOption[] VolumeStepOptions { get; } = [new(0.05, "5%"), new(0.1, "10%"), new(0.2, "20%")];

    public GuideOption[] ZoomOptions { get; } = [new(0.67, "67%"), new(0.8, "80%"), new(0.9, "90%"), new(1.0, "100%"), new(1.1, "110%"), new(1.25, "125%")];

    public override void OnNavigatedTo() => RefreshLibrary();

    [RelayCommand]
    private void ResetHomeUrl() => Config.HomeUrl = GuideWindowConfig.DefaultHomeUrl;

    /// <summary>与 WPF 相同：字幕位置与宽度恢复默认</summary>
    [RelayCommand]
    private void ResetSubtitlePosition()
    {
        Config.SubtitleCenterXRatio = 0.5;
        Config.SubtitleBottomRatio = 0.22;
        Config.SubtitleWidthRatio = 0.6;
    }

    [RelayCommand]
    private void OpenHotKeyPage() => _navigate("快捷键");

    [RelayCommand]
    private void RemoveFavorite(GuideFavoriteItem? item)
    {
        if (item is not null) _library.RemoveFavorite(item.Id);
    }

    [RelayCommand]
    private void RemoveHistory(GuideHistoryItem? item)
    {
        if (item is not null) _library.RemoveRecord(item.VideoKey);
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        if (AvaloniaPickers.MainWindow is not { } owner) return;
        if (await FluentMessageBox.ShowAsync(owner, "清空全部观看记录？收藏不受影响，清空后无法从上次位置继续观看。", "清空观看记录",
                DialogKind.Question, DialogButtons.OkCancel) == DialogResult.Ok)
        {
            _library.ClearHistory();
        }
    }

    /// <summary>重新读取收藏与观看记录（与 WPF 相同：观看记录最多显示 50 条）</summary>
    public void RefreshLibrary()
    {
        Favorites.Clear();
        foreach (var favorite in _library.GetFavorites())
        {
            Favorites.Add(new GuideFavoriteItem(_library, favorite, GuideLibraryPresentation.DescribeProgress(_library.FindRecord(favorite.VideoKey))));
        }

        History.Clear();
        foreach (var record in _library.GetHistory().Take(50))
        {
            History.Add(new GuideHistoryItem(record, GuideLibraryPresentation.DescribeProgress(record)));
        }

        LibrarySummary = $"收藏 {_library.FavoriteCount} · 观看记录 {_library.HistoryCount}";
        OnPropertyChanged(nameof(HasFavorites));
        OnPropertyChanged(nameof(HasHistory));
    }
}

/// <summary>下拉框选项（与 WPF GuideSettingOption 相同）</summary>
public sealed record GuideOption(object Value, string Name);

/// <summary>收藏项：标题可直接修改（写回收藏）</summary>
public sealed partial class GuideFavoriteItem : ObservableObject
{
    private readonly GuideLibraryStore _library;

    [ObservableProperty] private string _title;

    public GuideFavoriteItem(GuideLibraryStore library, GuideFavorite favorite, string progress)
    {
        _library = library;
        Id = favorite.Id;
        Url = favorite.Url;
        _title = favorite.Title;
        Progress = progress;
    }

    public string Id { get; }

    public string Url { get; }

    public string Progress { get; }

    partial void OnTitleChanged(string value) => _library.RenameFavorite(Id, value);
}

/// <summary>观看记录项</summary>
public sealed class GuideHistoryItem(GuideWatchRecord record, string progress)
{
    public string VideoKey { get; } = record.VideoKey;

    public string Title { get; } = string.IsNullOrWhiteSpace(record.Title) ? record.Url : record.Title;

    public string Url { get; } = record.Url;

    public string Progress { get; } = progress;
}
