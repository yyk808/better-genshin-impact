using BetterGenshinImpact;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Host.Headless;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Platform.Linux.X11;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Serilog;
using Serilog.Events;

// 用法：bgi-headless [--display :0] [--assets 主程序输出目录] [--process YuanShen,GenshinImpact]
//                    [--seconds 30] [--interval 50] [--frames 截图目录] [--no-pick] [--no-skip] [--log 日志文件]
//                    [--auto-enter | --no-auto-enter]
//        bgi-headless --check-opencv    # 只检查 OpenCvSharpExtern 原生库的模块（特征匹配、dnn 等）
var options = HeadlessOptions.Parse(args);

var loggerConfiguration = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
if (options.LogFile is { } logFile)
{
    loggerConfiguration.WriteTo.File(logFile,
        outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
}

Log.Logger = loggerConfiguration.CreateLogger();
var log = Log.ForContext("SourceContext", "Headless");

try
{
    if (options.AssetsDirectory is { } assets)
    {
        // 素材、模型、User 目录都按 Global.StartUpPath 解析
        Global.StartUpPath = Path.GetFullPath(assets) + Path.DirectorySeparatorChar;
    }

    log.Information("素材目录：{Path}", Global.StartUpPath);

    if (options.CheckOpenCv)
    {
        // 只检查 OpenCvSharpExtern 的模块是否齐全（大地图特征匹配、dnn 等），不连接 X 服务器
        return OpenCvSelfCheck.Run(log) ? 0 : 3;
    }

    // 1. 平台服务：X11 找窗 / 截图 / XTest 输入
    using var platform = new X11PlatformServices(options.Display);
    var probe = platform.Probe();
    log.Information("平台：{Name}，探测：{Probe}", platform.Name, probe);
    PlatformHost.Use(platform);
    GameSystem.Control = new PlatformGameSystemControl(platform, options.ProcessNames);

    // 2. 配置（内存中，不写回文件）
    var config = new AllConfig();
    config.AutoPickConfig.Enabled = options.Pick;
    config.AutoSkipConfig.Enabled = options.Skip;
    config.TriggerInterval = options.IntervalMs;
    if (options.AutoEnterGame is { } autoEnter)
    {
        // 自动开门（自动进入游戏）：默认沿用配置默认值，可用 --auto-enter / --no-auto-enter 覆盖
        config.GenshinStartConfig.AutoEnterGameEnabled = autoEnter;
    }

    // 推理：只用 CPU 和随程序发布的 onnxruntime 原生库
    config.HardwareAccelerationConfig ??= new();
    var inferenceStartupStatus = InferenceRuntimeService.PrepareStartup(config.HardwareAccelerationConfig);
    if (config.HardwareAccelerationConfig.InferenceDevice != InferenceDeviceType.Cpu)
    {
        config.HardwareAccelerationConfig.SwitchToCpu();
    }
    var configService = new HeadlessConfigService(config);

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
    await using var provider = services.BuildServiceProvider();
    CoreServices.SetProvider(() => provider);
    // 输入通道的“不支持的操作已忽略”告警（例如 X11 没有后台通道）写进日志
    InputChannelBase.LoggerFactory = () => provider.GetRequiredService<ILoggerFactory>().CreateLogger("Input");

    var inference = provider.GetRequiredService<InferenceRuntimeService>();
    await inference.InitializeAsync();
    log.Information("推理：{Engine}，设备 {Device}，运行库目录 {Dir}{Message}", inference.Current.Engine,
        inference.Current.Device.Name, inference.Current.RuntimeDirectory,
        string.IsNullOrEmpty(inference.StartupMessage) ? "" : "，" + inference.StartupMessage);

    // 4. 启动截图器（找窗 → 截图 → 输入 → 触发器调度）
    var dispatcher = provider.GetRequiredService<TaskTriggerDispatcher>();
    // 计时器回调中的异常会被 System.Timers.Timer 吞掉：经观察者写进日志。
    // 同一异常（类型 + 消息 + 抛出位置）第一次记录完整堆栈，之后每 100 次记一条计数
    var tickErrors = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
    dispatcher.TickExceptionObserver = ex =>
    {
        var key = $"{ex.GetType().FullName}|{ex.Message}|{ex.TargetSite}";
        var count = tickErrors.AddOrUpdate(key, 1, (_, c) => c + 1);
        if (count == 1)
        {
            log.Error(ex, "帧调度异常");
        }
        else if (count % 100 == 0)
        {
            log.Warning("帧调度异常已重复 {Count} 次：{Type}: {Message}", count, ex.GetType().Name, ex.Message);
        }
    };
    var frames = 0L;
    var savedFirst = false;
    Mat? lastFrame = null;
    var frameLock = new object();
    dispatcher.FrameCaptured += frame =>
    {
        var n = Interlocked.Increment(ref frames);
        if (options.FramesDirectory is null)
        {
            return;
        }

        lock (frameLock)
        {
            lastFrame?.Dispose();
            lastFrame = frame.Clone();
            if (!savedFirst)
            {
                savedFirst = true;
                Directory.CreateDirectory(options.FramesDirectory);
                Cv2.ImWrite(Path.Combine(options.FramesDirectory, "frame-first.png"), frame);
            }
        }

        if (n % 100 == 0)
        {
            log.Debug("已处理 {Frames} 帧", n);
        }
    };

    var runtimeService = provider.GetRequiredService<GameRuntimeService>();
    var runtime = await runtimeService.StartAsync();
    if (runtime is null)
    {
        log.Error("截图器启动失败");
        return 2;
    }

    log.Information("截图器已启动：窗口 0x{Handle:x}，画面 {Rect}，触发间隔 {Interval} ms，自动拾取={Pick}，自动剧情={Skip}",
        runtime.Window.Handle, runtime.Window.Viewport.ScreenRect, options.IntervalMs, options.Pick, options.Skip);

    // 定时器回调中的异常会被 System.Timers.Timer 吞掉：先在当前线程同步调度一帧，暴露初始化问题
    try
    {
        dispatcher.Tick(null, EventArgs.Empty);
    }
    catch (Exception e)
    {
        log.Error(e, "首帧调度异常");
    }

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(options.Seconds));
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };
    try
    {
        // 每 2 秒输出一次窗口与截图器状态，便于排查“没有处理任何帧”的原因
        while (!cts.IsCancellationRequested)
        {
            await Task.Delay(2000, cts.Token);
            log.Information("状态：前台={Foreground}，最小化={Minimized}，截图中={Capturing}，已处理 {Frames} 帧",
                runtime.Window.IsForeground, runtime.Window.IsMinimized, runtime.Capture.IsCapturing, Interlocked.Read(ref frames));
        }
    }
    catch (OperationCanceledException)
    {
    }

    await runtimeService.StopAsync();
    lock (frameLock)
    {
        if (lastFrame is not null && options.FramesDirectory is not null)
        {
            Cv2.ImWrite(Path.Combine(options.FramesDirectory, "frame-last.png"), lastFrame);
            lastFrame.Dispose();
        }
    }

    log.Information("截图器已停止，共处理 {Frames} 帧（{Seconds} 秒），帧调度异常 {Errors} 次",
        Interlocked.Read(ref frames), options.Seconds, tickErrors.Values.Sum());
    return 0;
}
catch (Exception e)
{
    log.Fatal(e, "无界面宿主异常退出");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

internal sealed record HeadlessOptions(
    string? Display,
    string? AssetsDirectory,
    IReadOnlyList<string> ProcessNames,
    int Seconds,
    int IntervalMs,
    string? FramesDirectory,
    bool Pick,
    bool Skip,
    string? LogFile,
    bool? AutoEnterGame,
    bool CheckOpenCv)
{
    public static HeadlessOptions Parse(string[] args)
    {
        string? display = null, assets = null, frames = null, logFile = null;
        IReadOnlyList<string> names = ["YuanShen", "GenshinImpact"];
        int seconds = 30, interval = 50;
        bool pick = true, skip = true;
        bool? autoEnter = null;
        var checkOpenCv = false;
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} 缺少参数值");
            switch (args[i])
            {
                case "--display": display = Next(); break;
                case "--assets": assets = Next(); break;
                case "--process": names = Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
                case "--seconds": seconds = int.Parse(Next()); break;
                case "--interval": interval = int.Parse(Next()); break;
                case "--frames": frames = Next(); break;
                case "--no-pick": pick = false; break;
                case "--no-skip": skip = false; break;
                case "--log": logFile = Next(); break;
                case "--auto-enter": autoEnter = true; break;
                case "--no-auto-enter": autoEnter = false; break;
                case "--check-opencv": checkOpenCv = true; break;
                default: throw new ArgumentException($"未知参数 {args[i]}");
            }
        }

        return new HeadlessOptions(display, assets, names, seconds, interval, frames, pick, skip, logFile, autoEnter, checkOpenCv);
    }
}
