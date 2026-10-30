using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using BetterGenshinImpact.Avalonia;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Avalonia.ViewModels;
using BetterGenshinImpact.Avalonia.Views;
using BetterGenshinImpact.Host.Common;
using BetterGenshinImpact.Platform;
using Serilog;

// 用法：bgi-avalonia [--display :0] [--assets 主程序输出目录] [--process YuanShen,GenshinImpact] [--interval 50]
//                    [--no-pick] [--no-skip] [--auto-enter | --no-auto-enter] [--log 日志文件]
//                    [--page 实时触发] [--auto-start] [--screenshots 截图目录] [--exit-after 秒] [--ui-selftest]
// --auto-start / --screenshots / --exit-after 用于无人值守验证：启动后自动启动截图器，
// 按页面保存界面截图（启动、实时触发、环境），到时退出
internal static class AvaloniaHostProgram
{
    [STAThread]
    public static int Main(string[] args)
    {
        var options = AvaloniaOptions.Parse(args);
        HostLogging.Configure(options.LogFile, c => c.WriteTo.Sink(UiLogSink.Instance));
        var log = Log.ForContext("SourceContext", "Avalonia");
        try
        {
            App.Startup = desktop => Startup(desktop, options, log);
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime([]);
        }
        catch (Exception e)
        {
            log.Fatal(e, "Avalonia 宿主异常退出");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new FontManagerOptions { FontFallbacks = FontInfo.Fallbacks })
            .LogToTrace();

    private static void Startup(IClassicDesktopStyleApplicationLifetime desktop, AvaloniaOptions options, ILogger log)
    {
        FontInfo.Detect();
        log.Information("界面字体：{Font}", FontInfo.UiFontFamily.Name);

        MainWindow? window = null;
        MainWindowViewModel? viewModel = null;

        // UiHost：UI 线程、消息框、剪贴板（任务代码经 UiHost 使用）
        UiHost.Dispatcher = new AvaloniaUiDispatcher();
        UiHost.Dialogs = new AvaloniaDialogService(() => window, (kind, message) => viewModel?.ShowToast(kind, message));
        UiHost.Clipboard = new AvaloniaClipboard(() => window);

        GameHost? host = null;
        string? hostError = null;
        try
        {
            host = GameHost.Create(options.Host, log);
        }
        catch (Exception e)
        {
            hostError = e.Message;
            log.Error(e, "平台服务初始化失败（是否设置了 DISPLAY / 能否连接 X 服务器？）");
        }

        viewModel = new MainWindowViewModel(host, hostError, log);
        window = new MainWindow { DataContext = viewModel };
        desktop.MainWindow = window;
        desktop.Exit += (_, _) =>
        {
            if (host is not null)
            {
                // 退出时（关闭主窗口或 Shutdown）停止截图器并释放平台连接（X11），避免输入线程使用已关闭的连接
                host.DisposeAsync().AsTask().GetAwaiter().GetResult();
                log.Information("已停止截图器并释放平台连接");
            }
        };

        window.Opened += async (_, _) =>
        {
            if (options.Page is { } page && !viewModel.Navigate(page))
            {
                log.Warning("没有名为 {Page} 的页面", page);
            }

            // 后台初始化推理（OCR），不阻塞界面
            _ = viewModel.Home.EnsureInferenceAsync();
            if (options.AutoStart && host is not null)
            {
                await viewModel.Home.StartAsync();
            }

            if (options.UiSelfTest)
            {
                await UiSelfTestAsync(desktop, log, options.ScreenshotDirectory);
            }

            if (options.ScreenshotDirectory is not null || options.ExitAfterSeconds is not null)
            {
                await RunUnattendedAsync(window, viewModel, options, log, desktop);
            }
        };
    }

    /// <summary>无人值守验证：等待截图器运行一段时间，依次切换页面保存界面截图，然后退出</summary>
    private static async Task RunUnattendedAsync(MainWindow window, MainWindowViewModel viewModel, AvaloniaOptions options, ILogger log,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        var total = TimeSpan.FromSeconds(options.ExitAfterSeconds ?? 15);
        var started = DateTime.UtcNow;
        if (options.ScreenshotDirectory is { } dir)
        {
            Directory.CreateDirectory(dir);
            // 先让触发器跑一会儿，日志面板里能看到拾取 / 剧情的记录
            await Task.Delay(total / 2);
            foreach (var (page, file) in new[] { ("实时触发", "avalonia-trigger.png"), ("环境", "avalonia-environment.png"), ("独立任务", "avalonia-placeholder.png"), ("启动", "avalonia-home.png") })
            {
                viewModel.Navigate(page);
                await Task.Delay(600);
                SaveScreenshot(window, Path.Combine(dir, file), log);
            }
        }

        var remaining = total - (DateTime.UtcNow - started);
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining);
        }

        if (options.ExitAfterSeconds is not null)
        {
            log.Information("无人值守运行结束，退出（累计处理 {Frames} 帧）", viewModel.Host?.FramesProcessed ?? 0);
            desktop.Shutdown();
        }
    }

    /// <summary>
    /// UiHost 自检：从后台线程经 UiHost 调度到 UI 线程、剪贴板写入后读回（X11 剪贴板）、轻提示、消息框（截图后关闭）
    /// </summary>
    private static async Task UiSelfTestAsync(IClassicDesktopStyleApplicationLifetime desktop, ILogger log, string? dir)
    {
        var onUiThread = await Task.Run(() => UiHost.Dispatcher.Invoke(() => UiHost.Dispatcher.CheckAccess()));
        log.Information("UiHost 自检：后台线程 Invoke 到 UI 线程 = {Ok}", onUiThread);

        var text = $"BetterGI 剪贴板自检 {DateTime.Now:HH:mm:ss}";
        UiHost.Clipboard.SetText(text);
        await Task.Delay(300);
        var readBack = await Task.Run(() => UiHost.Clipboard.GetText());
        log.Information("UiHost 自检：剪贴板写入后从后台线程读回 {Result}（{Text}）", readBack == text ? "一致" : "不一致", readBack);

        UiHost.Dialogs.Toast(DialogKind.Information, "UiHost 自检：这是一条轻提示");
        var dialog = Task.Run(() => UiHost.Dialogs.Show("这是 UiHost.Dialogs 的消息框自检（任务代码在后台线程同步调用）。\n截图后自动关闭。",
            "消息框自检", DialogKind.Question, DialogButtons.YesNo));
        await Task.Delay(800);
        var box = desktop.Windows.OfType<MessageBoxWindow>().FirstOrDefault();
        if (box is null)
        {
            log.Warning("UiHost 自检：没有找到消息框窗口");
            return;
        }

        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
            SaveScreenshot(box, Path.Combine(dir, "avalonia-messagebox.png"), log);
            if (desktop.MainWindow is { } main)
            {
                SaveScreenshot(main, Path.Combine(dir, "avalonia-toast.png"), log);
            }
        }

        box.Close(DialogResult.Yes);
        var result = await dialog;
        log.Information("UiHost 自检：消息框返回 {Result}", result);
    }

    private static void SaveScreenshot(Window window, string path, ILogger log)
    {
        var scale = window.RenderScaling;
        var size = new global::Avalonia.PixelSize((int)(window.Bounds.Width * scale), (int)(window.Bounds.Height * scale));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        bitmap.Render(window);
#pragma warning disable CS0618 // BitmapEncoderOptions 重载在 12.x 中签名尚不稳定，PNG 默认编码即可
        bitmap.Save(path);
#pragma warning restore CS0618
        log.Information("界面截图：{Path}", path);
    }
}

internal sealed record AvaloniaOptions(
    GameHostOptions Host,
    string? LogFile,
    string? Page,
    bool AutoStart,
    string? ScreenshotDirectory,
    int? ExitAfterSeconds,
    bool UiSelfTest)
{
    public static AvaloniaOptions Parse(string[] args)
    {
        var host = new GameHostOptions();
        string? logFile = null, page = null, screenshots = null;
        var autoStart = false;
        var uiSelfTest = false;
        int? exitAfter = null;
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} 缺少参数值");
            if (host.Apply(args[i], Next) is { } applied)
            {
                host = applied;
                continue;
            }

            switch (args[i])
            {
                case "--log": logFile = Next(); break;
                case "--page": page = Next(); break;
                case "--auto-start": autoStart = true; break;
                case "--screenshots": screenshots = Next(); break;
                case "--exit-after": exitAfter = int.Parse(Next()); break;
                case "--ui-selftest": uiSelfTest = true; break;
                default: throw new ArgumentException($"未知参数 {args[i]}");
            }
        }

        return new AvaloniaOptions(host, logFile, page, autoStart, screenshots, exitAfter, uiSelfTest);
    }
}
