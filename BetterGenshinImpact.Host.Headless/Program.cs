using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Host.Common;
using OpenCvSharp;
using Serilog;

// 用法：bgi-headless [--display :0] [--assets 主程序输出目录] [--process YuanShen,GenshinImpact]
//                    [--seconds 30] [--interval 50] [--frames 截图目录] [--no-pick] [--no-skip] [--log 日志文件]
//                    [--auto-enter | --no-auto-enter]
//        bgi-headless --check-opencv    # 只检查 OpenCvSharpExtern 原生库的模块（特征匹配、dnn 等）
var options = HeadlessOptions.Parse(args);

HostLogging.Configure(options.LogFile);
var log = Log.ForContext("SourceContext", "Headless");

try
{
    if (options.CheckOpenCv)
    {
        if (options.Host.AssetsDirectory is { } assets)
        {
            Global.StartUpPath = Path.GetFullPath(assets) + Path.DirectorySeparatorChar;
        }

        // 只检查 OpenCvSharpExtern 的模块是否齐全（大地图特征匹配、dnn 等），不连接 X 服务器
        return OpenCvSelfCheck.Run(log) ? 0 : 3;
    }

    // 平台服务、配置、依赖注入、推理：与 Avalonia 宿主共用 GameHost
    await using var host = GameHost.Create(options.Host, log);
    await host.InitializeInferenceAsync();

    var dispatcher = host.Dispatcher;
    var savedFirst = false;
    Mat? lastFrame = null;
    var frameLock = new object();
    dispatcher.FrameCaptured += frame =>
    {
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

    };

    var runtime = await host.StartAsync();
    if (runtime is null)
    {
        return 2;
    }

    host.TickOnce();

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
                runtime.Window.IsForeground, runtime.Window.IsMinimized, runtime.Capture.IsCapturing, host.FramesProcessed);
        }
    }
    catch (OperationCanceledException)
    {
    }

    await host.StopAsync();
    lock (frameLock)
    {
        if (lastFrame is not null && options.FramesDirectory is not null)
        {
            Cv2.ImWrite(Path.Combine(options.FramesDirectory, "frame-last.png"), lastFrame);
            lastFrame.Dispose();
        }
    }

    log.Information("运行结束：共处理 {Frames} 帧（{Seconds} 秒），帧调度异常 {Errors} 次",
        host.FramesProcessed, options.Seconds, host.TickErrorCount);
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
    GameHostOptions Host,
    int Seconds,
    string? FramesDirectory,
    string? LogFile,
    bool CheckOpenCv)
{
    public static HeadlessOptions Parse(string[] args)
    {
        var host = new GameHostOptions();
        string? frames = null, logFile = null;
        int seconds = 30;
        var checkOpenCv = false;
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} 缺少参数值");
            switch (args[i])
            {
                case "--display": host = host with { Display = Next() }; break;
                case "--assets": host = host with { AssetsDirectory = Next() }; break;
                case "--process": host = host with { ProcessNames = Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) }; break;
                case "--interval": host = host with { IntervalMs = int.Parse(Next()) }; break;
                case "--no-pick": host = host with { Pick = false }; break;
                case "--no-skip": host = host with { Skip = false }; break;
                case "--auto-enter": host = host with { AutoEnterGame = true }; break;
                case "--no-auto-enter": host = host with { AutoEnterGame = false }; break;
                case "--seconds": seconds = int.Parse(Next()); break;
                case "--frames": frames = Next(); break;
                case "--log": logFile = Next(); break;
                case "--check-opencv": checkOpenCv = true; break;
                default: throw new ArgumentException($"未知参数 {args[i]}");
            }
        }

        return new HeadlessOptions(host, seconds, frames, logFile, checkOpenCv);
    }
}
