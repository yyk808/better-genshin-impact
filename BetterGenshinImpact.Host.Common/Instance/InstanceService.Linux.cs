using BetterGenshinImpact.GameTask.SoloTask.Execution;
using BetterGenshinImpact.Pulonia.Remote;
using BetterGenshinImpact.Service.Instance.MessageHandlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.Instance;

/// <summary>
/// 实例服务的 Linux 部分（Avalonia / 无界面宿主共用 WPF 版的 InstanceService.cs）：
/// 没有桌面分身的相对鼠标转发（每个实例有自己的显示与输入）；激活主窗口与关闭应用交给宿主注册的回调。
/// </summary>
public sealed partial class InstanceService
{
    /// <summary>宿主处理转发来的启动参数（激活主窗口等），在任意线程调用</summary>
    public static Action<string[]>? ActivationHandler { get; set; }

    /// <summary>宿主关闭应用（根实例要求子实例关闭时），在任意线程调用；未设置时直接退出进程</summary>
    public static Action? ShutdownHandler { get; set; }

    /// <summary>启动参数里的 --restart-from 进程号（宿主解析命令行后设置）</summary>
    public static int? RestartFromProcessId { get; set; }

    public InstanceService(
        InstanceBootstrap bootstrap,
        IServiceProvider serviceProvider,
        SoloTaskExecutionService soloTaskExecutionService,
        ILogger<InstanceService> logger)
    {
        _bootstrap = bootstrap;
        _logger = logger;
        _soloTaskExecutionService = soloTaskExecutionService;
        _soloTaskExecutionService.StatusChanged += OnLocalSoloTaskStatusChanged;
        _relativeMouseMessageHandler = new RelativeMouseMessageHandler();
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
        var handler = ActivationHandler;
        if (handler is null)
        {
            _logger.LogInformation("收到另一次启动转发来的参数，宿主未处理：{Arguments}", string.Join(" ", args));
            return;
        }
        handler(args);
    }

    private static partial void RequestApplicationShutdown()
    {
        if (ShutdownHandler is { } handler) handler();
        else Environment.Exit(0);
    }

    private static partial int? GetRestartFromProcessId() => RestartFromProcessId;
}
