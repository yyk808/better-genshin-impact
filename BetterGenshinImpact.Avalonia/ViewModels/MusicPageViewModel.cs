using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Music.Model;
using BetterGenshinImpact.GameTask.Music.Service;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Host.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ILogger = Serilog.ILogger;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// “自动演奏”页（对应 WPF MusicPage）：曲谱目录与历史、搜索 / 格式 / 乐器筛选、播放控制、播放模式、输入方式、乐器档案与移调。
/// 曲库扫描、解析、时间轴与按键演奏都使用 Core 的 Music 服务（与 WPF 相同的实现、MusicConfig 与 music 状态文件）；
/// 按键经 Core 的 KeyInputTransports → InputHub（Linux 无后台通道时，游戏在前台才发键）。
/// 引导式跟弹依赖 WPF 遮罩层，Linux 暂不提供。
/// </summary>
public sealed partial class MusicPageViewModel : PageViewModel, IDisposable
{
    public const string AllFormats = "全部格式";
    public const string AllInstruments = "全部乐器";

    private readonly ILogger _log;
    private readonly IMusicStateStore _stateStore;
    private readonly IInstrumentProfileService _profileService;
    private readonly IMusicTimelineBuilder _timelineBuilder;
    private readonly IMusicLibraryService _libraryService;
    private readonly IMusicPlaybackService _playbackService;
    private IReadOnlyList<PerformanceScore> _activeQueue = [];
    private CancellationTokenSource? _refreshCts;
    private bool _initialized;
    private bool _isUpdatingSelection;
    private bool _isLoading;

    [ObservableProperty] private PerformanceScore? _selectedMusicItem;
    [ObservableProperty] private PerformanceScore? _currentMusicItem;
    [ObservableProperty] private InstrumentProfile? _selectedInstrumentProfile;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedFormatFilter = AllFormats;
    [ObservableProperty] private string _selectedInstrumentFilter = AllInstruments;
    [ObservableProperty] private MusicFolderHistoryItem? _selectedMusicFolder;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(InputModeDisplayText))] private bool _useBackgroundInput = true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PlaybackModeIcon), nameof(PlaybackModeGlyph), nameof(PlaybackModeToolTip))] private MusicPlaybackMode _playbackMode;
    [ObservableProperty] private int _transpose;
    [ObservableProperty] private double _currentPositionMilliseconds;
    [ObservableProperty] private double _totalDurationMilliseconds = 1;
    [ObservableProperty] private string _currentTimeText = "00:00";
    [ObservableProperty] private string _totalTimeText = "00:00";
    [ObservableProperty] private string _currentTrackName = "未播放";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PlayPauseIcon), nameof(PlayPauseGlyph))] private bool _isPlaying;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(EmptyTitle))] private bool _hasMusicItems;
    [ObservableProperty] private bool _hasVisibleItems;
    [ObservableProperty] private string _libraryStatusText = "尚未选择有效的曲谱目录";
    [ObservableProperty] private string _musicFolderDisplayText = "尚未选择曲谱目录";
    [ObservableProperty] private string _message = "";

    public MusicPageViewModel(GameHost? host, ILogger log)
    {
        _log = log;
        Config = host?.Config ?? new AllConfig();
        _stateStore = new MusicStateStore();
        _profileService = new InstrumentProfileService();
        _timelineBuilder = new MusicTimelineBuilder(_profileService);
        _libraryService = new MusicLibraryService(new MusicScoreParser(), _stateStore);
        _playbackService = new MusicPlaybackService(_timelineBuilder, _profileService, new MusicInstrumentSwitcher(host?.RuntimeService ?? (IGameRuntimeHost)NoRuntimeHost.Instance),
            [new PostMessageKeyInputTransport(), new SendInputKeyInputTransport()]);

        MusicFolderDisplayText = string.IsNullOrWhiteSpace(Config.MusicConfig.MusicFolder) ? "尚未选择曲谱目录" : GetFolderDisplayName(Config.MusicConfig.MusicFolder);
        foreach (var f in _stateStore.State.MusicFolderHistory.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            MusicFolderHistory.Add(new MusicFolderHistoryItem(f));
        }

        if (!string.IsNullOrWhiteSpace(Config.MusicConfig.MusicFolder))
        {
            AddMusicFolderToHistory(Config.MusicConfig.MusicFolder);
            _selectedMusicFolder = MusicFolderHistory.FirstOrDefault(x => Same(x.FullPath, Config.MusicConfig.MusicFolder));
        }

        Profiles = _profileService.Profiles;
        _useBackgroundInput = Config.MusicConfig.InputMode != nameof(MusicInputMode.ForegroundSendInput);
        _playbackMode = Config.MusicConfig.PlaybackMode switch
        {
            nameof(MusicPlaybackMode.SingleLoop) => MusicPlaybackMode.SingleLoop,
            nameof(MusicPlaybackMode.Shuffle) => MusicPlaybackMode.Shuffle,
            _ => MusicPlaybackMode.Sequential
        };
        _libraryService.FilesChanged += (_, _) => Dispatcher.UIThread.Post(() => _ = RefreshLibraryAsync());
        _libraryService.ScoreParseFailed += (_, e) => Dispatcher.UIThread.Post(() => Message = $"曲谱解析失败，已跳过 {Path.GetFileName(e.FilePath)}：{e.ErrorMessage}");
        _playbackService.SnapshotChanged += OnPlaybackSnapshotChanged;
    }

    public override string Title => "自动演奏";

    public AllConfig Config { get; }

    public IMusicPlaybackService PlaybackService => _playbackService;

    /// <summary>当前筛选结果（与 WPF 的 MusicItemsView 相同：播放队列取自此列表）</summary>
    public ObservableCollection<PerformanceScore> MusicItems { get; } = [];

    public List<PerformanceScore> AllMusicItems { get; } = [];

    public ObservableCollection<InstrumentProfile> Profiles { get; }

    public ObservableCollection<MusicFolderHistoryItem> MusicFolderHistory { get; } = [];

    public ObservableCollection<string> FormatFilters { get; } = [AllFormats];

    public ObservableCollection<string> InstrumentFilters { get; } = [AllInstruments];

    public string[] PlayModes { get; } = ["自动演奏", "引导式跟弹（Linux 暂不支持）"];

    private int _playModeIndex;

    public int PlayModeIndex
    {
        get => _playModeIndex;
        set
        {
            if (!SetProperty(ref _playModeIndex, value) || value == 0) return;
            Message = "引导式跟弹需要游戏画面上的引导圈遮罩，Linux 版暂不支持，仍使用自动演奏";
            // 绑定写回过程中不能立即改回，稍后再让下拉框回到“自动演奏”
            Dispatcher.UIThread.Post(() => PlayModeIndex = 0);
        }
    }

    public string PlayPauseIcon => IsPlaying ? "Pause24" : "Play24";

    public string PlayPauseGlyph => FluentSymbols.Glyph(PlayPauseIcon);

    public string PlaybackModeGlyph => FluentSymbols.Glyph(PlaybackModeIcon);

    public string PlaybackModeIcon => PlaybackMode switch
    {
        MusicPlaybackMode.SingleLoop => "ArrowRepeatAll24",
        MusicPlaybackMode.Shuffle => "ArrowShuffle24",
        _ => "ArrowWrap20"
    };

    public string PlaybackModeToolTip => $"播放模式：{GetPlaybackModeText(PlaybackMode)}（点击切换）";

    public string EmptyTitle => HasMusicItems ? "没有匹配的曲目" : "曲库还是空的";

    public string InputModeDisplayText => UseBackgroundInput ? "后台" : "前台";

    public override void OnNavigatedTo()
    {
        if (_initialized) return;
        _initialized = true;
        _libraryService.Watch(Config.MusicConfig.MusicFolder);
        _ = RefreshLibraryAsync();
    }

    partial void OnSelectedMusicItemChanged(PerformanceScore? value)
    {
        if (value == null) return;
        _isUpdatingSelection = true;
        var profile = string.IsNullOrWhiteSpace(value.OutputProfileName) ? _profileService.Find(value.Instrument) : _profileService.Find(value.OutputProfileName);
        SelectedInstrumentProfile = profile;
        value.OutputProfileName = profile.Name;
        Transpose = value.Transpose;
        _isUpdatingSelection = false;
        _timelineBuilder.Build(value, profile, Transpose);
        if (!_isLoading && _playbackService.Snapshot.State == MusicPlaybackState.Stopped)
        {
            UpdateStoppedPlaybackDisplay(value, TimeSpan.Zero);
            SavePlaybackState(value, TimeSpan.Zero);
        }
    }

    partial void OnSelectedInstrumentProfileChanged(InstrumentProfile? value)
    {
        if (_isUpdatingSelection || value == null || SelectedMusicItem == null) return;
        SelectedMusicItem.OutputProfileName = value.Name;
        Config.MusicConfig.SelectedInstrumentProfile = value.Name;
        SaveSelectedPreference();
    }

    partial void OnTransposeChanged(int value)
    {
        if (_isUpdatingSelection || SelectedMusicItem == null) return;
        SelectedMusicItem.Transpose = Math.Clamp(value, -24, 24);
        SaveSelectedPreference();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedFormatFilterChanged(string value) => ApplyFilter();

    partial void OnSelectedInstrumentFilterChanged(string value) => ApplyFilter();

    partial void OnUseBackgroundInputChanged(bool value) => Config.MusicConfig.InputMode = GetInputMode().ToString();

    partial void OnPlaybackModeChanged(MusicPlaybackMode value)
    {
        Config.MusicConfig.PlaybackMode = value.ToString();
        _playbackService.SetPlaybackMode(value);
    }

    partial void OnSelectedMusicFolderChanged(MusicFolderHistoryItem? value)
    {
        if (value != null && !Same(value.FullPath, Config.MusicConfig.MusicFolder))
        {
            _ = SelectMusicFolderAsync(value.FullPath);
        }
    }

    [RelayCommand]
    private void CyclePlaybackMode() => PlaybackMode = PlaybackMode switch
    {
        MusicPlaybackMode.Sequential => MusicPlaybackMode.SingleLoop,
        MusicPlaybackMode.SingleLoop => MusicPlaybackMode.Shuffle,
        _ => MusicPlaybackMode.Sequential
    };

    [RelayCommand]
    private async Task ChooseFolderAsync()
    {
        if (AvaloniaPickers.MainWindow?.StorageProvider is not { CanPickFolder: true } storage) return;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择曲谱根目录", AllowMultiple = false });
        var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (path != null) await SelectMusicFolderAsync(path);
    }

    /// <summary>切换曲谱目录（与 WPF SelectMusicFolder 相同：记入历史、停止播放、监视并重新扫描）</summary>
    public async Task SelectMusicFolderAsync(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        if (!Directory.Exists(folder))
        {
            Message = "曲谱目录不可用：该历史目录不存在或当前无法访问。";
            return;
        }

        var normalized = Path.GetFullPath(folder);
        if (!Same(Config.MusicConfig.MusicFolder, normalized))
        {
            _playbackService.Stop();
        }

        AddMusicFolderToHistory(normalized);
        Config.MusicConfig.MusicFolder = normalized;
        SelectedMusicFolder = MusicFolderHistory.FirstOrDefault(x => Same(x.FullPath, normalized));
        MusicFolderDisplayText = GetFolderDisplayName(normalized);
        _libraryService.Watch(normalized);
        await RefreshLibraryAsync();
    }

    [RelayCommand]
    private async Task DeleteMusicFolderAsync()
    {
        var item = SelectedMusicFolder;
        if (item == null) return;
        MusicFolderHistory.Remove(item);
        SaveMusicFolderHistory();
        if (!Same(Config.MusicConfig.MusicFolder, item.FullPath)) return;
        var next = MusicFolderHistory.FirstOrDefault(x => Directory.Exists(x.FullPath))?.FullPath;
        if (next != null)
        {
            await SelectMusicFolderAsync(next);
            return;
        }

        _playbackService.Stop();
        SelectedMusicFolder = null;
        Config.MusicConfig.MusicFolder = string.Empty;
        MusicFolderDisplayText = "尚未选择曲谱目录";
        _libraryService.Watch(string.Empty);
        await RefreshLibraryAsync();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (!Directory.Exists(Config.MusicConfig.MusicFolder))
        {
            Message = "曲谱目录不可用：请先选择一个有效的曲谱目录。";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("xdg-open", $"\"{Config.MusicConfig.MusicFolder}\"") { UseShellExecute = false });
        }
        catch (Exception e)
        {
            Message = $"无法打开目录：{e.Message}";
        }
    }

    [RelayCommand]
    public Task RefreshAsync()
    {
        _libraryService.Watch(Config.MusicConfig.MusicFolder);
        return RefreshLibraryAsync();
    }

    [RelayCommand]
    private void PlayPause()
    {
        var state = _playbackService.Snapshot.State;
        if (state == MusicPlaybackState.Playing) _playbackService.Pause();
        else if (state == MusicPlaybackState.Paused) _playbackService.Resume();
        else StartPlaybackSession(true);
    }

    [RelayCommand]
    private async Task PlaySelectedAsync(PerformanceScore? item)
    {
        if (item is not { IsValid: true })
        {
            Message = $"无法播放：{item?.Error ?? "请选择一首有效曲谱。"}";
            return;
        }

        if (_playbackService.Snapshot.State != MusicPlaybackState.Stopped)
        {
            _playbackService.Stop();
            if (_sessionTask != null) await ObserveAsync(_sessionTask);
        }

        SelectedMusicItem = item;
        StartPlaybackSession(false);
    }

    [RelayCommand]
    private void Stop()
    {
        _playbackService.Stop();
        if (SelectedMusicItem != null)
        {
            UpdateStoppedPlaybackDisplay(SelectedMusicItem, TimeSpan.Zero);
            SavePlaybackState(SelectedMusicItem, TimeSpan.Zero);
        }
    }

    [RelayCommand]
    private void Next() => _playbackService.Next();

    [RelayCommand]
    private void Previous() => _playbackService.Previous();

    /// <summary>拖动进度条松开后定位（停止时只记住位置）</summary>
    public void Seek(double milliseconds)
    {
        var position = TimeSpan.FromMilliseconds(Math.Clamp(milliseconds, 0, TotalDurationMilliseconds));
        if (_playbackService.Snapshot.State == MusicPlaybackState.Stopped)
        {
            var score = CurrentMusicItem ?? SelectedMusicItem;
            if (score != null)
            {
                UpdateStoppedPlaybackDisplay(score, position);
                SavePlaybackState(score, position);
            }
        }
        else
        {
            _playbackService.Seek(position);
        }
    }

    [RelayCommand]
    private void SaveProfiles()
    {
        _profileService.Save();
        Message = "乐器档案已保存：新的音高映射将在下次开始曲目时生效。";
    }

    private Task? _sessionTask;

    public Task? SessionTask => _sessionTask;

    private void StartPlaybackSession(bool restoreSavedPosition)
    {
        if (SelectedMusicItem is not { IsValid: true } selected)
        {
            Message = $"无法播放：{SelectedMusicItem?.Error ?? "请选择一首有效曲谱。"}";
            return;
        }

        SaveAllPreferences();
        var queue = MusicItems.Where(x => x.IsValid).ToList();
        var startIndex = queue.IndexOf(selected);
        if (startIndex < 0) return;
        var options = new MusicPlaybackOptions
        {
            InputMode = GetInputMode(),
            PlaybackMode = PlaybackMode,
            Speed = Config.MusicConfig.Speed,
            CustomBpm = Config.MusicConfig.UseCustomBpm ? Math.Clamp(Config.MusicConfig.CustomBpm, 1, 1000) : null,
            AutoSwitchInstrument = Config.MusicConfig.AutoSwitchInstrument,
            StartPosition = restoreSavedPosition ? GetSavedPlaybackPosition(selected) : TimeSpan.Zero
        };
        _activeQueue = queue;
        Message = "";
        _sessionTask = Task.Run(() => new TaskRunner().RunThreadAsync((_, ct) => _playbackService.RunPlaylistAsync(queue, startIndex, options, ct)));
        _ = ObserveAsync(_sessionTask);
    }

    private async Task ObserveAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception e)
        {
            _log.Warning(e, "自动演奏结束：{Message}", e.Message);
        }
    }

    private async Task RefreshLibraryAsync()
    {
        _refreshCts?.Cancel();
        var cts = new CancellationTokenSource();
        _refreshCts = cts;
        IsRefreshing = true;
        LibraryStatusText = "正在扫描曲谱目录…";
        try
        {
            var scores = await _libraryService.ScanAsync(Config.MusicConfig.MusicFolder, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            _isLoading = true;
            var selectedPath = SelectedMusicItem?.FullPath;
            if (string.IsNullOrWhiteSpace(selectedPath)) selectedPath = _stateStore.State.CurrentTrackFullPath;
            AllMusicItems.Clear();
            foreach (var score in scores)
            {
                if (string.IsNullOrWhiteSpace(score.OutputProfileName)) score.OutputProfileName = _profileService.Find(score.Instrument).Name;
                AllMusicItems.Add(score);
            }

            RebuildFilters();
            SelectedMusicItem = MusicItems.FirstOrDefault(x => Same(x.FullPath, selectedPath)) ?? MusicItems.FirstOrDefault();
            if (_playbackService.Snapshot.State == MusicPlaybackState.Stopped) RestorePlaybackState();
            _isLoading = false;
            LibraryStatusText = Directory.Exists(Config.MusicConfig.MusicFolder)
                ? $"已载入 {AllMusicItems.Count} 首曲目，其中 {AllMusicItems.Count(x => x.IsValid)} 首可播放"
                : "尚未选择有效的曲谱目录";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            _log.Error(e, "刷新曲库失败：{Folder}", Config.MusicConfig.MusicFolder);
            LibraryStatusText = "刷新失败，请检查曲谱目录";
            Message = $"刷新曲库失败：{e.Message}";
        }
        finally
        {
            if (ReferenceEquals(_refreshCts, cts)) IsRefreshing = false;
            _isLoading = false;
            HasMusicItems = AllMusicItems.Count > 0;
        }
    }

    private bool Matches(PerformanceScore s) =>
        (string.IsNullOrWhiteSpace(SearchText)
         || s.DisplayTitle.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
         || s.Author.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
         || s.RelativePath.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
        && (string.IsNullOrWhiteSpace(SelectedFormatFilter) || SelectedFormatFilter == AllFormats || s.FormatName == SelectedFormatFilter)
        && (string.IsNullOrWhiteSpace(SelectedInstrumentFilter) || SelectedInstrumentFilter == AllInstruments
            || s.Instrument.Split(',', StringSplitOptions.TrimEntries).Contains(SelectedInstrumentFilter, StringComparer.OrdinalIgnoreCase));

    private void ApplyFilter()
    {
        var selected = SelectedMusicItem;
        MusicItems.Clear();
        foreach (var s in AllMusicItems.Where(Matches)) MusicItems.Add(s);
        HasVisibleItems = MusicItems.Count > 0;
        if (selected != null && MusicItems.Contains(selected) && !ReferenceEquals(SelectedMusicItem, selected)) SelectedMusicItem = selected;
    }

    private void RebuildFilters()
    {
        var format = SelectedFormatFilter;
        var instrument = SelectedInstrumentFilter;
        FormatFilters.Clear();
        FormatFilters.Add(AllFormats);
        foreach (var f in AllMusicItems.Select(x => x.FormatName).Distinct().Order()) FormatFilters.Add(f);
        InstrumentFilters.Clear();
        InstrumentFilters.Add(AllInstruments);
        foreach (var i in AllMusicItems.SelectMany(x => x.Instrument.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                     .Distinct(StringComparer.OrdinalIgnoreCase).Order())
            InstrumentFilters.Add(i);
        SelectedFormatFilter = FormatFilters.Contains(format) ? format : AllFormats;
        SelectedInstrumentFilter = InstrumentFilters.Contains(instrument) ? instrument : AllInstruments;
        ApplyFilter();
    }

    private void SaveSelectedPreference()
    {
        if (SelectedMusicItem == null) return;
        SavePreference(SelectedMusicItem);
        _stateStore.Save();
    }

    private void SaveAllPreferences()
    {
        foreach (var item in AllMusicItems) SavePreference(item);
        _stateStore.Save();
    }

    private void SavePreference(PerformanceScore score) =>
        _stateStore.State.Items[score.RelativePath] = new MusicItemPreference
        {
            OutputProfileName = score.OutputProfileName,
            Transpose = score.Transpose,
            DisabledTrackIndexes = score.Tracks.Where(x => !x.IsEnabled).Select(x => x.Index).ToList()
        };

    private void AddMusicFolderToHistory(string folder)
    {
        var existing = MusicFolderHistory.FirstOrDefault(x => Same(x.FullPath, folder));
        if (existing != null)
        {
            if (MusicFolderHistory.IndexOf(existing) == 0) return;
            MusicFolderHistory.Remove(existing);
        }

        MusicFolderHistory.Insert(0, existing ?? new MusicFolderHistoryItem(folder));
        SaveMusicFolderHistory();
    }

    private void SaveMusicFolderHistory()
    {
        _stateStore.State.MusicFolderHistory = [.. MusicFolderHistory.Select(x => x.FullPath)];
        _stateStore.Save();
    }

    private void RestorePlaybackState()
    {
        if (SelectedMusicItem == null)
        {
            CurrentMusicItem = null;
            CurrentPositionMilliseconds = 0;
            TotalDurationMilliseconds = 1;
            CurrentTimeText = TotalTimeText = "00:00";
            CurrentTrackName = "未播放";
            return;
        }

        UpdateStoppedPlaybackDisplay(SelectedMusicItem, GetSavedPlaybackPosition(SelectedMusicItem));
    }

    private TimeSpan GetSavedPlaybackPosition(PerformanceScore score)
    {
        if (!Same(_stateStore.State.CurrentTrackFullPath, score.FullPath)) return TimeSpan.Zero;
        var ms = _stateStore.State.CurrentPositionMilliseconds;
        if (double.IsNaN(ms) || double.IsInfinity(ms) || ms <= 0) return TimeSpan.Zero;
        return TimeSpan.FromMilliseconds(Math.Min(ms, score.Duration.TotalMilliseconds));
    }

    private void UpdateStoppedPlaybackDisplay(PerformanceScore score, TimeSpan position)
    {
        CurrentMusicItem = score;
        position = position > score.Duration ? score.Duration : position;
        CurrentPositionMilliseconds = position.TotalMilliseconds;
        TotalDurationMilliseconds = Math.Max(1, score.Duration.TotalMilliseconds);
        CurrentTimeText = FormatTime(position);
        TotalTimeText = FormatTime(score.Duration);
        CurrentTrackName = score.DisplayTitle;
    }

    private void SavePlaybackState(PerformanceScore score, TimeSpan position)
    {
        _stateStore.State.CurrentTrackFullPath = score.FullPath;
        _stateStore.State.CurrentPositionMilliseconds = Math.Max(0, position.TotalMilliseconds);
        _stateStore.Save();
    }

    private long _lastSavedSecond = -1;

    private void OnPlaybackSnapshotChanged(object? sender, PlaybackSnapshot snapshot)
    {
        var queue = _activeQueue;
        Dispatcher.UIThread.Post(() =>
        {
            CurrentPositionMilliseconds = snapshot.Position.TotalMilliseconds;
            CurrentTimeText = FormatTime(snapshot.Position);
            TotalDurationMilliseconds = Math.Max(1, snapshot.Duration.TotalMilliseconds);
            TotalTimeText = FormatTime(snapshot.Duration);
            CurrentTrackName = string.IsNullOrWhiteSpace(snapshot.TrackName) ? "未播放" : snapshot.TrackName;
            IsPlaying = snapshot.State == MusicPlaybackState.Playing;
            IsPaused = snapshot.State == MusicPlaybackState.Paused;
            if (snapshot.QueueIndex >= 0 && snapshot.QueueIndex < queue.Count)
            {
                var current = queue[snapshot.QueueIndex];
                CurrentMusicItem = current;
                if (!ReferenceEquals(SelectedMusicItem, current)) SelectedMusicItem = current;
                var second = (long)snapshot.Position.TotalSeconds;
                if (snapshot.State == MusicPlaybackState.Paused || (snapshot.State == MusicPlaybackState.Playing && second != _lastSavedSecond))
                {
                    _lastSavedSecond = second;
                    SavePlaybackState(current, snapshot.Position);
                }
            }
        });
    }

    private MusicInputMode GetInputMode() => UseBackgroundInput ? MusicInputMode.BackgroundPostMessage : MusicInputMode.ForegroundSendInput;

    private static string GetPlaybackModeText(MusicPlaybackMode mode) => mode switch
    {
        MusicPlaybackMode.SingleLoop => "单曲循环",
        MusicPlaybackMode.Shuffle => "随机播放",
        _ => "顺序播放"
    };

    private static string FormatTime(TimeSpan time) => time.TotalHours >= 1
        ? time.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
        : time.ToString(@"mm\:ss", CultureInfo.InvariantCulture);

    private static string GetFolderDisplayName(string folder)
    {
        var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? folder : name;
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        _playbackService.Stop();
        _libraryService.Dispose();
    }
}

/// <summary>曲谱目录历史项（与 WPF MusicFolderHistoryItem 相同）</summary>
public sealed class MusicFolderHistoryItem(string fullPath)
{
    public string FullPath { get; } = fullPath;

    public string DisplayName
    {
        get
        {
            var name = Path.GetFileName(FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrWhiteSpace(name) ? FullPath : name;
        }
    }

    public override string ToString() => DisplayName;
}

/// <summary>没有游戏宿主（宿主初始化失败）时的占位：自动切换乐器直接跳过</summary>
internal sealed class NoRuntimeHost : IGameRuntimeHost
{
    public static readonly NoRuntimeHost Instance = new();

    public GameRuntime? Current => null;

    public Task<GameRuntime?> StartAsync(CancellationToken ct = default) => Task.FromResult<GameRuntime?>(null);
}
