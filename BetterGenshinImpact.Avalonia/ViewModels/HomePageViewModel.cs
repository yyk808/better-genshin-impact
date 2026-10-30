using Avalonia.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Host.Common;
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
