using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BetterGenshinImpact.Avalonia;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Avalonia.ViewModels;
using BetterGenshinImpact.Avalonia.Views;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Host.Common;
using Microsoft.Extensions.DependencyInjection;
using BetterGenshinImpact.Platform;
using Serilog;

// 用法：bgi-avalonia [--display :0] [--assets 主程序输出目录] [--process YuanShen,GenshinImpact] [--interval 50]
//                    [--no-pick] [--no-skip] [--auto-enter | --no-auto-enter] [--log 日志文件]
//                    [--page 实时触发] [--auto-start] [--screenshots 截图目录] [--exit-after 秒] [--ui-selftest]
//                    [--select-window 标题] [--config 配置文件 | --no-save-config] [--capture 截图方式] [--mask | --no-mask] [--mask-recognition]
//                    [--solo-task 任务标识] [--solo-stop-after 秒]
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
        UiHost.Lifetime = new AvaloniaAppLifetime(desktop);

        GameHost? host = null;
        string? hostError = null;
        try
        {
            // 独立任务页提供脚本与一条龙解析战斗策略时用到的设置（ITaskSettingsSource，WPF 中为 TaskSettingsPageViewModel）
            host = GameHost.Create(options.Host, log,
                services => services.AddSingleton<ITaskSettingsSource>(_ => viewModel!.Tasks));
        }
        catch (Exception e)
        {
            hostError = e.Message;
            log.Error(e, "平台服务初始化失败（是否设置了 DISPLAY / 能否连接 X 服务器？）");
        }

        // 遮罩窗口（透明、置顶、点击穿透），截图器运行且启用遮罩时显示
        MaskWindowController? mask = null;
        if (host is not null)
        {
            mask = new MaskWindowController(host, options.Host.Display, log);
        }

        viewModel = new MainWindowViewModel(host, hostError, log);
        window = new MainWindow { DataContext = viewModel };
        desktop.MainWindow = window;
        desktop.Exit += (_, _) =>
        {
            mask?.Dispose();
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
            if (options.SelectWindowTitle is { } title && host is not null)
            {
                await SelectWindowUnattendedAsync(viewModel, title, options.ScreenshotDirectory, log);
            }

            if (options.AutoStart && host is not null)
            {
                await viewModel.Home.StartAsync();
            }

            if (options.SoloTaskId is { } soloTaskId && host is not null)
            {
                await RunSoloTaskUnattendedAsync(window, viewModel, soloTaskId, options.SoloStopAfterSeconds, options.ScreenshotDirectory, log);
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
            foreach (var (page, file) in new[] { ("实时触发", "avalonia-trigger.png"), ("环境", "avalonia-environment.png"), ("独立任务", "avalonia-tasks.png"), ("一条龙", "avalonia-onedragon.png"), ("启动", "avalonia-home.png") })
            {
                viewModel.Navigate(page);
                await Task.Delay(600);
                SaveScreenshot(window, Path.Combine(dir, file), log);
            }

            // 启动页下半部分：收起日志、展开“同时启动原神”，滚动到底部
            viewModel.IsLogExpanded = false;
            foreach (var expander in window.GetVisualDescendants().OfType<Expander>())
            {
                expander.IsExpanded = true;
            }

            await Task.Delay(400);
            window.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(s => s.Extent.Height > s.Viewport.Height)?.ScrollToEnd();
            await Task.Delay(400);
            SaveScreenshot(window, Path.Combine(dir, "avalonia-home-launch.png"), log);
            viewModel.IsLogExpanded = true;

            // 截图器拿到的一帧（用于核对截图内容，例如是否截到了遮罩）
            if (viewModel.Host is { IsRunning: true } runningHost)
            {
                await SaveCapturedFrameAsync(runningHost, Path.Combine(dir, "capture-frame.png"), log);
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

        // 配置保存：经界面绑定同样的路径修改配置属性，防抖后应写入文件
        if (desktop.MainWindow?.DataContext is MainWindowViewModel { Host: { ConfigFilePath: { } configPath } host })
        {
            var before = File.Exists(configPath) ? File.GetLastWriteTimeUtc(configPath) : DateTime.MinValue;
            var value = !host.Config.AutoSkipConfig.QuicklySkipConversationsEnabled;
            host.Config.AutoSkipConfig.QuicklySkipConversationsEnabled = value;
            host.LinuxConfig.LaunchTimeoutSeconds += 1;
            await Task.Delay(1200);
            var json = File.ReadAllText(configPath);
            var saved = json.Contains($"\"quicklySkipConversationsEnabled\": {(value ? "true" : "false")}", StringComparison.Ordinal);
            log.Information("UiHost 自检：修改配置后防抖保存 {Result}（文件{Changed}更新）", saved ? "成功" : "失败",
                File.GetLastWriteTimeUtc(configPath) > before ? "已" : "未");
            host.Config.AutoSkipConfig.QuicklySkipConversationsEnabled = !value;
            host.LinuxConfig.LaunchTimeoutSeconds -= 1;
        }

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

    /// <summary>
    /// 无人值守：在“独立任务”页点击某个任务的启动按钮，运行一段时间后点击停止，等待任务结束（验证取消与按键释放）
    /// </summary>
    private static async Task RunSoloTaskUnattendedAsync(MainWindow window, MainWindowViewModel viewModel, string taskId, int stopAfterSeconds,
        string? dir, ILogger log)
    {
        viewModel.Navigate("独立任务");
        var card = viewModel.Tasks.Groups.SelectMany(g => g.Tasks).FirstOrDefault(t => t.TaskId == taskId);
        if (card is null)
        {
            log.Warning("独立任务页没有任务 {TaskId}", taskId);
            return;
        }

        await Task.Delay(500);
        log.Information("无人值守：启动独立任务 {Name}（{TaskId}），{Seconds} 秒后停止", card.DisplayName, taskId, stopAfterSeconds);
        await card.ToggleCommand.ExecuteAsync(null);
        await Task.Delay(TimeSpan.FromSeconds(stopAfterSeconds));
        await ScrollIntoViewAsync(window, card);
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
            SaveScreenshot(window, Path.Combine(dir, "avalonia-solo-running.png"), log);
        }

        log.Information("无人值守：停止独立任务，当前状态 {Status}", viewModel.Tasks.StatusText);
        await viewModel.Tasks.StopAsync();
        for (var i = 0; i < 100 && viewModel.Tasks.IsTaskActive; i++)
        {
            await Task.Delay(100);
        }

        log.Information("无人值守：独立任务已结束，状态 {Status}", viewModel.Tasks.StatusText);
        await Task.Delay(300);
        if (dir is not null)
        {
            SaveScreenshot(window, Path.Combine(dir, "avalonia-solo-stopped.png"), log);
        }
    }

    private static async Task ScrollIntoViewAsync(MainWindow window, object dataContext)
    {
        var target = window.GetVisualDescendants().OfType<Expander>().FirstOrDefault(e => ReferenceEquals(e.DataContext, dataContext));
        if (target is not null)
        {
            target.IsExpanded = true;
            await Task.Delay(200);
            target.BringIntoView();
            await Task.Delay(200);
        }
    }

    /// <summary>无人值守：打开“选择捕获窗口”对话框，截图后选中标题包含指定文字的窗口</summary>
    private static async Task SelectWindowUnattendedAsync(MainWindowViewModel viewModel, string title, string? dir, ILogger log)
    {
        var pick = viewModel.Home.SelectWindowAsync();
        await Task.Delay(800);
        var picker = AvaloniaPickers.CurrentWindowPicker;
        if (picker is null)
        {
            log.Warning("没有打开窗口选择对话框");
            return;
        }

        log.Information("窗口选择对话框列出 {Count} 个窗口：{Windows}", picker.Items.Count, string.Join(" | ", picker.Items.Select(w => w.Display)));
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
            SaveScreenshot(picker, Path.Combine(dir, "avalonia-window-picker.png"), log);
        }

        if (!picker.SelectAndConfirm(w => w.Title.Contains(title, StringComparison.Ordinal)))
        {
            log.Warning("没有标题包含 {Title} 的窗口", title);
            picker.Close(null);
        }

        await pick;
    }

    private static async Task SaveCapturedFrameAsync(GameHost host, string path, ILogger log)
    {
        var saved = new TaskCompletionSource();
        void OnFrame(OpenCvSharp.Mat frame)
        {
            if (saved.Task.IsCompleted)
            {
                return;
            }

            OpenCvSharp.Cv2.ImWrite(path, frame);
            saved.TrySetResult();
        }

        host.Dispatcher.FrameCaptured += OnFrame;
        try
        {
            await saved.Task.WaitAsync(TimeSpan.FromSeconds(3));
            log.Information("截图器画面：{Path}", path);
        }
        catch (TimeoutException)
        {
            log.Warning("3 秒内没有拿到截图器画面");
        }
        finally
        {
            host.Dispatcher.FrameCaptured -= OnFrame;
        }
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

/// <summary>一条龙完成后“关闭软件”等：在 UI 线程关闭桌面应用</summary>
internal sealed class AvaloniaAppLifetime(IClassicDesktopStyleApplicationLifetime desktop) : IAppLifetime
{
    public void Shutdown() => Dispatcher.UIThread.Post(() => desktop.Shutdown());
}

internal sealed record AvaloniaOptions(
    GameHostOptions Host,
    string? LogFile,
    string? Page,
    bool AutoStart,
    string? ScreenshotDirectory,
    int? ExitAfterSeconds,
    bool UiSelfTest,
    string? SelectWindowTitle,
    string? SoloTaskId = null,
    int SoloStopAfterSeconds = 4)
{
    public static AvaloniaOptions Parse(string[] args)
    {
        // 图形界面默认读写配置文件（User/config.json、User/linux-host.json），可用 --no-save-config 关闭
        var host = new GameHostOptions { PersistConfig = true };
        string? logFile = null, page = null, screenshots = null;
        var autoStart = false;
        var uiSelfTest = false;
        string? selectWindow = null;
        int? exitAfter = null;
        string? soloTask = null;
        var soloStopAfter = 4;
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
                case "--select-window": selectWindow = Next(); break;
                case "--solo-task": soloTask = Next(); break;
                case "--solo-stop-after": soloStopAfter = int.Parse(Next()); break;
                default: throw new ArgumentException($"未知参数 {args[i]}");
            }
        }

        return new AvaloniaOptions(host, logFile, page, autoStart, screenshots, exitAfter, uiSelfTest, selectWindow, soloTask, soloStopAfter);
    }
}
