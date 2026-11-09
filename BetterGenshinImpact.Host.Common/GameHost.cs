using System.Threading;
﻿using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Host.Common.Notification;
using BetterGenshinImpact.Infrastructure.Network;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notifier;
using BetterGenshinImpact.GameTask.AutoMusicGame;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.GameTask.SoloTask.Execution;
using System.Collections.Concurrent;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Host.Common.Config;
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
    private readonly IPlatformServices _platform;
    private readonly ILogger _log;
    private readonly ConcurrentDictionary<string, int> _tickErrors = new();
    private long _frames;

    private readonly FileConfigService? _fileConfig;
    private readonly LinuxHostConfigStore? _linuxConfigStore;

    private GameHost(GameHostOptions options, IPlatformServices platform, PlatformProbeResult probe, AllConfig config,
        ServiceProvider provider, ILogger log, FileConfigService? fileConfig, LinuxHostConfigStore? linuxConfigStore,
        LinuxHostConfig linuxConfig, ManualWindowSelection manualWindow)
    {
        Options = options;
        _fileConfig = fileConfig;
        _linuxConfigStore = linuxConfigStore;
        LinuxConfig = linuxConfig;
        ManualWindow = manualWindow;
        _platform = platform;
        Probe = probe;
        Config = config;
        _provider = provider;
        _log = log;
        Dispatcher = provider.GetRequiredService<TaskTriggerDispatcher>();
        if (platform is BetterGenshinImpact.Platform.Linux.Wayland.WaylandPlatformServices)
        {
            BetterGenshinImpact.Platform.Linux.Wayland.PortalScreenCastCapture.Diagnostic += OnPortalDiagnostic;
        }
        RuntimeService = provider.GetRequiredService<GameRuntimeService>();
        // 与 WPF 相同在启动时创建，订阅截图器与开关状态
        QuestVoice = provider.GetRequiredService<QuestVoiceService>();
        Dispatcher.TickExceptionObserver = OnTickException;
        Dispatcher.FrameCaptured += _ => Interlocked.Increment(ref _frames);
    }

    public GameHostOptions Options { get; }

    public IPlatformServices Platform => _platform;

    /// <summary>平台环境探测结果（显示服务器、扩展、会话类型等）</summary>
    public PlatformProbeResult Probe { get; }

    public AllConfig Config { get; }

    /// <summary>Linux 宿主设置（截图方式、启动游戏命令）</summary>
    public LinuxHostConfig LinuxConfig { get; }

    /// <summary>手动选择的游戏窗口（找不到原神窗口时使用，只在本次运行有效）</summary>
    public ManualWindowSelection ManualWindow { get; }

    /// <summary>配置文件路径（不保存配置时为 null）</summary>
    public string? ConfigFilePath => _fileConfig?.FilePath;

    /// <summary>Linux 宿主设置文件路径（不保存配置时为 null）</summary>
    public string? LinuxConfigFilePath => _linuxConfigStore?.Store.FilePath;

    /// <summary>
    /// 实际使用的截图方式：启用遮罩时，屏幕区域截图会截到遮罩上画的内容，默认的 MIT-SHM 屏幕区域改用 Composite 窗口图像
    /// （Composite 不可用时截图器自己回退）
    /// </summary>
    public static string EffectiveCaptureMode(LinuxHostConfig config) =>
        config.MaskWindowEnabled && string.Equals(config.CaptureMode, "ShmRoot", StringComparison.OrdinalIgnoreCase)
            ? "CompositeWindow"
            : config.CaptureMode;

    /// <summary>
    /// 按平台取实际截图方式：Wayland 会话下根窗口没有画面，ShmRoot / GetImageRoot 改为 GetImageWindow，遮罩是 layer-shell 覆盖层、
    /// 不会被 X 窗口截图截到，所以不需要改用 Composite；没有 X 显示（xwayland-satellite）时只能用 Portal。X11 下同 <see cref="EffectiveCaptureMode(LinuxHostConfig)"/>
    /// </summary>
    public static string EffectiveCaptureMode(LinuxHostConfig config, IPlatformServices? platform) => platform switch
    {
        BetterGenshinImpact.Platform.Linux.Wayland.WaylandPlatformServices { X11: null } => BetterGenshinImpact.Platform.Linux.Wayland.PortalScreenCastCapture.MethodName,
        BetterGenshinImpact.Platform.Linux.Wayland.WaylandPlatformServices => BetterGenshinImpact.Platform.Linux.Wayland.WaylandPlatformServices.MapCaptureMode(config.CaptureMode),
        _ => EffectiveCaptureMode(config),
    };

    /// <summary>
    /// 临时抓一帧（音游引导识别键位等）用的截图方式：Portal 每次都要新建会话、启动 PipeWire 读取进程，太慢，
    /// X 窗口（Wine 游戏）改用 GetImageWindow；原生 Wayland 窗口只能用 Portal
    /// </summary>
    public static string TemporaryGrabMode(IPlatformServices platform, nint handle, string mode) =>
        platform is BetterGenshinImpact.Platform.Linux.Wayland.WaylandPlatformServices { X11: not null }
        && string.Equals(mode, BetterGenshinImpact.Platform.Linux.Wayland.PortalScreenCastCapture.MethodName, StringComparison.OrdinalIgnoreCase)
        && ((long)handle & BetterGenshinImpact.Platform.Linux.Wayland.NiriWindow.HandleTag) == 0
            ? "GetImageWindow"
            : mode;

    /// <summary>Wayland（niri）会话下可选的截图方式（Wine 游戏经 xwayland-satellite 是 X 窗口）</summary>
    public static IReadOnlyList<CaptureMethodInfo> WaylandCaptureMethods { get; } =
    [
        new("GetImageWindow", "XGetImage 窗口（推荐）", "经 xwayland-satellite 直接读取 Wine 游戏的 X 窗口内容，不受遮罩和其它窗口遮挡影响，不需要授权"),
        new("CompositeWindow", "Composite 窗口图像", "读取游戏 X 窗口的离屏图像（Composite 扩展 + MIT-SHM），比 XGetImage 快；Xwayland 不支持时自动回退"),
        new(BetterGenshinImpact.Platform.Linux.Wayland.PortalScreenCastCapture.MethodName, "ScreenCast 门户（PipeWire）",
            "经 xdg-desktop-portal 截图：第一次弹出系统的选择对话框（选游戏窗口；选整个显示器时按 niri 报告的窗口位置裁剪），之后自动恢复；原生 Wayland 窗口只能用这个"),
    ];

    /// <summary>没有 X 显示（没运行 xwayland-satellite）的 Wayland 会话只能用 Portal</summary>
    private static IReadOnlyList<CaptureMethodInfo> PortalOnlyCaptureMethods { get; } = [WaylandCaptureMethods[2]];

    /// <summary>当前平台下设置页可选的截图方式（X11 下为 <see cref="CaptureMethods"/>，与以前相同）</summary>
    public static IReadOnlyList<CaptureMethodInfo> CaptureMethodsFor(IPlatformServices? platform) => platform switch
    {
        BetterGenshinImpact.Platform.Linux.Wayland.WaylandPlatformServices { X11: null } => PortalOnlyCaptureMethods,
        BetterGenshinImpact.Platform.Linux.Wayland.WaylandPlatformServices => WaylandCaptureMethods,
        _ => CaptureMethods,
    };

    /// <summary>设置页里显示为选中的截图方式：配置值在当前平台的实际方式（Wayland 下默认的 ShmRoot 显示为 GetImageWindow），不在列表中时取第一项</summary>
    public static CaptureMethodInfo SelectedCaptureMethodFor(IPlatformServices? platform, LinuxHostConfig config)
    {
        var list = CaptureMethodsFor(platform);
        var id = platform is BetterGenshinImpact.Platform.Linux.Wayland.WaylandPlatformServices ? EffectiveCaptureMode(config, platform) : config.CaptureMode;
        return list.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)) ?? list[0];
    }

    /// <summary>当前平台下可选的截图方式</summary>
    public IReadOnlyList<CaptureMethodInfo> AvailableCaptureMethods => CaptureMethodsFor(_platform);

    /// <summary>X11 可用的截图方式</summary>
    public static IReadOnlyList<CaptureMethodInfo> CaptureMethods { get; } =
    [
        new("ShmRoot", "MIT-SHM 屏幕区域（推荐）", "XShmGetImage 截取游戏画面所在的屏幕区域，最快；游戏窗口须可见、不被遮挡（类似 Windows 的 BitBlt）"),
        new("GetImageRoot", "XGetImage 屏幕区域", "不使用共享内存，较慢；远程 X 或 MIT-SHM 不可用时使用"),
        new("GetImageWindow", "XGetImage 窗口", "直接读取窗口内容，较慢；没有合成器时被遮挡部分的内容未定义"),
        new("CompositeWindow", "Composite 窗口图像", "读取游戏窗口的离屏图像（Composite 扩展 + MIT-SHM），被遮挡或被遮罩覆盖时仍是游戏本身的画面；启用遮罩时代替 MIT-SHM 屏幕区域"),
    ];
    public IServiceProvider Services => _provider;

    public TaskTriggerDispatcher Dispatcher { get; }

    /// <summary>遮罩绘制内容（识别框、文字）的快照来源</summary>
    public IMaskWindowSnapshotSource<MaskWindowDrawingSnapshot> MaskDrawing => _provider.GetRequiredService<IMaskWindowSnapshotSource<MaskWindowDrawingSnapshot>>();

    public GameRuntimeService RuntimeService { get; }

    /// <summary>剧情语音（自动配音）服务</summary>
    public QuestVoiceService QuestVoice { get; }

    /// <summary>云原神页面宿主（图形界面宿主注册；无界面宿主为 null）</summary>
    public ICloudGamePageHost? CloudGamePage => _provider.GetService<ICloudGamePageHost>();

    /// <summary>当前设置的云原神页面地址（为空时用默认地址）</summary>
    public string CloudGameUrl => CloudUrl(LinuxConfig);

    private static string CloudUrl(LinuxHostConfig c) =>
        string.IsNullOrWhiteSpace(c.CloudGameUrl) ? LinuxHostConfig.DefaultCloudGameUrl : c.CloudGameUrl.Trim();

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

        // 任务的调试截图等直接写入 log 子目录（Windows 版由日志文件创建该目录；这里日志可能写在别处），启动时先建好
        try
        {
            Directory.CreateDirectory(Global.Absolute("log"));
        }
        catch (Exception e)
        {
            log.Warning("创建 log 目录失败：{Message}", e.Message);
        }

        try
        {
            OpenCvCapabilities.LogStartupStatus(log);
        }
        catch (Exception e)
        {
            log.Warning("OpenCV 特征匹配探测失败：{Message}", e.Message);
        }

        // 1. 平台服务：X11 会话为 X11 找窗 / 截图 / XTest 输入（与以前相同）；Wayland 会话（WAYLAND_DISPLAY / XDG_SESSION_TYPE，
        //    或 BGI_LINUX_BACKEND=wayland）为 niri IPC + evdev 热键 + uinput / XTest 输入 + X 窗口或 ScreenCast portal 截图
        var platform = BetterGenshinImpact.Platform.Linux.Wayland.LinuxSession.Create(options.Display);
        try
        {
            var probe = platform.Probe();
            log.Information("平台：{Name}（{Kind}），探测：{Probe}", platform.Name, probe.Kind,
                string.Join("，", probe.Items.Select(kv => $"{kv.Key}={kv.Value}")));
            PlatformHost.Use(platform);
            // 自动音游的窗口直接取色（Windows 为 GDI GetPixel）
            WindowPixelSampler.Current = new X11WindowPixelSampler(options.Display);
            // 任务暂停状态（一条龙子任务、寻路等查询；与 Windows 主程序相同使用 RunnerContext）
            RunnerSuspend.Provider = static () => RunnerContext.Instance;

            // 2. 配置：User/config.json（与 Windows 主程序格式相同，防抖保存）或内存中的默认配置
            FileConfigService? fileConfig = null;
            LinuxHostConfigStore? linuxConfigStore = null;
            IConfigService configService;
            AllConfig config;
            InferenceStartupStatus inferenceStartupStatus = null!;

            void ApplyOverrides(AllConfig c)
            {
                // 命令行覆盖与强制 CPU 推理：只在本次运行有效，保存时写回文件原值（见 RuntimeOverrides）
                if (options.Pick is { } pick) c.AutoPickConfig.Enabled = pick;
                if (options.Skip is { } skip) c.AutoSkipConfig.Enabled = skip;
                if (options.IntervalMs is { } interval) c.TriggerInterval = interval;
                if (options.AutoEnterGame is { } autoEnter) c.GenshinStartConfig.AutoEnterGameEnabled = autoEnter;

                // 推理：CPU（随程序发布的 onnxruntime）或 ROCm（系统带 MIGraphX 的 onnxruntime，见 LinuxOnnxRuntimeSelection）；
                // Windows 专有引擎（DirectML / OpenVINO / CUDA）在 Linux 上不可选，启动准备时回退 CPU
                c.HardwareAccelerationConfig ??= new();
                InferenceDeviceTypeExtensions.Available = [InferenceDeviceType.Cpu, InferenceDeviceType.ROCm];
                if (!InferenceDeviceTypeExtensions.Available.Contains(c.HardwareAccelerationConfig.InferenceDevice))
                {
                    c.HardwareAccelerationConfig.SwitchToCpu();
                }

                inferenceStartupStatus = InferenceRuntimeService.PrepareStartup(c.HardwareAccelerationConfig);
            }

            LinuxHostConfig linuxConfig;
            if (options.PersistConfig)
            {
                var configPath = Path.GetFullPath(options.ConfigFile ?? Global.Absolute(FileConfigService.DefaultRelativePath));
                fileConfig = new FileConfigService(configPath, log);
                config = fileConfig.Load(ApplyOverrides);
                configService = fileConfig;
                linuxConfigStore = new LinuxHostConfigStore(Path.Combine(Path.GetDirectoryName(configPath)!, "linux-host.json"), log);
                linuxConfig = linuxConfigStore.Config;
                log.Information("配置文件：{Path}（{State}），Linux 宿主设置：{Linux}", configPath,
                    fileConfig.CreatedDefault ? "新建" : "已读取", linuxConfigStore.Store.FilePath);
            }
            else
            {
                config = new AllConfig();
                ApplyOverrides(config);
                configService = new MemoryConfigService(config);
                linuxConfig = new LinuxHostConfig();
                log.Information("配置：只在内存中（不读写配置文件）");
            }

            // Linux 宿主设置的命令行覆盖：只在本次运行有效，不写回 linux-host.json
            void ApplyLinuxOverrides(LinuxHostConfig c)
            {
                if (options.CaptureMode is { } captureMode) c.CaptureMode = captureMode;
                if (options.MaskWindow is { } mask) c.MaskWindowEnabled = mask;
                if (options.MaskDrawAllRecognition is { } drawAll) c.MaskDrawAllRecognition = drawAll;
                if (!string.IsNullOrWhiteSpace(options.CloudGameUrl))
                {
                    c.CloudGameEnabled = true;
                    c.CloudGameUrl = options.CloudGameUrl;
                }
            }

            if (linuxConfigStore is not null)
            {
                linuxConfigStore.ApplyRuntimeOverride(ApplyLinuxOverrides);
            }
            else
            {
                ApplyLinuxOverrides(linuxConfig);
            }

            // Wine 游戏的后台输入是否先发 WM_ACTIVATE（Linux 宿主设置，默认不发；每次操作时读取）
            if (BetterGenshinImpact.Platform.Linux.Wayland.LinuxSession.X11Of(platform) is { } x11Platform)
            {
                x11Platform.WineBackgroundActivate = () => linuxConfig.WineBackgroundActivate;
            }

            var manualWindow = new ManualWindowSelection(platform);
            GameSystem.Control = new PlatformGameSystemControl(platform, options.ProcessNames, () => linuxConfig, log);
            // 临时抓帧（音游引导识别键位等）经平台截图器
            GameFrameGrabber.Current = (handle, c) => GameFrameGrabber.GrabWith(platform, handle, c, TemporaryGrabMode(platform, handle, EffectiveCaptureMode(linuxConfig, platform)));
            // 音频输出：PulseAudio / PipeWire 可用时播放剧情语音，否则静音（流程照常推进），结果显示在环境页
            var audio = BetterGenshinImpact.Platform.Linux.Audio.LinuxAudioOutput.Install();
            log.Information("音频输出：{Status}", audio.Status);
            // 系统语音：本机装有 espeak-ng 时注册为剧情语音引擎，未安装时不出现在引擎列表
            if (BetterGenshinImpact.Platform.Linux.Tts.EspeakNgTtsProvider.TryRegister())
            {
                // 引擎独立配置的缺省条目（Windows 配置不含该引擎，只在 Linux 宿主补齐）
                config.QuestVoiceConfig.ExtraProviders.TryAdd(BetterGenshinImpact.Platform.Linux.Tts.EspeakNgTtsProvider.ProviderId,
                    BetterGenshinImpact.Platform.Linux.Tts.EspeakNgTtsProvider.DefaultConfig());
                log.Information("系统语音（espeak-ng）：已注册");
            }
            else
            {
                log.Information("系统语音（espeak-ng）：未安装，不提供该剧情语音引擎");
            }

            // 3. 依赖注入
            var services = new ServiceCollection();
            // HttpClient 的请求日志包含完整地址（Webhook 地址中可能带令牌），只记录警告以上
            services.AddLogging(b => b.AddSerilog(dispose: false).AddFilter("System.Net.Http.HttpClient", Microsoft.Extensions.Logging.LogLevel.Warning));
            // 任务参数基类经 IStringLocalizer<T> 读取多语言资源（与 WPF 主程序相同）
            services.AddLocalization();
            services.AddSingleton<IPlatformServices>(platform);
            services.AddSingleton<IConfigService>(configService);
            services.AddSingleton(config);
            services.AddSingleton<IOverlayMetricsSink>(NullOverlayMetricsSink.Instance);
            services.AddSingleton<IGameWindowStateSink>(NullGameWindowStateSink.Instance);
            // 遮罩绘制：识别框、文字等写入绘制板，Avalonia 遮罩窗口读取快照绘制（无界面宿主没有遮罩，只是不读取）
            services.AddSingleton<MaskWindowDrawingBoard>();
            services.AddSingleton<IMaskWindowDrawingBoard>(sp => sp.GetRequiredService<MaskWindowDrawingBoard>());
            services.AddSingleton<IMaskWindowSnapshotSource<MaskWindowDrawingSnapshot>>(sp => sp.GetRequiredService<MaskWindowDrawingBoard>());
            services.AddSingleton<IMaskWindowMapState, MaskWindowMapState>();
            services.AddSingleton<TaskStopService>();
            // 键鼠录制：相对鼠标移动用 XInput2 原始移动（DirectInput / RawInput 两种方式都映射到它），按键与按钮来自全局键鼠监听
            services.AddSingleton<IRelativeMouseInputMonitorFactory>(_ => new X11RelativeMouseInputMonitorFactory(BetterGenshinImpact.Platform.Linux.Wayland.LinuxSession.CreateRelativeMouseMonitor(platform, options.Display)));
            services.AddSingleton<KeyMouseRecordService>();
            services.AddSingleton<KeyMouseRecordController>();
            // 独立任务：注册表只扫描 Core（Linux 宿主没有 WPF 主程序集，一条龙另行迁移），执行走 Core 的 TaskRunner
            services.AddSingleton(new SoloTaskRegistry([typeof(CoreServices).Assembly]));
            // 可插拔能力（OCR 模型、传送等）：只扫描 Core 中的内置实现；Linux 宿主暂不加载外部插件
            config.PluginConfig ??= new();
            var pluggables = new BetterGenshinImpact.Core.Plugins.Pluggables.PluggableRegistry([typeof(CoreServices).Assembly], config.PluginConfig);
            services.AddSingleton(pluggables);
            pluggables.RegisterProxies(services);
            services.AddSingleton<SoloTaskExecutionService>();
            // 配置组执行（一条龙中的配置组任务、脚本）
            services.AddSingleton<IScriptService, ScriptService>();
            services.AddSingleton<ISoloTaskNotificationSuppressor>(NoopSoloTaskNotificationSuppressor.Instance);
            services.AddSingleton<TaskTriggerDispatcher>();
            services.AddSingleton<ITriggerDispatcher>(sp => sp.GetRequiredService<TaskTriggerDispatcher>());
            services.AddSingleton(linuxConfig);
            services.AddSingleton(manualWindow);
            services.AddSingleton<IGameRuntimeProvider>(sp =>
            {
                var local = ActivatorUtilities.CreateInstance<PlatformRuntimeProvider>(sp,
                    (Func<GameWindowSearch>)(() => new GameWindowSearch(options.ProcessNames)),
                    (Func<string?>)(() => EffectiveCaptureMode(linuxConfig, platform)),
                    (Func<IPlatformWindow?>)manualWindow.Open);
                // 云原神：页面宿主由图形界面宿主注册（无界面宿主没有），按设置在启动截图器时选择
                var pageHost = sp.GetService<ICloudGamePageHost>();
                var cloud = pageHost is null
                    ? null
                    : ActivatorUtilities.CreateInstance<CloudWebRuntimeProvider>(sp, pageHost,
                        (Func<string>)(() => CloudUrl(linuxConfig)),
                        (Func<string?>)(() => EffectiveCaptureMode(linuxConfig, platform)));
                return new SwitchingRuntimeProvider(local, cloud, () => linuxConfig.CloudGameEnabled);
            });
            services.AddSingleton(sp => ActivatorUtilities.CreateInstance<GameRuntimeService>(sp, sp.GetRequiredService<IGameRuntimeProvider>()));
            services.AddSingleton<IGameRuntimeHost>(sp => sp.GetRequiredService<GameRuntimeService>());
            // 自动剧情语音检测（听完语音再选 / Silero VAD 诊断）：按进程捕获音频为 PulseAudio / PipeWire 的单流录音（Windows 为 WASAPI 进程环回）
            BetterGenshinImpact.GameTask.AutoSkip.Audio.ProcessAudioCapture.Factory =
                static pid => new BetterGenshinImpact.Platform.Linux.Audio.PulseProcessAudioCapture(pid);
            services.AddSingleton<BetterGenshinImpact.GameTask.AutoSkip.Audio.DialogueOptionVoiceDiagnosticState>();
            services.AddSingleton<BetterGenshinImpact.GameTask.AutoSkip.Audio.DialogueOptionVoiceDiagnosticService>();
            // OCR / ONNX 推理（CPU 或 ROCm；选运行库必须在任何 ONNX 原生调用之前）
            services.AddSingleton(inferenceStartupStatus);
            services.AddSingleton<IOnnxRuntimeSelection>(LinuxOnnxRuntimeSelection.Select(config.HardwareAccelerationConfig, AppContext.BaseDirectory, log));
            services.AddSingleton<IInferenceDeviceDiscovery, LinuxInferenceDeviceDiscovery>();
            services.AddSingleton<IInferenceBenchmarkLeaseProvider>(sp => new InferenceBenchmarkCoordinator(
                TaskControl.TaskSemaphore, () => sp.GetRequiredService<TaskTriggerDispatcher>().PauseForInference()));
            services.AddSingleton<InferenceModelDiagnostics>();
            services.AddSingleton<IInferenceModelDiagnostics>(sp => sp.GetRequiredService<InferenceModelDiagnostics>());
            services.AddSingleton<InferenceRuntimeService>();
            services.AddSingleton<BgiOnnxFactory>();
            // 绑定当前推理运行时的会话工厂，供可插拔实现（如 PP-OCRv6）在构造函数中注入（与 WPF App 相同）
            services.AddTransient(sp => sp.GetRequiredService<BgiOnnxFactory>().ForCurrentRuntime());
            services.AddSingleton<OcrFactory>();
            // 剧情语音（自动配音）：会话与服务都在 Core，与 WPF 注册方式相同；开关打开且截图器运行时自动启停
            services.AddSingleton(sp => new QuestVoiceSessionFactory(config.QuestVoiceConfig, Global.StartUpPath,
                sp.GetRequiredService<OcrFactory>(), sp.GetRequiredService<InferenceRuntimeService>()));
            services.AddSingleton<IQuestVoiceSessionFactory>(sp => sp.GetRequiredService<QuestVoiceSessionFactory>());
            services.AddSingleton<QuestVoiceSessionController>();
            services.AddSingleton<QuestVoiceService>();
            // 自动配音设置窗口的模型（Core）：角色目录存储与设置模型；界面能力 IQuestVoiceSettingsUi 由宿主经 configureServices 注册
            services.AddSingleton<BetterGenshinImpact.GameTask.QuestVoice.Features.Routing.RoutingCatalogStore>();
            services.AddSingleton<BetterGenshinImpact.ViewModel.Pages.QuestVoiceSettingsViewModel>();
            // 键鼠录制：相对鼠标移动用 XInput2 原始移动（DirectInput / RawInput 两种方式都映射到它），按键与按钮来自全局键鼠监听
            services.AddSingleton<IRelativeMouseInputMonitorFactory>(_ => new X11RelativeMouseInputMonitorFactory(BetterGenshinImpact.Platform.Linux.Wayland.LinuxSession.CreateRelativeMouseMonitor(platform, options.Display)));
            services.AddSingleton<KeyMouseRecordService>();
            services.AddSingleton<KeyMouseRecordController>();
            // 通知：网络通知器与 Windows 版相同（Core 的 NetworkNotifierFactory），系统通知走 freedesktop Notifications
            services.AddHttpClient(HttpClientNames.Default, c =>
            {
                c.Timeout = TimeSpan.FromSeconds(30);
                c.DefaultRequestHeaders.UserAgent.ParseAdd($"BetterGI/{BetterGenshinImpact.Core.Config.Global.Version}");
            });
            // 下载服务（网络背景图等大文件）：不限总时长、关闭自动解压缩（与 WPF AddBgiHttpClients 相同）
            services.AddHttpClient(HttpClientNames.Download, c => c.Timeout = Timeout.InfiniteTimeSpan)
                .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.HttpClientHandler
                {
                    AutomaticDecompression = System.Net.DecompressionMethods.None,
                });
            services.AddSingleton(sp => new BetterGenshinImpact.Infrastructure.Network.Download.DownloadService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientNames.Download),
                BetterGenshinImpact.Infrastructure.Storage.AppPaths.Downloads,
                sp.GetRequiredService<ILogger<BetterGenshinImpact.Infrastructure.Network.Download.DownloadService>>()));
            services.AddSingleton<BetterGenshinImpact.Infrastructure.Network.Download.IDownloadService>(sp =>
                sp.GetRequiredService<BetterGenshinImpact.Infrastructure.Network.Download.DownloadService>());
            services.AddSingleton<BetterGenshinImpact.Service.Interface.IBannerImageService, BetterGenshinImpact.Service.BannerImageService>();
            services.AddHttpClient(HttpClientNames.Notification, c => c.Timeout = TimeSpan.FromSeconds(30));
            services.AddSingleton<NotifierManager>();
            services.AddSingleton<LinuxNotificationService>();
            services.AddSingleton<INotificationHub>(sp => sp.GetRequiredService<LinuxNotificationService>());
            // 服务器时间（月卡判断、按服务器日期的执行周期等使用，与 WPF 相同）
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IServerTimeProvider, ServerTimeProvider>();
            // 地图点位（遮罩小地图 / 大地图点位层）：点位服务与三个数据源接口与 WPF 相同；文件缓存放在 Cache/Files（与 WPF 相同的分区与过期规则，
            // 定期清理的 CacheMaintenanceService 是托管服务，Linux 宿主不跑托管服务，过期条目在读取时按 FileCache 规则失效）
            services.AddHttpClient(HttpClientNames.GameApi, c => c.Timeout = TimeSpan.FromSeconds(30));
            services.AddHttpClient(HttpClientNames.Images, c => c.Timeout = TimeSpan.FromSeconds(15));
            services.AddMemoryCache();
            services.AddSingleton<LazyCache.IAppCache, LazyCache.CachingService>();
            services.AddSingleton(sp => new BetterGenshinImpact.Infrastructure.Cache.FileCache(BetterGenshinImpact.Infrastructure.Storage.AppPaths.FileCache,
                sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<BetterGenshinImpact.Infrastructure.Cache.FileCache>>()));
            services.AddSingleton<BetterGenshinImpact.Infrastructure.Cache.IFileCache>(sp => sp.GetRequiredService<BetterGenshinImpact.Infrastructure.Cache.FileCache>());
            services.AddSingleton<IMihoyoMapApiService, MihoyoMapApiService>();
            services.AddSingleton<IKongyingTavernApiService, BetterGenshinImpact.Service.Tavern.KongyingTavernApiService>();
            services.AddSingleton<IHoYoLabMapApiService, HoYoLabMapApiService>();
            services.AddSingleton<IMaskMapPointService, MaskMapPointService>();
            services.AddSingleton<BetterGenshinImpact.ViewModel.MaskWindowViewModel>();
            // 任务计划（Pulonia）：模型、存储、执行服务与执行器都在 Core，与 Windows 相同；
            // 账户切换器在 Core，登录凭据存到系统密钥环（Secret Service），运行环境检查为本机 XTest / uinput 输入 + Wine 下的 YuanShen.exe；
            // 没有多实例（不注册远程派发）
            PuloniaTaskStore.DefaultCredentialVaultFactory = static () => new BetterGenshinImpact.Platform.Linux.Accounts.SecretServiceCredentialVault();
            PuloniaGenshinAccountSwitcher.DefaultEnvironmentFactory = static () => new BetterGenshinImpact.Platform.Linux.Accounts.LinuxAccountSwitchEnvironment();
            services.AddSingleton<PuloniaTaskStore>();
            services.AddSingleton(_ => ScriptRepositoryStore.Shared);
            services.AddSingleton<PuloniaTaskBuilder>();
            services.AddSingleton<PuloniaTaskClipboardService>();
            services.AddSingleton<PuloniaCSharpTaskRegistry>();
            services.AddSingleton<PuloniaGameTaskCoordinator>();
            services.AddSingleton<IPuloniaGenshinAccountSwitcher, PuloniaGenshinAccountSwitcher>();
            services.AddSingleton<IPuloniaTaskExecutor, PuloniaCSharpTaskExecutor>();
            services.AddSingleton<IPuloniaTaskExecutor, PuloniaShellTaskExecutor>();
            services.AddSingleton<IPuloniaTaskExecutor, PuloniaPathingTaskExecutor>();
            services.AddSingleton<IPuloniaTaskExecutor, PuloniaJavaScriptTaskExecutor>();
            services.AddSingleton<IPuloniaTaskExecutor, PuloniaKeyMouseTaskExecutor>();
            services.AddSingleton<IPuloniaTaskExecutor, PuloniaSoloTaskExecutor>();
            services.AddSingleton<PuloniaTaskResourceCatalog>();
            services.AddSingleton<PuloniaTaskCreationService>();
            // 账户目录与实例当前账户（上游统筹设计）。多实例（宿主启动前调用了 InstanceBootstrap）时与 Windows 相同：
            // 实例键取自实例上下文（根实例 = 主实例，另一 Unix 会话里的实例 = 子实例），注册实例 IPC、在线标记与 Pulonia 一步下发；
            // 没有调用时（无界面宿主、自测）只有主实例，不注册远程派发
            var instanceBootstrap = BetterGenshinImpact.Service.Instance.InstanceBootstrap.Current;
            // 外部访问的状态、令牌与日志分发器总是注册（设置页可查看状态、重新生成令牌）；服务本身随多实例上下文注册（只在根实例监听）
            services.AddSingleton(HostLogging.ExternalLog);
            services.AddSingleton<BetterGenshinImpact.Service.ExternalAccess.ExternalAccessState>();
            services.AddSingleton<BetterGenshinImpact.Service.ExternalAccess.ExternalAccessTokenService>();
            if (instanceBootstrap is not null)
            {
                services.AddSingleton(instanceBootstrap);
                services.AddSingleton<IPuloniaInstanceKeyProvider>(_ => PuloniaInstanceKeyProvider.FromContext(instanceBootstrap.Context));
                services.AddSingleton<BetterGenshinImpact.Service.Instance.InstanceService>();
                services.AddSingleton<PuloniaInstancePresence>();
                // 外部访问（同 WPF）：只有根实例（主实例）监听 127.0.0.1，HTTP / WebSocket / MCP 复用同一能力服务
                services.AddSingleton<BetterGenshinImpact.Service.ExternalAccess.IBgiExternalCapabilityService, BetterGenshinImpact.Service.ExternalAccess.BgiExternalCapabilityService>();
                services.AddSingleton<BetterGenshinImpact.Service.ExternalAccess.ExternalLogWebSocketHandler>();
                services.AddSingleton<BetterGenshinImpact.Service.ExternalAccess.ExternalAccessHost>();
                services.AddSingleton(sp => new BetterGenshinImpact.Pulonia.Remote.PuloniaRunDispatcher(
                    () => sp.GetRequiredService<BetterGenshinImpact.Service.Instance.InstanceService>(),
                    sp.GetRequiredService<TimeProvider>(),
                    sp.GetRequiredService<ILogger<BetterGenshinImpact.Pulonia.Remote.PuloniaRunDispatcher>>()));
                services.AddSingleton(sp => new BetterGenshinImpact.Pulonia.Remote.PuloniaRunDispatchHost(
                    sp.GetRequiredService<IPuloniaTaskService>(),
                    sp.GetRequiredService<IPuloniaInstanceKeyProvider>(),
                    () => sp.GetRequiredService<BetterGenshinImpact.Service.Instance.InstanceService>(),
                    sp.GetRequiredService<ILogger<BetterGenshinImpact.Pulonia.Remote.PuloniaRunDispatchHost>>()));
            }
            else
            {
                services.AddSingleton<IPuloniaInstanceKeyProvider>(_ => new PuloniaInstanceKeyProvider(PuloniaInstanceKey.Primary));
            }
            services.AddSingleton<IPuloniaAccountService, PuloniaAccountService>();
            // 临时文件（仓库 zip 导入解压、更换仓库地址时重新克隆等）：与 WPF 相同按“实例类型-进程号”分目录，退出时删除，
            // 已结束进程留下的目录在启动时回收（租约文件以独占方式打开，Linux 上为独占 flock）
            var tempDirectoryName = instanceBootstrap is not null
                ? $"{instanceBootstrap.Context.InstanceType.ToString().ToLowerInvariant()}-{instanceBootstrap.Context.ProcessId}"
                : $"primary-{Environment.ProcessId}";
            services.AddSingleton(sp => new BetterGenshinImpact.Infrastructure.Temp.TempFileService(tempDirectoryName,
                sp.GetRequiredService<ILogger<BetterGenshinImpact.Infrastructure.Temp.TempFileService>>()));
            services.AddSingleton<BetterGenshinImpact.Infrastructure.Temp.ITempFileService>(sp => sp.GetRequiredService<BetterGenshinImpact.Infrastructure.Temp.TempFileService>());
            // 存储空间（设置页“存储空间”与悬浮小窗设置页）：与 WPF 相同的 StorageService、推迟清理记录与各区域来源；
            // 网页数据为 WebKitGTK 的 Data/WebKit 目录（WPF 为 WebView2 配置）；Linux 不使用下载服务，没有“下载”分组
            services.AddSingleton<BetterGenshinImpact.Infrastructure.Storage.PendingCleanupStore>();
            services.AddSingleton<BetterGenshinImpact.Infrastructure.Storage.IStorageService, BetterGenshinImpact.Infrastructure.Storage.StorageService>();
            services.AddSingleton<BetterGenshinImpact.Infrastructure.Storage.IStorageAreaProvider>(sp => sp.GetRequiredService<BetterGenshinImpact.Infrastructure.Cache.FileCache>());
            services.AddSingleton<BetterGenshinImpact.Infrastructure.Storage.IStorageAreaProvider>(sp => sp.GetRequiredService<BetterGenshinImpact.Infrastructure.Temp.TempFileService>());
            services.AddSingleton<BetterGenshinImpact.Infrastructure.Storage.IStorageAreaProvider, BetterGenshinImpact.Host.Common.Storage.LinuxWebDataStorageAreaProvider>();
            services.AddSingleton<BetterGenshinImpact.Infrastructure.Storage.IStorageAreaProvider, BetterGenshinImpact.Core.Script.ScriptUpdateBackupStorageAreaProvider>();
            services.AddSingleton<BetterGenshinImpact.Infrastructure.Storage.IStorageAreaProvider, BetterGenshinImpact.Core.Recognition.ONNX.InferenceCacheStorageAreaProvider>();
            services.AddSingleton<BetterGenshinImpact.Infrastructure.Storage.IStorageAreaProvider, BetterGenshinImpact.Service.AppStorageAreaProvider>();
            // 文件缓存维护（超过上限时删除最早的缓存，每天一次）：与 WPF 相同只在根实例执行，由宿主随启动 / 退出启停
            var isRootInstance = instanceBootstrap?.Context.IsRoot ?? true;
            services.AddSingleton(sp => new BetterGenshinImpact.Infrastructure.Cache.CacheMaintenanceService(
                sp.GetRequiredService<BetterGenshinImpact.Infrastructure.Cache.FileCache>(), config.StorageConfig, isRootInstance,
                sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<BetterGenshinImpact.Infrastructure.Cache.CacheMaintenanceService>>()));
            services.AddSingleton<IPuloniaTaskService>(sp => new PuloniaTaskService(
                sp.GetRequiredService<PuloniaTaskStore>(),
                sp.GetRequiredService<PuloniaTaskBuilder>(),
                sp.GetServices<IPuloniaTaskExecutor>(),
                sp.GetRequiredService<PuloniaGameTaskCoordinator>(),
                sp.GetRequiredService<TaskStopService>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetService<IPuloniaGenshinAccountSwitcher>(),
                logger: sp.GetRequiredService<ILogger<PuloniaTaskService>>(),
                accountService: sp.GetRequiredService<IPuloniaAccountService>()));
            services.AddSingleton<LinuxPuloniaTriggerHotkeys>();
            services.AddSingleton<PuloniaSchedulerHost>();
            // 悬浮小窗 / 字幕浮窗：与 WPF 同一个协调服务；窗口由图形界面宿主经 configureServices 注册 Func<IGuideWindowView> / Func<IGuideSubtitleView>，
            // 没有注册（无界面宿主）时打开小窗报错并提示。遮罩状态由图形界面宿主的遮罩窗口发布
            services.AddSingleton<BetterGenshinImpact.Service.Guide.GuideLibraryStore>();
            services.AddSingleton<BetterGenshinImpact.Core.Mask.MaskWindowStateSource>();
            services.AddSingleton<BetterGenshinImpact.Core.Mask.IMaskWindowHost>(sp => sp.GetRequiredService<BetterGenshinImpact.Core.Mask.MaskWindowStateSource>());
            services.AddSingleton(sp => new BetterGenshinImpact.Service.Guide.GuideWindowService(
                sp.GetRequiredService<IConfigService>(),
                sp.GetRequiredService<BetterGenshinImpact.Service.Guide.GuideLibraryStore>(),
                sp.GetRequiredService<GameRuntimeService>(),
                sp.GetRequiredService<TaskTriggerDispatcher>(),
                sp.GetRequiredService<BetterGenshinImpact.Core.Mask.IMaskWindowHost>(),
                sp.GetService<BetterGenshinImpact.Service.Instance.InstanceBootstrap>(),
                sp.GetService<Func<BetterGenshinImpact.Service.Guide.IGuideWindowView>>()
                    ?? (() => throw new InvalidOperationException("没有图形界面，不能打开悬浮小窗")),
                sp.GetService<Func<BetterGenshinImpact.Service.Guide.IGuideSubtitleView>>()
                    ?? (() => throw new InvalidOperationException("没有图形界面，不能打开字幕浮窗")),
                sp.GetRequiredService<ILogger<BetterGenshinImpact.Service.Guide.GuideWindowService>>()));
            services.AddSingleton<BetterGenshinImpact.Service.Guide.IGuideWindowService>(sp => sp.GetRequiredService<BetterGenshinImpact.Service.Guide.GuideWindowService>());
            configureServices?.Invoke(services);
            var provider = services.BuildServiceProvider();
            CoreServices.SetProvider(() => provider);
            ServerTimeHelper.Initialize(provider.GetRequiredService<IServerTimeProvider>());
            // 输入通道的“不支持的操作已忽略 / 已跳过”告警写进日志。
            // 先取出记录器：停止截图器时仍在执行的一帧可能在服务容器释放后才发出告警
            var inputLogger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Input");
            InputChannelBase.LoggerFactory = () => inputLogger;

            // 推迟到本次启动的清理（网页数据、推理缓存等）必须在使用方加载之前执行；与 WPF 相同只在根实例执行，失败不影响启动
            if (BetterGenshinImpact.Service.Instance.InstanceBootstrap.Current?.Context.IsRoot ?? true)
            {
                try
                {
                    provider.GetRequiredService<BetterGenshinImpact.Infrastructure.Storage.PendingCleanupStore>().ExecutePending();
                }
                catch (Exception e)
                {
                    log.Warning(e, "执行推迟的存储清理失败，已跳过且不影响程序启动。");
                }
            }

            var gameHost = new GameHost(options, platform, probe, config, provider, log, fileConfig, linuxConfigStore, linuxConfig, manualWindow);
            gameHost.StartInstanceServices();
            provider.GetRequiredService<BetterGenshinImpact.Infrastructure.Temp.TempFileService>().StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            provider.GetRequiredService<BetterGenshinImpact.Infrastructure.Cache.CacheMaintenanceService>().StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            // Silero VAD 实时诊断（与 WPF 的托管服务相同：未开启时只轮询配置）
            provider.GetRequiredService<BetterGenshinImpact.GameTask.AutoSkip.Audio.DialogueOptionVoiceDiagnosticService>().StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            return gameHost;
        }
        catch
        {
            platform.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 多实例：启动实例 IPC（根实例监听根管道 / 子实例连接根实例）、在线标记（online.lock）与 Pulonia 一步下发宿主（子实例接受下发）。
    /// 与 WPF 版的托管服务启动顺序相同；宿主界面就绪后再调用 <see cref="MarkInstanceApplicationReady"/> 处理排队的激活
    /// </summary>
    private void StartInstanceServices()
    {
        if (_provider.GetService<BetterGenshinImpact.Service.Instance.InstanceService>() is not { } instances)
            return;
        instances.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        ((Microsoft.Extensions.Hosting.IHostedService)_provider.GetRequiredService<PuloniaInstancePresence>()).StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        ((Microsoft.Extensions.Hosting.IHostedService)_provider.GetRequiredService<BetterGenshinImpact.Pulonia.Remote.PuloniaRunDispatchHost>()).StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var context = instances.Context;
        var typeLabel = context.InstanceName is { } instanceName ? $"{context.InstanceType}({instanceName})" : context.InstanceType.ToString();
        HostLogging.ExternalLogInstance = $"{typeLabel}:S{context.WindowsSessionId}:P{context.ProcessId}:T{context.StartedAt.ToUnixTimeMilliseconds()}";
        // 外部访问在实例服务之后启动（同 WPF 托管服务顺序）；启动失败只更新状态，不影响宿主
        _provider.GetRequiredService<BetterGenshinImpact.Service.ExternalAccess.ExternalAccessHost>().StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        _log.Information("多实例：{Type}，进程 {Pid}，Unix 会话 {Session}，实例键 {Key}", context.InstanceType, context.ProcessId,
            context.WindowsSessionId, _provider.GetRequiredService<IPuloniaInstanceKeyProvider>().CurrentInstanceKey);
    }

    /// <summary>界面就绪：处理在此之前收到的激活转发（与 WPF 主窗口加载后相同）</summary>
    public void MarkInstanceApplicationReady() =>
        _provider.GetService<BetterGenshinImpact.Service.Instance.InstanceService>()?.MarkApplicationReady();

    /// <summary>悬浮小窗协调服务（与 WPF 相同）；图形界面就绪后调用 <see cref="StartGuide"/></summary>
    public BetterGenshinImpact.Service.Guide.GuideWindowService Guide =>
        _provider.GetRequiredService<BetterGenshinImpact.Service.Guide.GuideWindowService>();

    /// <summary>
    /// 启动悬浮小窗服务（订阅截图器、任务模式与配置变化，按设置自动打开）。在界面线程、主窗口打开后调用一次，
    /// 对应 WPF 托管服务的 StartAsync
    /// </summary>
    public void StartGuide()
    {
        if (_guideStarted)
        {
            return;
        }

        _guideStarted = true;
        Guide.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private bool _guideStarted;

    /// <summary>多实例服务（未启用多实例时为 null）</summary>
    public BetterGenshinImpact.Service.Instance.InstanceService? Instances =>
        _provider.GetService<BetterGenshinImpact.Service.Instance.InstanceService>();

    /// <summary>初始化 CPU 推理（OCR 等），首次启动截图器前调用</summary>
    public async Task InitializeInferenceAsync()
    {
        var inference = Inference;
        await inference.InitializeAsync();
        if (!inference.IsCurrentValidated)
        {
            // 新选的 ROCm：先用 OCR 模型实际加载一次 provider 验证（同 Windows 主窗口加载后的验证），失败时回退 CPU 并记录原因
            await inference.ValidatePendingAsync();
        }
        _log.Information("推理：{Engine}，设备 {Device}，运行库目录 {Dir}{Message}", inference.Current.Engine,
            inference.Current.Device.Name, inference.Current.RuntimeDirectory,
            string.IsNullOrEmpty(inference.StartupMessage) ? "" : "，" + inference.StartupMessage);
    }

    /// <summary>启动截图器（找窗 → 截图 → 输入 → 触发器调度）；找不到游戏窗口等失败时返回 null</summary>
    public async Task<GameRuntime?> StartAsync(CancellationToken ct = default)
    {
        if (ManualWindow.Handle is null && !LinuxConfig.CloudGameEnabled && LinuxConfig.LaunchGameEnabled && !IsGameWindowPresent())
        {
            // 同时启动游戏：找不到游戏窗口时按设置的命令启动，等待窗口出现
            if (!await LaunchGameAndWaitAsync(ct))
            {
                return null;
            }
        }

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

    /// <summary>按进程名能否找到游戏窗口</summary>
    public bool IsGameWindowPresent()
    {
        using var window = Platform.GameWindows.FindGameWindow(new GameWindowSearch(Options.ProcessNames));
        return window is not null;
    }

    /// <summary>按 Linux 宿主设置启动游戏并等待游戏窗口出现</summary>
    public async Task<bool> LaunchGameAndWaitAsync(CancellationToken ct = default)
    {
        try
        {
            GameLauncher.Launch(LinuxConfig, _log);
        }
        catch (Exception e)
        {
            _log.Error(e, "启动游戏失败");
            UiHost.Dialogs.Error("启动游戏失败：" + e.Message);
            return false;
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(5, LinuxConfig.LaunchTimeoutSeconds));
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(1000, ct);
            if (IsGameWindowPresent())
            {
                _log.Information("游戏窗口已出现");
                // 等窗口完成映射与首帧绘制
                await Task.Delay(1500, ct);
                return true;
            }
        }

        _log.Error("启动游戏后 {Seconds} 秒内没有找到游戏窗口（进程名 {Names}）", (int)timeout.TotalSeconds, string.Join(", ", Options.ProcessNames));
        UiHost.Dialogs.Error($"启动游戏后 {(int)timeout.TotalSeconds} 秒内没有找到游戏窗口");
        return false;
    }

    /// <summary>列出可选的顶层窗口（手动选择游戏窗口用），游戏窗口候选排在前面</summary>
    public IReadOnlyList<WindowInfo> ListWindows()
    {
        var search = new GameWindowSearch(Options.ProcessNames);
        using var game = Platform.GameWindows.FindGameWindow(search);
        var result = new List<WindowInfo>();
        foreach (var window in Platform.Windows.EnumerateWindows())
        {
            using (window)
            {
                var rect = window.Viewport.ScreenRect;
                // 跳过太小、无标题的窗口和宿主自己的窗口
                if (rect.Width < 64 || rect.Height < 64 || string.IsNullOrEmpty(window.Title) || window.ProcessId == Environment.ProcessId)
                {
                    continue;
                }

                result.Add(new WindowInfo(window.Handle, window.Title, window.ClassName, window.ProcessName, window.ProcessId,
                    rect.Width, rect.Height, game is not null && game.Handle == window.Handle));
            }
        }

        return [.. result.OrderByDescending(w => w.IsGameCandidate).ThenBy(w => w.Title, StringComparer.CurrentCulture)];
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

    private void OnPortalDiagnostic(string message) => _log.Warning("截图（ScreenCast 门户）：{Message}", message);

    public async ValueTask DisposeAsync()
    {
        await QuestVoice.StopAsync(applicationExit: true);
        if (RuntimeService.IsRunning)
        {
            await RuntimeService.StopAsync();
        }

        // 悬浮小窗：保存播放进度与收藏（与 WPF 托管服务停止时相同），停止计时器
        if (_guideStarted)
        {
            try
            {
                await Guide.StopAsync(CancellationToken.None);
                Guide.StopPlatformTimers();
            }
            catch (Exception e)
            {
                _log.Warning(e, "停止悬浮小窗服务失败");
            }
        }

        // 外部访问：后启动的先停止
        if (_provider.GetService<BetterGenshinImpact.Service.ExternalAccess.ExternalAccessHost>() is { } externalAccess)
        {
            try
            {
                await externalAccess.StopAsync(CancellationToken.None);
            }
            catch (Exception e)
            {
                _log.Warning(e, "停止外部访问服务失败");
            }
        }

        // 多实例：先停一步下发宿主与实例 IPC，再释放在线标记（与 WPF 托管服务的停止顺序相同：后启动的先停止）
        if (_provider.GetService<BetterGenshinImpact.Service.Instance.InstanceService>() is { } instances)
        {
            try
            {
                await ((Microsoft.Extensions.Hosting.IHostedService)_provider.GetRequiredService<BetterGenshinImpact.Pulonia.Remote.PuloniaRunDispatchHost>()).StopAsync(CancellationToken.None);
                await instances.StopAsync(CancellationToken.None);
                await ((Microsoft.Extensions.Hosting.IHostedService)_provider.GetRequiredService<PuloniaInstancePresence>()).StopAsync(CancellationToken.None);
            }
            catch (Exception e)
            {
                _log.Warning(e, "停止多实例服务失败");
            }
        }

        try
        {
            await _provider.GetRequiredService<BetterGenshinImpact.GameTask.AutoSkip.Audio.DialogueOptionVoiceDiagnosticService>().StopAsync(CancellationToken.None);
        }
        catch (Exception e)
        {
            _log.Warning(e, "停止语音检测诊断失败");
        }

        try
        {
            await _provider.GetRequiredService<BetterGenshinImpact.Infrastructure.Cache.CacheMaintenanceService>().StopAsync(CancellationToken.None);
        }
        catch (Exception e)
        {
            _log.Warning(e, "停止文件缓存维护失败");
        }

        // 删除本进程的临时目录（与 WPF 托管服务停止时相同）
        try
        {
            await _provider.GetRequiredService<BetterGenshinImpact.Infrastructure.Temp.TempFileService>().StopAsync(CancellationToken.None);
        }
        catch (Exception e)
        {
            _log.Warning(e, "清理临时目录失败");
        }

        // 写入未保存的配置改动
        _fileConfig?.Dispose();
        _linuxConfigStore?.Dispose();
        BetterGenshinImpact.Platform.Linux.Wayland.PortalScreenCastCapture.Diagnostic -= OnPortalDiagnostic;
        await _provider.DisposeAsync();
        _platform.Dispose();
    }
}

/// <summary>截图方式</summary>
/// <summary>截图方式说明</summary>
public sealed record CaptureMethodInfo(string Id, string Name, string Description);

/// <summary>可选择的顶层窗口</summary>
public sealed record WindowInfo(nint Handle, string Title, string ClassName, string ProcessName, int ProcessId, int Width, int Height,
    bool IsGameCandidate)
{
    public string Display => $"{Title}  [{ClassName}]  {ProcessName}（{ProcessId}） {Width}×{Height}" + (IsGameCandidate ? "  ★游戏窗口" : "");
}

/// <summary>手动选择的窗口：只记录句柄，每次启动截图器时重新打开（运行环境持有并释放打开的窗口对象）</summary>
public sealed class ManualWindowSelection(IPlatformServices platform)
{
    private nint? _handle;

    public nint? Handle
    {
        get => _handle;
        set
        {
            _handle = value;
            Changed?.Invoke();
        }
    }

    public event Action? Changed;

    /// <summary>打开选择的窗口；没有选择或窗口已关闭时返回 null</summary>
    public IPlatformWindow? Open() => _handle is { } h ? platform.Windows.FromHandle(h) : null;
}
