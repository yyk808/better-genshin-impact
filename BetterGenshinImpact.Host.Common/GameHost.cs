using System.Collections.Concurrent;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Platform.Linux.X11;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using ILogger = Serilog.ILogger;

namespace BetterGenshinImpact.Host.Common;

/// <summary>
/// 宿主共用的启动引导：平台服务（X11 找窗 / 截图 / XTest）→ 配置（内存）→ 依赖注入 →
/// CoreServices / PlatformHost / GameSystem / 输入告警日志 → CPU 推理 → 截图器启动停止。
/// 无界面宿主与 Avalonia 宿主都用它；UI 线程、消息框、剪贴板（UiHost）由宿主在创建前自行设置。
/// </summary>
public sealed class GameHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly X11PlatformServices _platform;
    private readonly ILogger _log;
    private readonly ConcurrentDictionary<string, int> _tickErrors = new();
    private long _frames;

    private GameHost(GameHostOptions options, X11PlatformServices platform, PlatformProbeResult probe, AllConfig config,
        ServiceProvider provider, ILogger log)
    {
        Options = options;
        _platform = platform;
        Probe = probe;
        Config = config;
        _provider = provider;
        _log = log;
        Dispatcher = provider.GetRequiredService<TaskTriggerDispatcher>();
        RuntimeService = provider.GetRequiredService<GameRuntimeService>();
        Dispatcher.TickExceptionObserver = OnTickException;
        Dispatcher.FrameCaptured += _ => Interlocked.Increment(ref _frames);
    }

    public GameHostOptions Options { get; }

    public IPlatformServices Platform => _platform;

    /// <summary>平台环境探测结果（显示服务器、扩展、会话类型等）</summary>
    public PlatformProbeResult Probe { get; }

    public AllConfig Config { get; }

    public IServiceProvider Services => _provider;

    public TaskTriggerDispatcher Dispatcher { get; }

    public GameRuntimeService RuntimeService { get; }

    public InferenceRuntimeService Inference => _provider.GetRequiredService<InferenceRuntimeService>();

    /// <summary>截图器启动以来处理的帧数（跨多次启动累计）</summary>
    public long FramesProcessed => Interlocked.Read(ref _frames);

    /// <summary>帧调度异常次数</summary>
    public int TickErrorCount => _tickErrors.Values.Sum();

    public bool IsRunning => RuntimeService.IsRunning;

    /// <summary>
    /// 创建宿主：初始化平台服务与依赖注入（不启动截图器）。
    /// <paramref name="configureServices"/> 可追加宿主自己的服务
    /// </summary>
    public static GameHost Create(GameHostOptions options, ILogger log, Action<IServiceCollection>? configureServices = null)
    {
        if (options.AssetsDirectory is { } assets)
        {
            // 素材、模型、User 目录都按 Global.StartUpPath 解析
            Global.StartUpPath = Path.GetFullPath(assets) + Path.DirectorySeparatorChar;
        }

        log.Information("素材目录：{Path}", Global.StartUpPath);

        // 1. 平台服务：X11 找窗 / 截图 / XTest 输入
        var platform = new X11PlatformServices(options.Display);
        try
        {
            var probe = platform.Probe();
            log.Information("平台：{Name}（{Kind}），探测：{Probe}", platform.Name, probe.Kind,
                string.Join("，", probe.Items.Select(kv => $"{kv.Key}={kv.Value}")));
            PlatformHost.Use(platform);
            GameSystem.Control = new PlatformGameSystemControl(platform, options.ProcessNames);

            // 2. 配置（内存中，不写回文件）
            var config = new AllConfig();
            config.AutoPickConfig.Enabled = options.Pick;
            config.AutoSkipConfig.Enabled = options.Skip;
            config.TriggerInterval = options.IntervalMs;
            if (options.AutoEnterGame is { } autoEnter)
            {
                // 自动开门（自动进入游戏）：默认沿用配置默认值
                config.GenshinStartConfig.AutoEnterGameEnabled = autoEnter;
            }

            // 推理：只用 CPU 和随程序发布的 onnxruntime 原生库
            config.HardwareAccelerationConfig ??= new();
            var inferenceStartupStatus = InferenceRuntimeService.PrepareStartup(config.HardwareAccelerationConfig);
            if (config.HardwareAccelerationConfig.InferenceDevice != InferenceDeviceType.Cpu)
            {
                config.HardwareAccelerationConfig.SwitchToCpu();
            }

            var configService = new MemoryConfigService(config);

            // 3. 依赖注入
            var services = new ServiceCollection();
            services.AddLogging(b => b.AddSerilog(dispose: false));
            services.AddSingleton<IPlatformServices>(platform);
            services.AddSingleton<IConfigService>(configService);
            services.AddSingleton(config);
            services.AddSingleton<IOverlayMetricsSink>(NullOverlayMetricsSink.Instance);
            services.AddSingleton<IGameWindowStateSink>(NullGameWindowStateSink.Instance);
            services.AddSingleton<IMaskWindowDrawingBoard>(NullMaskWindowDrawingBoard.Instance);
            services.AddSingleton<IMaskWindowMapState, MaskWindowMapState>();
            services.AddSingleton<TaskStopService>();
            services.AddSingleton<TaskTriggerDispatcher>();
            services.AddSingleton<ITriggerDispatcher>(sp => sp.GetRequiredService<TaskTriggerDispatcher>());
            services.AddSingleton<IGameRuntimeProvider>(sp => ActivatorUtilities.CreateInstance<PlatformRuntimeProvider>(sp,
                (Func<GameWindowSearch>)(() => new GameWindowSearch(options.ProcessNames))));
            services.AddSingleton(sp => ActivatorUtilities.CreateInstance<GameRuntimeService>(sp, sp.GetRequiredService<IGameRuntimeProvider>()));
            services.AddSingleton<IGameRuntimeHost>(sp => sp.GetRequiredService<GameRuntimeService>());
            // OCR / ONNX 推理（CPU）
            services.AddSingleton(inferenceStartupStatus);
            services.AddSingleton<IOnnxRuntimeSelection>(new CpuOnnxRuntimeSelection(AppContext.BaseDirectory));
            services.AddSingleton<IInferenceDeviceDiscovery, CpuInferenceDeviceDiscovery>();
            services.AddSingleton<IInferenceBenchmarkLeaseProvider>(sp => new InferenceBenchmarkCoordinator(
                TaskControl.TaskSemaphore, () => sp.GetRequiredService<TaskTriggerDispatcher>().PauseForInference()));
            services.AddSingleton<InferenceModelDiagnostics>();
            services.AddSingleton<IInferenceModelDiagnostics>(sp => sp.GetRequiredService<InferenceModelDiagnostics>());
            services.AddSingleton<InferenceRuntimeService>();
            services.AddSingleton<BgiOnnxFactory>();
            services.AddSingleton<OcrFactory>();
            configureServices?.Invoke(services);
            var provider = services.BuildServiceProvider();
            CoreServices.SetProvider(() => provider);
            // 输入通道的“不支持的操作已忽略 / 已跳过”告警写进日志。
            // 先取出记录器：停止截图器时仍在执行的一帧可能在服务容器释放后才发出告警
            var inputLogger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Input");
            InputChannelBase.LoggerFactory = () => inputLogger;

            return new GameHost(options, platform, probe, config, provider, log);
        }
        catch
        {
            platform.Dispose();
            throw;
        }
    }

    /// <summary>初始化 CPU 推理（OCR 等），首次启动截图器前调用</summary>
    public async Task InitializeInferenceAsync()
    {
        var inference = Inference;
        await inference.InitializeAsync();
        _log.Information("推理：{Engine}，设备 {Device}，运行库目录 {Dir}{Message}", inference.Current.Engine,
            inference.Current.Device.Name, inference.Current.RuntimeDirectory,
            string.IsNullOrEmpty(inference.StartupMessage) ? "" : "，" + inference.StartupMessage);
    }

    /// <summary>启动截图器（找窗 → 截图 → 输入 → 触发器调度）；找不到游戏窗口等失败时返回 null</summary>
    public async Task<GameRuntime?> StartAsync(CancellationToken ct = default)
    {
        var runtime = await RuntimeService.StartAsync(ct);
        if (runtime is null)
        {
            _log.Error("截图器启动失败");
            return null;
        }

        _log.Information("截图器已启动：窗口 0x{Handle:x}，画面 {Rect}，触发间隔 {Interval} ms，自动拾取={Pick}，自动剧情={Skip}",
            runtime.Window.Handle, runtime.Window.Viewport.ScreenRect, Config.TriggerInterval,
            Config.AutoPickConfig.Enabled, Config.AutoSkipConfig.Enabled);
        return runtime;
    }

    /// <summary>
    /// 在当前线程同步调度一帧：定时器回调中的异常会被 System.Timers.Timer 吞掉，首帧同步执行可以暴露初始化问题
    /// </summary>
    public void TickOnce()
    {
        try
        {
            Dispatcher.Tick(null, EventArgs.Empty);
        }
        catch (Exception e)
        {
            _log.Error(e, "首帧调度异常");
        }
    }

    public async Task StopAsync()
    {
        await RuntimeService.StopAsync();
        _log.Information("截图器已停止，累计处理 {Frames} 帧，帧调度异常 {Errors} 次", FramesProcessed, TickErrorCount);
    }

    /// <summary>
    /// 计时器回调中的异常会被 System.Timers.Timer 吞掉：经观察者写进日志。
    /// 同一异常（类型 + 消息 + 抛出位置）第一次记录完整堆栈，之后每 100 次记一条计数
    /// </summary>
    private void OnTickException(Exception ex)
    {
        var key = $"{ex.GetType().FullName}|{ex.Message}|{ex.TargetSite}";
        var count = _tickErrors.AddOrUpdate(key, 1, (_, c) => c + 1);
        if (count == 1)
        {
            _log.Error(ex, "帧调度异常");
        }
        else if (count % 100 == 0)
        {
            _log.Warning("帧调度异常已重复 {Count} 次：{Type}: {Message}", count, ex.GetType().Name, ex.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (RuntimeService.IsRunning)
        {
            await RuntimeService.StopAsync();
        }

        await _provider.DisposeAsync();
        _platform.Dispose();
    }
}
