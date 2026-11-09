using Microsoft.Extensions.DependencyInjection;
using FluentAvalonia.UI.Controls;
using BetterGenshinImpact.Service.Interface;
using Avalonia.Media;
using Avalonia.Controls;
using System.Threading.Tasks;
using System.Threading;
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
            _selectedCaptureMethod = GameHost.SelectedCaptureMethodFor(host.Platform, host.LinuxConfig);
            _bannerImageService = host.Services.GetService<IBannerImageService>();
            _ = TryLoadConfiguredNetworkBannerAsync(showErrorToast: false);
        }
    }

    public LinuxHostConfig? LinuxConfig => _host?.LinuxConfig;

    /// <summary>默认横幅图（WPF Resources/Images/banner.jpg）</summary>
    public const string DefaultBannerUri = "avares://bgi-avalonia/Assets/Images/banner.jpg";

    private global::Avalonia.Media.Imaging.Bitmap? _bannerImage;
    private IBannerImageService? _bannerImageService;
    private CancellationTokenSource? _bannerDownloadCts;

    /// <summary>
    /// 界面自测替换：下载网络背景图。参数为 (service, url, ct)；返回是否应用成功。
    /// 默认走 <see cref="IBannerImageService.DownloadAndSaveAsync"/>。
    /// </summary>
    public static Func<IBannerImageService, string, CancellationToken, Task<bool>>? DownloadNetworkBannerAsync { get; set; }

    /// <summary>当前打开的“网络图片地址”对话框（界面自测用）</summary>
    public static FAContentDialog? CurrentWebBannerDialog { get; private set; }

    /// <summary>首页横幅图片：本地自定义 / 网络图 / 默认图（优先级同 WPF：有配置的网络图则用网络文件，否则本地自定义，否则默认）</summary>
    public global::Avalonia.Media.Imaging.Bitmap? BannerImage => _bannerImage ??= LoadBannerImage();

    private void ReloadBanner()
    {
        var oldBmp = _bannerImage;
        _bannerImage = LoadBannerImage();
        OnPropertyChanged(nameof(BannerImage));
        OnPropertyChanged(nameof(IsCustomBanner));
        OnPropertyChanged(nameof(IsCustomNetworkBanner));
        oldBmp?.Dispose();
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
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            CancelBannerDownload();
            _bannerImageService?.ResetNetworkImage();
            ApplyCustomBanner(path);
        }
    }

    /// <summary>把图片设为自定义横幅</summary>
    public void ApplyCustomBanner(string path)
    {
        try
        {
            var oldBmp = _bannerImage;
            _bannerImage = null;
            oldBmp?.Dispose();
            HomeBannerImage.CopyToCustom(path);
            IsCustomNetworkBanner = false;
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

    /// <summary>使用网络图片（对照 WPF ChangeWebBannerImage / WebImageInput）</summary>
    [RelayCommand]
    private async Task ChangeWebBannerImage()
    {
        if (_bannerImageService is null)
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, "网络背景服务不可用。");
            return;
        }

        if (AvaloniaPickers.MainWindow is not { } owner) return;
        CancelBannerDownload();
        var initial = "";
        try { initial = _bannerImageService.ReadConfiguredUrl() ?? ""; }
        catch (Exception e) { _log.Warning(e, "读取网络背景图片地址失败"); }

        var box = new TextBox { Name = "WebBannerUrlBox", Text = initial, MinWidth = 360, PlaceholderText = "https://..." };
        var label = new TextBlock
        {
            Text = "图片直链地址：",
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            Margin = new global::Avalonia.Thickness(0, 0, 8, 0),
        };
        DockPanel.SetDock(label, Dock.Left);
        var row = new DockPanel();
        row.Children.Add(label);
        row.Children.Add(box);
        var content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "请在下方输入网络图片地址", FontSize = 16 },
                row,
            },
        };

        var dialog = new FAContentDialog
        {
            Title = "请输入网络图片地址",
            Content = content,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = FAContentDialogButton.Primary,
        };
        CurrentWebBannerDialog = dialog;
        FAContentDialogResult result;
        try { result = await FluentMessageBox.ShowCustomAsync(dialog, owner); }
        finally { CurrentWebBannerDialog = null; }
        if (result != FAContentDialogResult.Primary) return;

        var url = box.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(url))
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, "请输入网络图片地址。");
            return;
        }

        await DownloadAndApplyNetworkBannerAsync(url, showErrorToast: true, saveUrl: true);
    }

    /// <summary>刷新已配置的网络图片</summary>
    [RelayCommand]
    private async Task RefreshWebBannerImage()
    {
        if (_bannerImageService is null) return;
        try
        {
            var url = _bannerImageService.ReadConfiguredUrl();
            if (!string.IsNullOrEmpty(url))
            {
                await DownloadAndApplyNetworkBannerAsync(url, showErrorToast: true, saveUrl: false);
            }
            else
            {
                CancelBannerDownload();
                IsCustomNetworkBanner = false;
                ReloadBanner();
            }
        }
        catch (Exception e)
        {
            _log.Error(e, "刷新背景图片失败");
            UiHost.Dialogs.Toast(DialogKind.Error, $"刷新背景图片失败：{e.Message}");
        }
    }

    /// <summary>恢复默认图片（删除本地自定义与网络背景配置，同 WPF）</summary>
    [RelayCommand]
    private void ResetBannerImage()
    {
        CancelBannerDownload();
        var oldBmp = _bannerImage;
        _bannerImage = null;
        oldBmp?.Dispose();
        try
        {
            HomeBannerImage.DeleteCustom();
            _bannerImageService?.ResetNetworkImage();
            IsCustomNetworkBanner = false;
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

    /// <summary>当前横幅是否为本地自定义图片（非网络）</summary>
    public bool IsCustomBanner { get; private set; }

    /// <summary>当前横幅是否为网络图片（同 WPF IsCustomNetworkBanner；控制“刷新网络图片”菜单可见）</summary>
    [ObservableProperty] private bool _isCustomNetworkBanner;

    private async Task TryLoadConfiguredNetworkBannerAsync(bool showErrorToast)
    {
        if (_bannerImageService is null) return;
        try
        {
            var url = _bannerImageService.ReadConfiguredUrl();
            if (!string.IsNullOrEmpty(url))
            {
                await DownloadAndApplyNetworkBannerAsync(url, showErrorToast, saveUrl: false);
            }
        }
        catch (Exception e)
        {
            _log.Error(e, "初始化背景图片失败，使用现有背景图片");
        }
    }

    private async Task DownloadAndApplyNetworkBannerAsync(string url, bool showErrorToast, bool saveUrl)
    {
        if (_bannerImageService is null) return;
        CancelBannerDownload();
        var cts = new CancellationTokenSource();
        _bannerDownloadCts = cts;
        try
        {
            var download = DownloadNetworkBannerAsync ?? ((svc, u, ct) => svc.DownloadAndSaveAsync(u, ct));
            if (!await download(_bannerImageService, url, cts.Token))
            {
                return;
            }

            cts.Token.ThrowIfCancellationRequested();
            if (saveUrl) _bannerImageService.SaveConfiguredUrl(url);
            // 网络图优先：清掉本地自定义文件锁冲突前先卸位图
            var oldBmp = _bannerImage;
            _bannerImage = null;
            oldBmp?.Dispose();
            HomeBannerImage.DeleteCustom();
            IsCustomNetworkBanner = true;
            ReloadBanner();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // 新操作替代或恢复默认时无需提示
        }
        catch (Exception e)
        {
            _log.Error(e, "下载自定义背景图片 url 失败，使用现有背景图片");
            if (showErrorToast)
            {
                UiHost.Dialogs.Toast(DialogKind.Error, $"下载自定义背景图片url失败：{e.Message}");
            }

            IsCustomNetworkBanner = false;
            ReloadBanner();
        }
        finally
        {
            if (ReferenceEquals(_bannerDownloadCts, cts)) _bannerDownloadCts = null;
            cts.Dispose();
        }
    }

    private void CancelBannerDownload()
    {
        var cts = _bannerDownloadCts;
        _bannerDownloadCts = null;
        cts?.Cancel();
        _bannerImageService?.InvalidatePendingDownloads();
    }

    private global::Avalonia.Media.Imaging.Bitmap? LoadBannerImage()
    {
        try
        {
            // 网络图优先（与 WPF RefreshBannerImage / LoadFallback 一致）
            if (_bannerImageService is not null && File.Exists(_bannerImageService.NetworkImagePath)
                && !string.IsNullOrEmpty(_bannerImageService.ReadConfiguredUrl()))
            {
                IsCustomBanner = false;
                IsCustomNetworkBanner = true;
                _log.Information("已加载自定义背景图片url");
                return new global::Avalonia.Media.Imaging.Bitmap(_bannerImageService.NetworkImagePath);
            }

            if (HomeBannerImage.ResolveCustomFile() is { } custom)
            {
                IsCustomBanner = true;
                IsCustomNetworkBanner = false;
                _log.Information("已加载自定义背景图片");
                return new global::Avalonia.Media.Imaging.Bitmap(custom);
            }
        }
        catch (Exception e)
        {
            _log.Error(e, "初始化背景图片失败，使用默认图片");
        }

        IsCustomBanner = false;
        IsCustomNetworkBanner = false;
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

    /// <summary>当前平台下可选的截图方式（Wayland 会话下为 XGetImage 窗口 / Composite / Portal）</summary>
    public IReadOnlyList<CaptureMethodInfo> CaptureMethods => _host?.AvailableCaptureMethods ?? GameHost.CaptureMethods;

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
