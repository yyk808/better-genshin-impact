using BetterGenshinImpact.Pulonia.Models;
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
//                    [--solo-task 任务标识] [--solo-stop-after 秒] [--script-group 配置组名称] [--script-stop-after 秒] [--notify-test]
//                    [--task-plan 计划] [--ui-test script-settings|taskplan]
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
                services =>
                {
                    services.AddSingleton<ITaskSettingsSource>(_ => viewModel!.Tasks);
                    // 自动配音设置窗口的界面能力（UI 线程投递、计时器、确认框、文件选择）
                    services.AddSingleton<AvaloniaQuestVoiceSettingsUi>();
                    services.AddSingleton<BetterGenshinImpact.ViewModel.Pages.IQuestVoiceSettingsUi>(sp => sp.GetRequiredService<AvaloniaQuestVoiceSettingsUi>());
                    // 树脂设置对话框（专属参数编辑器的“设置”按钮）
                    services.AddSingleton<BetterGenshinImpact.ViewModel.Resin.IResinSettingsDialogs, AvaloniaResinSettingsDialogs>();
                    services.AddSingleton<BetterGenshinImpact.ViewModel.SoloTaskSettings.IResinPlanSettingsDialogs, AvaloniaResinPlanSettingsDialogs>();
                    // 云原神（网页版）窗口：WebKitGTK
                    services.AddSingleton<ICloudGamePageHost, GtkCloudGameHost>();
                });
        }
        catch (Exception e)
        {
            hostError = e.Message;
            log.Error(e, "平台服务初始化失败（是否设置了 DISPLAY / 能否连接 X 服务器？）");
        }

        // 软件日志文件（log 目录）：按 Linux 宿主设置启用；平台初始化失败时不写
        if (host is not null)
        {
            LogFiles.Attach(host.LinuxConfig);
        }
        else
        {
            HostLogging.FileLog.Disable();
        }

        // 遮罩窗口（透明、置顶、点击穿透），截图器运行且启用遮罩时显示
        MaskWindowController? mask = null;
        if (host is not null)
        {
            mask = new MaskWindowController(host, options.Host.Display, log);
        }

        // 脚本 HTML 遮罩（WebKitGTK 透明窗口），任务结束时由 Core 关闭
        GtkHtmlMaskWindowHost? htmlMask = null;
        if (host is not null)
        {
            htmlMask = new GtkHtmlMaskWindowHost(host, log);
            BetterGenshinImpact.Core.Script.Dependence.HtmlMaskWindowHost.Current = htmlMask;
        }

        viewModel = new MainWindowViewModel(host, hostError, log);
        window = new MainWindow { DataContext = viewModel };
        desktop.MainWindow = window;
        desktop.Exit += (_, _) =>
        {
            mask?.Dispose();
            htmlMask?.Dispose();
            // 注销快捷键（X11 XGrabKey），再释放平台连接
            viewModel?.Settings.Dispose();
            if (host is not null)
            {
                // 退出时（关闭主窗口或 Shutdown）停止截图器并释放平台连接（X11），避免输入线程使用已关闭的连接
                // 释放在线程池上进行，同时处理 UI 线程上排队的任务：任务计划服务等的收尾续接可能投递到 UI 线程，
                // 直接在 UI 线程上同步等待会互相等待，直到被外部超时结束
                var disposing = Task.Run(async () => await host.DisposeAsync());
                while (!disposing.Wait(20))
                {
                    Dispatcher.UIThread.RunJobs();
                }

                disposing.GetAwaiter().GetResult();
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

            if (options.ScriptGroup is { } scriptGroup && host is not null)
            {
                await RunScriptGroupUnattendedAsync(window, viewModel, scriptGroup, options.ScriptStopAfterSeconds, options.ScreenshotDirectory, log);
            }

            if (options.TaskPlan is { } taskPlan && host is not null)
            {
                await RunTaskPlanUnattendedAsync(window, viewModel, taskPlan, options.ScriptStopAfterSeconds, options.ScreenshotDirectory, log);
            }

            if (options.NotifyTest && host is not null)
            {
                // 无人值守：经设置页的“发送测试通知”发送一条测试通知，结果写入日志
                await viewModel.Settings.SendTestNotificationAsync();
            }

            if (options.UiTest is { } uiTest)
            {
                await new UiTests(window, viewModel, log, options.ScreenshotDirectory).RunAsync(uiTest);
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
            // 按导航项依次截图（与 WPF 版导航栏相同的页面；全自动的子项、快捷键、通知为同一页面的不同选项卡）
            foreach (var (page, file) in new[]
                     {
                         ("实时触发", "avalonia-trigger.png"), ("环境", "avalonia-environment.png"), ("独立任务", "avalonia-tasks.png"),
                         ("任务计划", "avalonia-taskplan.png"), ("一条龙", "avalonia-onedragon.png"),
                         ("JS 脚本", "avalonia-auto-js.png"), ("地图追踪", "avalonia-auto-pathing.png"), ("录制回放", "avalonia-auto-record.png"),
                         ("全自动", "avalonia-auto.png"), ("快捷键", "avalonia-settings-hotkey.png"), ("通知", "avalonia-settings-notify.png"),
                         ("新一条龙", "avalonia-placeholder.png"), ("设置", "avalonia-settings.png"), ("启动", "avalonia-home.png"),
                     })
            {
                viewModel.Navigate(page);
                await Task.Delay(600);
                SaveScreenshot(window, Path.Combine(dir, file), log);
            }

            viewModel.Navigate("启动");
            await Task.Delay(300);
            // 启动页下半部分：展开“同时启动原神”，滚动到底部
            foreach (var expander in window.GetVisualDescendants().OfType<Expander>())
            {
                expander.IsExpanded = true;
            }

            await Task.Delay(400);
            window.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(s => s.Extent.Height > s.Viewport.Height)?.ScrollToEnd();
            await Task.Delay(400);
            SaveScreenshot(window, Path.Combine(dir, "avalonia-home-launch.png"), log);

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
        var box = FluentMessageBox.Current;
        if (box is null)
        {
            log.Warning("UiHost 自检：没有找到消息框");
            return;
        }

        if (dir is not null && desktop.MainWindow is { } main)
        {
            Directory.CreateDirectory(dir);
            // 消息框为主窗口覆盖层上的 ContentDialog，与轻提示一起截主窗口
            SaveScreenshot(main, Path.Combine(dir, "avalonia-messagebox.png"), log);
            SaveScreenshot(main, Path.Combine(dir, "avalonia-toast.png"), log);
        }

        box.Hide(FluentAvalonia.UI.Controls.FAContentDialogResult.Primary);
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

    /// <summary>无人值守：切到任务计划页，选中配置组并点击运行；到时（大于 0 时）点击停止，等待结束</summary>
    private static async Task RunScriptGroupUnattendedAsync(MainWindow window, MainWindowViewModel viewModel, string groupName, int stopAfterSeconds,
        string? dir, ILogger log)
    {
        viewModel.Navigate("全自动");
        viewModel.Auto.SelectedTab = 0;
        var page = viewModel.ScriptControl;
        page.Reload();
        var group = page.ScriptGroups.FirstOrDefault(g => g.Name == groupName);
        if (group is null)
        {
            log.Warning("任务计划页没有配置组 {Name}", groupName);
            return;
        }

        page.SelectedScriptGroup = group;
        await Task.Delay(500);
        log.Information("无人值守：运行配置组 {Name}，{Seconds} 秒后停止", groupName, stopAfterSeconds);
        var run = page.StartScriptGroup();
        var waited = 0;
        while (!run.IsCompleted && (stopAfterSeconds <= 0 || waited < stopAfterSeconds * 10))
        {
            await Task.Delay(100);
            waited++;
        }

        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
            SaveScreenshot(window, Path.Combine(dir, "avalonia-script-running.png"), log);
        }

        if (!run.IsCompleted)
        {
            log.Information("无人值守：停止配置组，当前状态 {Status}", page.StatusText);
            page.Stop();
        }

        await run;
        log.Information("无人值守：配置组已结束，状态 {Status}", page.StatusText);
        await Task.Delay(300);
        if (dir is not null)
        {
            SaveScreenshot(window, Path.Combine(dir, "avalonia-script-stopped.png"), log);
        }
    }

    /// <summary>无人值守：切到任务计划页，选中计划并点击运行；到时（大于 0 时）点击停止，等待运行结束</summary>
    private static async Task RunTaskPlanUnattendedAsync(MainWindow window, MainWindowViewModel viewModel, string planName, int stopAfterSeconds,
        string? dir, ILogger log)
    {
        viewModel.Navigate("任务计划");
        var page = viewModel.TaskPlan;
        await page.ReloadAsync();
        var plan = page.Plans.FirstOrDefault(p => p.Name == planName || p.Plan.Id == planName);
        if (plan is null)
        {
            log.Warning("任务计划页没有计划 {Name}", planName);
            return;
        }

        page.SelectedPlan = plan;
        await Task.Delay(500);
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
            SaveScreenshot(window, Path.Combine(dir, "avalonia-taskplan.png"), log);
        }

        log.Information("无人值守：运行任务计划 {Name}，{Seconds} 秒后停止", planName, stopAfterSeconds);
        if (await page.RunPlanAsync(plan) is not { } runId)
        {
            return;
        }

        using var cts = new CancellationTokenSource();
        var wait = page.WaitForRunAsync(runId, cts.Token);
        var waited = 0;
        while (!wait.IsCompleted && (stopAfterSeconds <= 0 || waited < stopAfterSeconds * 10))
        {
            await Task.Delay(100);
            waited++;
        }

        if (dir is not null)
        {
            SaveScreenshot(window, Path.Combine(dir, "avalonia-taskplan-running.png"), log);
        }

        if (!wait.IsCompleted)
        {
            log.Information("无人值守：点击停止任务计划");
            await page.StopPlanAsync();
        }

        var result = await wait;
        log.Information("无人值守：任务计划已结束，状态 {Status}，{Message}", result is null ? "?" : PuloniaTaskRunStatusText.Get(result.Status), result?.Message);
        await Task.Delay(500);
        page.SelectedTab = 1;
        await Task.Delay(500);
        if (dir is not null)
        {
            SaveScreenshot(window, Path.Combine(dir, "avalonia-taskplan-history.png"), log);
        }

        page.SelectedTab = 0;
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

    internal static void SaveScreenshotPublic(Window window, string path, ILogger log) => SaveScreenshot(window, path, log);

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
    int SoloStopAfterSeconds = 4,
    string? ScriptGroup = null,
    int ScriptStopAfterSeconds = 0,
    bool NotifyTest = false,
    string? TaskPlan = null,
    string? UiTest = null)
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
        string? scriptGroup = null;
        var scriptStopAfter = 0;
        var notifyTest = false;
        string? taskPlan = null;
        string? uiTest = null;
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
                case "--script-group": scriptGroup = Next(); break;
                case "--script-stop-after": scriptStopAfter = int.Parse(Next()); break;
                case "--notify-test": notifyTest = true; break;
                // 无人值守运行任务计划（按名称），停止时间沿用 --script-stop-after
                case "--task-plan": taskPlan = Next(); break;
                // 宿主内界面自测：script-settings（脚本设置）或 taskplan（任务计划编辑），点击控件后读取磁盘文件核对
                case "--ui-test": uiTest = Next(); break;
                default: throw new ArgumentException($"未知参数 {args[i]}");
            }
        }

        return new AvaloniaOptions(host, logFile, page, autoStart, screenshots, exitAfter, uiSelfTest, selectWindow, soloTask, soloStopAfter, scriptGroup,
            scriptStopAfter, notifyTest, taskPlan, uiTest);
    }
}
