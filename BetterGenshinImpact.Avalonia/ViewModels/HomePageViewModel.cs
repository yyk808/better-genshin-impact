using Avalonia.Threading;
using Avalonia.Platform.Storage;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Host.Common;
using BetterGenshinImpact.Host.Common.Config;
using BetterGenshinImpact.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>“启动”页：启动 / 停止截图器（对应 WPF 的 HomePageViewModel 的启动部分）</summary>
public sealed partial class HomePageViewModel : PageViewModel
{
    private readonly GameHost? _host;
    private readonly ILogger _log;
    private readonly DispatcherTimer _timer;
    private Task? _inferenceInit;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "截图器未启动";
    [ObservableProperty] private string _windowText = "—";
    [ObservableProperty] private string _framesText = "0";
    [ObservableProperty] private string _manualWindowText = "未选择（按进程名自动查找）";
    [ObservableProperty] private LaunchPreset? _selectedPreset;

    public HomePageViewModel(GameHost? host, ILogger log)
    {
        _host = host;
        _log = log;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => RefreshStatus());
        _timer.Start();
        if (host is null)
        {
            StatusText = "平台服务初始化失败，无法启动截图器（详见“环境”页与日志）";
        }
        else
        {
            _selectedCaptureMethod = GameHost.CaptureMethods.FirstOrDefault(m => m.Id == host.LinuxConfig.CaptureMode) ?? GameHost.CaptureMethods[0];
        }
    }

    public LinuxHostConfig? LinuxConfig => _host?.LinuxConfig;

    /// <summary>默认横幅图（WPF Resources/Images/banner.jpg）</summary>
    public const string DefaultBannerUri = "avares://bgi-avalonia/Assets/Images/banner.jpg";

    private global::Avalonia.Media.Imaging.Bitmap? _bannerImage;

    /// <summary>首页横幅图片：与 WPF 相同，有 User/Images/custom_banner.jpg 时用它，否则用默认图</summary>
    public global::Avalonia.Media.Imaging.Bitmap? BannerImage => _bannerImage ??= LoadBannerImage();

    private void ReloadBanner()
    {
        var old = _bannerImage;
        _bannerImage = LoadBannerImage();
        OnPropertyChanged(nameof(BannerImage));
        OnPropertyChanged(nameof(IsCustomBanner));
        old?.Dispose();
    }

    /// <summary>更换背景图片：选择本地图片（与 WPF 相同复制为 User/Images/custom_banner.jpg）</summary>
    [RelayCommand]
    private async Task ChangeBannerImage()
    {
        if (AvaloniaPickers.MainWindow?.StorageProvider is not { CanOpen: true } storage) return;
        var files = await storage.OpenFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = HomeBannerImage.PickTitle,
            AllowMultiple = false,
            FileTypeFilter = [new global::Avalonia.Platform.Storage.FilePickerFileType("图片文件") { Patterns = HomeBannerImage.ImagePatterns }, global::Avalonia.Platform.Storage.FilePickerFileTypes.All],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) ApplyCustomBanner(path);
    }

    /// <summary>把图片设为自定义横幅</summary>
    public void ApplyCustomBanner(string path)
    {
        try
        {
            // 先释放当前位图再覆盖文件
            var old = _bannerImage;
            _bannerImage = null;
            old?.Dispose();
            HomeBannerImage.CopyToCustom(path);
            ReloadBanner();
            UiHost.Dialogs.Toast(DialogKind.Success, HomeBannerImage.ChangedText);
        }
        catch (Exception e)
        {
            _log.Error(e, "更换背景图片失败");
            ReloadBanner();
            UiHost.Dialogs.Toast(DialogKind.Error, $"更换背景图片失败: {e.Message}");
        }
    }

    /// <summary>恢复默认图片（删除自定义图片；网络图片未迁移）</summary>
    [RelayCommand]
    private void ResetBannerImage()
    {
        var old = _bannerImage;
        _bannerImage = null;
        old?.Dispose();
        try
        {
            HomeBannerImage.DeleteCustom();
            ReloadBanner();
            UiHost.Dialogs.Toast(DialogKind.Success, HomeBannerImage.ResetText);
        }
        catch (Exception e)
        {
            _log.Error(e, "恢复默认背景图片失败");
            ReloadBanner();
            UiHost.Dialogs.Toast(DialogKind.Warning, HomeBannerImage.ResetDeleteFailedText);
        }
    }

    /// <summary>当前横幅是否为自定义图片</summary>
    public bool IsCustomBanner { get; private set; }

    private global::Avalonia.Media.Imaging.Bitmap? LoadBannerImage()
    {
        try
        {
            if (HomeBannerImage.ResolveCustomFile() is { } custom)
            {
                IsCustomBanner = true;
                _log.Information("已加载自定义背景图片");
                return new global::Avalonia.Media.Imaging.Bitmap(custom);
            }
        }
        catch (Exception e)
        {
            _log.Error(e, "初始化背景图片失败，使用默认图片");
        }

        IsCustomBanner = false;
        try
        {
            using var stream = global::Avalonia.Platform.AssetLoader.Open(new Uri(DefaultBannerUri));
            return new global::Avalonia.Media.Imaging.Bitmap(stream);
        }
        catch (Exception e)
        {
            _log.Error(e, "加载默认背景图片失败");
            return null;
        }
    }

    public IReadOnlyList<CaptureMethodInfo> CaptureMethods => GameHost.CaptureMethods;

    public IReadOnlyList<LaunchPreset> LaunchPresets => LinuxHostConfig.Presets;

    private CaptureMethodInfo? _selectedCaptureMethod;

    /// <summary>截图方式（保存到 linux-host.json，下次启动截图器生效）</summary>
    public CaptureMethodInfo? SelectedCaptureMethod
    {
        get => _selectedCaptureMethod;
        set
        {
            if (value is null || !SetProperty(ref _selectedCaptureMethod, value) || _host is null)
            {
                return;
            }

            _host.LinuxConfig.CaptureMode = value.Id;
            _log.Information("截图方式：{Name}（{Id}），下次启动截图器生效", value.Name, value.Id);
        }
    }

    public string ConfigFileText => _host?.ConfigFilePath is { } path
        ? $"配置保存在 {path}，Linux 宿主设置保存在 {_host.LinuxConfigFilePath}"
        : "配置只在内存中（--no-save-config），不会保存";

    partial void OnSelectedPresetChanged(LaunchPreset? value)
    {
        if (value is not null && _host is not null)
        {
            _host.LinuxConfig.LaunchCommand = value.Command;
        }
    }

    /// <summary>选择捕获窗口（列出 X11 顶层窗口）</summary>
    [RelayCommand]
    public async Task SelectWindowAsync()
    {
        if (_host is null)
        {
            return;
        }

        var selected = await AvaloniaPickers.PickWindowAsync(_host.ListWindows, _host.ManualWindow.Handle);
        if (selected is null)
        {
            return;
        }

        _host.ManualWindow.Handle = selected.Handle;
        ManualWindowText = selected.Display;
        _log.Information("手动选择捕获窗口：{Window}（0x{Handle:x}），下次启动截图器生效", selected.Display, selected.Handle);
    }

    /// <summary>直接选择指定窗口（无人值守验证用）</summary>
    public void SelectWindow(WindowInfo window)
    {
        if (_host is null)
        {
            return;
        }

        _host.ManualWindow.Handle = window.Handle;
        ManualWindowText = window.Display;
        _log.Information("手动选择捕获窗口：{Window}（0x{Handle:x}），下次启动截图器生效", window.Display, window.Handle);
    }

    [RelayCommand]
    private void ClearManualWindow()
    {
        if (_host is null)
        {
            return;
        }

        _host.ManualWindow.Handle = null;
        ManualWindowText = "未选择（按进程名自动查找）";
    }

    [RelayCommand]
    private async Task BrowseGamePathAsync()
    {
        if (_host is null)
        {
            return;
        }

        var path = await AvaloniaPickers.PickFileAsync("选择原神可执行文件（YuanShen.exe / GenshinImpact.exe）");
        if (path is not null)
        {
            _host.LinuxConfig.GamePath = path;
        }
    }

    /// <summary>立即按设置的命令启动游戏（不启动截图器）</summary>
    [RelayCommand]
    private async Task LaunchGameNowAsync()
    {
        if (_host is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            StatusText = "正在启动游戏…";
            var ok = await _host.LaunchGameAndWaitAsync();
            StatusText = ok ? "游戏已启动，可以启动截图器" : "启动游戏失败（详见日志）";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>打开云原神窗口（登录、排队或直接操作页面）</summary>
    [RelayCommand]
    private void OpenCloudGameWindow()
    {
        if (_host?.CloudGamePage is not { } page)
        {
            return;
        }

        try
        {
            page.ShowForInteraction(_host.CloudGameUrl);
            StatusText = "云原神窗口已打开：请完成登录并进入游戏，然后启动截图器";
        }
        catch (Exception e)
        {
            _log.Error(e, "打开云原神窗口失败");
            StatusText = "打开云原神窗口失败：" + e.Message;
        }
    }

    public override string Title => "启动";

    public AllConfig? Config => _host?.Config;

    public bool CanStart => _host is not null && !IsRunning && !IsBusy;

    public bool CanStop => _host is not null && IsRunning && !IsBusy;

    partial void OnIsRunningChanged(bool value) => NotifyCan();

    partial void OnIsBusyChanged(bool value) => NotifyCan();

    private void NotifyCan()
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    /// <summary>后台初始化推理（OCR），首次启动前完成</summary>
    public Task EnsureInferenceAsync()
    {
        if (_host is null)
        {
            return Task.CompletedTask;
        }

        return _inferenceInit ??= Task.Run(async () =>
        {
            try
            {
                await _host.InitializeInferenceAsync();
            }
            catch (Exception e)
            {
                _log.Error(e, "推理初始化失败");
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    public async Task StartAsync()
    {
        if (_host is null)
        {
            return;
        }

        IsBusy = true;
        StatusText = "正在启动截图器…";
        try
        {
            await EnsureInferenceAsync();
            var runtime = await _host.StartAsync();
            if (runtime is null)
            {
                StatusText = "启动失败：未找到游戏窗口（进程名 " + string.Join(" / ", _host.Options.ProcessNames) + "）";
                UiHost.Dialogs.Toast(DialogKind.Warning, StatusText);
                return;
            }

            // 首帧在后台线程同步调度一次，暴露初始化问题（异常写进日志）
            await Task.Run(_host.TickOnce);
            IsRunning = true;
            UiHost.Dialogs.Toast(DialogKind.Success, "截图器已启动");
        }
        catch (Exception e)
        {
            _log.Error(e, "启动截图器异常");
            StatusText = "启动失败：" + e.Message;
            await UiHost.Dialogs.ErrorAsync(e.Message, "启动截图器失败");
        }
        finally
        {
            IsBusy = false;
            RefreshStatus();
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    public async Task StopAsync()
    {
        if (_host is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _host.StopAsync();
        }
        finally
        {
            IsRunning = _host.IsRunning;
            IsBusy = false;
            RefreshStatus();
        }
    }

    private void RefreshStatus()
    {
        if (_host is null)
        {
            return;
        }

        var runtime = _host.RuntimeService.Current;
        IsRunning = runtime is not null;
        FramesText = _host.FramesProcessed.ToString("N0");
        if (runtime is null)
        {
            if (!IsBusy && StatusText.StartsWith("截图器运行中", StringComparison.Ordinal))
            {
                StatusText = "截图器未启动";
            }

            WindowText = "—";
            return;
        }

        var window = runtime.Window;
        WindowText = $"0x{window.Handle:x} {window.ProcessName}，画面 {window.Viewport.ScreenRect.Width}×{window.Viewport.ScreenRect.Height}" +
                     (window.IsForeground ? "，前台" : "，后台") + (window.IsMinimized ? "，最小化" : "");
        StatusText = $"截图器运行中（{(runtime.Capture.IsCapturing ? "截图中" : "截图已暂停")}，帧调度异常 {_host.TickErrorCount} 次）";
    }
}
