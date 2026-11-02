using System;
using System.Windows;
using System.Windows.Interop;
using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.SoloTask.Execution;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Instance.MessageHandlers;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BetterGenshinImpact.Pulonia.Remote;

namespace BetterGenshinImpact.Service.Instance;

/// <summary>实例服务的 WPF 部分：构造（桌面分身相对鼠标、配置）、激活主窗口与关闭应用。与平台无关的部分在 InstanceService.cs（Linux 版 Avalonia 共用）</summary>
public sealed partial class InstanceService
{
    public InstanceService(
        InstanceBootstrap bootstrap,
        IServiceProvider serviceProvider,
        RawInputMonitor rawInputMonitor,
        IConfigService configService,
        SoloTaskExecutionService soloTaskExecutionService,
        ILogger<InstanceService> logger)
    {
        _bootstrap = bootstrap;
        _logger = logger;
        _soloTaskExecutionService = soloTaskExecutionService;
        _soloTaskExecutionService.StatusChanged += OnLocalSoloTaskStatusChanged;
        _relativeMouseMessageHandler = new RelativeMouseMessageHandler(
            Context,
            serviceProvider,
            rawInputMonitor,
            IsRootConnection,
            logger);
        if (Context.InstanceType == BetterGiInstanceType.Primary)
        {
            _relativeMouseMessageHandler.SetGameMouseModeEnabled(
                configService.Get().ChildSessionConfig.GameMouseModeEnabled);
        }
        // Pulonia 一步下发的子实例宿主与主实例客户端依赖 Pulonia 服务，收到请求时才从容器取得，避免构造期循环依赖
        var puloniaRunMessageHandler = new PuloniaRunMessageHandler(
            Context,
            serviceProvider.GetRequiredService<PuloniaRunDispatchHost>,
            serviceProvider.GetRequiredService<PuloniaRunDispatcher>);
        _requestHandler = new InstanceRequestHandler(
            Context,
            _messageState,
            _relativeMouseMessageHandler,
            EnqueueActivation,
            DispatchWebViewMessage,
            _soloTaskExecutionService,
            ReceiveSoloTaskStatus,
            NotifyConnectedInstancesChanged,
            puloniaRunMessageHandler,
            logger);
    }

    private partial void DispatchActivation(string[] args)
    {
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            var commandLineOptions = CommandLineOptions.Parse(args);
            if (commandLineOptions.IsPuloniaAction)
            {
                // 二次启动转发来的 --pulonia-run 提交到接收转发的本实例（主实例或桌面分身），
                // 不激活主窗口，不与用户正在操作的桌面争抢焦点。
                _ = CoreServices.GetService<Pulonia.Services.PuloniaTaskTriggerHost>()?.HandleActivationAsync(commandLineOptions);
                return;
            }
            var mainWindow = Application.Current.MainWindow;
            mainWindow?.Show();
            mainWindow?.Activate();
            if (mainWindow is not null)
            {
                GameSystem.RestoreWindow(new WindowInteropHelper(mainWindow).Handle);
            }

            CoreServices.GetService<HomePageViewModel>()?.HandleActivation(commandLineOptions);
        }));
    }

    private static partial void RequestApplicationShutdown()
    {
        var application = Application.Current;
        if (application is null)
        {
            Environment.Exit(0);
            return;
        }

        _ = application.Dispatcher.BeginInvoke(new Action(application.Shutdown));
    }

    private static partial int? GetRestartFromProcessId() => CommandLineOptions.Instance.RestartFromProcessId;
}
