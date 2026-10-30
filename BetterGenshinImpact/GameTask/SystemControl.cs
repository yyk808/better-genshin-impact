using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.Runtime.Win32;
using BetterGenshinImpact.GameTask.Runtime.WebPage;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Platform.Windows;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 游戏进程与窗口相关的静态入口。
/// 平台无关部分在 Core 的 <see cref="GenshinGameProcess"/>；找窗走 <see cref="IPlatformServices.GameWindows"/>；
/// 按句柄操作窗口的 Win32 实现在 Platform.Windows 的 <see cref="Win32WindowOps"/>，这里保留转发以兼容现有调用
/// </summary>
public class SystemControl
{
    private static readonly ILogger Logger = CoreServices.GetLogger<SystemControl>();

    private static readonly Lazy<IPlatformServices> FallbackPlatform =
        new(() => new Win32PlatformServices(logger: Logger));

    /// <summary>
    /// 平台服务：优先取 DI 中注册的实例，宿主未建立（单元测试等）时使用本地实例
    /// </summary>
    public static IPlatformServices Platform => CoreServices.GetService<IPlatformServices>() ?? FallbackPlatform.Value;

    /// <summary>
    /// 配置根。SystemControl 是静态的 Win32 函数集合，没有创建方可以传参，暂时读静态入口（见 task-context-removal.md 第 5 节）
    /// </summary>
    private static AllConfig Config => ConfigService.Config!;

    /// <summary>
    /// 游戏进程名列表：传入已绑定的 Win32 游戏窗口时只返回该窗口的进程名，否则返回候选进程名（含安装路径中的自定义 exe 名）
    /// </summary>
    /// <param name="boundWindow">已绑定的游戏窗口；还没有运行环境（例如找窗）时传 null</param>
    public static List<string> GetGenshinGameProcessNameList(IGameWindow? boundWindow)
    {
        if (boundWindow is Win32GameWindow window && boundWindow is not WebPageGameWindow)
        {
            return [window.ProcessName];
        }

        string? installPath = null;
        try
        {
            installPath = Config.GenshinStartConfig.InstallPath;
        }
        catch
        {
            /* ignore */
        }

        return GenshinGameProcess.CandidateProcessNames(installPath);
    }

    /// <summary>当前设置下的找窗条件</summary>
    public static GameWindowSearch GetGameWindowSearch() =>
        new(GetGenshinGameProcessNameList(null), Config.OtherConfig.WindowClassDetectPreferred);

    /// <summary>
    /// 查找游戏窗口（平台无关）。找不到返回 null，调用方负责 Dispose
    /// </summary>
    public static IPlatformWindow? FindGenshinImpactWindow() => Platform.GameWindows.FindGameWindow(GetGameWindowSearch());

    /// <summary>
    /// 查找游戏窗口句柄，找不到返回 0。
    /// 其他设置“窗口类名优先检测”关闭时按进程名 + MainWindowHandle；开启后 ①按窗口类名枚举 → ②按进程枚举最大可见窗口 → ③原版方式
    /// </summary>
    public static nint FindGenshinImpactHandle()
    {
        // 找窗时还没有运行环境，按候选进程名查找
        var search = GetGameWindowSearch();
        if (Platform.GameWindows is Win32GameWindowFinder win32)
        {
            return win32.FindHandle(search);
        }

        using var window = Platform.GameWindows.FindGameWindow(search);
        return window?.Handle ?? 0;
    }

    public static async Task<nint> StartFromLocalAsync(string path)
    {
        if (!File.Exists(path))
        {
            await UiHost.Dialogs.ErrorAsync($"原神启动路径 {path} 不存在，请前往 启动——同时启动原神——原神安装路径 重新进行配置！");
            return IntPtr.Zero;
        }

        var cfg = Config.GenshinStartConfig;
        var workdir = Path.GetDirectoryName(path) ?? "";
        var arg = GenshinGameProcess.BuildStartArguments(
            cfg.GenshinStartArgs,
            InstanceBootstrap.Current.Context.InstanceType == BetterGiInstanceType.ChildSession);

        if (cfg.StartGameWithCmd)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"\" /d \"{workdir}\" \"{path}\" {arg}",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
        }
        else
        {
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                Arguments = arg,
                WorkingDirectory = workdir
            });
        }

        for (var i = 0; i < 5; i++)
        {
            var handle = FindGenshinImpactHandle();
            if (handle != 0)
            {
                await Task.Delay(2333);
                handle = FindGenshinImpactHandle();
                await Task.Delay(2577);
                return handle;
            }

            await Task.Delay(5577);
        }

        return FindGenshinImpactHandle();
    }

    internal static string BuildGenshinStartArguments(string? configuredArguments, bool isChildSession) =>
        GenshinGameProcess.BuildStartArguments(configuredArguments, isChildSession);

    public static string GetActiveByProcess()
    {
        return GetActiveProcessName() ?? "Unknown";
    }

    /// <summary>
    /// 游戏是否在前台，用于键鼠钩子类热键的门控。
    /// 有运行环境时看它的窗口；没有时按前台窗口的进程名判断
    /// </summary>
    /// <param name="window">当前运行环境的游戏窗口，截图器未启动时为 null</param>
    public static bool IsGameForeground(IGameWindow? window)
    {
        if (window is not null)
        {
            return window.IsForeground;
        }

        var name = GetActiveProcessName();
        return !string.IsNullOrEmpty(name)
               && GetGenshinGameProcessNameList(null).Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
    }

    // ---- 以下为 Win32WindowOps 的转发，行为不变 ----

    public static nint GetForegroundWindowHandle() => Win32WindowOps.GetForegroundWindowHandle();

    public static nint FindHandleByProcessName(params string[] names) => Win32GameWindowFinder.FindHandleByProcessName(names);

    public static nint FindHandleByWindowName() => Win32WindowOps.FindHandleByWindowName();

    public static string? GetActiveProcessName() => Win32WindowOps.GetActiveProcessName();

    public static Process? GetProcessByHandle(nint hWnd) => Win32WindowOps.GetProcessByHandle(hWnd);

    /// <summary>获取窗口位置</summary>
    public static RECT GetWindowRect(nint hWnd) => Win32WindowOps.GetWindowRect(hWnd);

    /// <summary>游戏本身分辨率获取</summary>
    public static RECT GetGameScreenRect(nint hWnd) => Win32WindowOps.GetGameScreenRect(hWnd);

    /// <summary>GetWindowRect or GetGameScreenRect</summary>
    public static RECT GetCaptureRect(nint hWnd) => Win32WindowOps.GetCaptureRect(hWnd);

    public static void ActivateWindow(nint hWnd) => Win32WindowOps.ActivateWindow(hWnd);

    public static void FocusWindow(nint hWnd) => Win32WindowOps.FocusWindow(hWnd);

    public static void MinimizeAndActivateWindow(nint hWnd) => Win32WindowOps.MinimizeAndActivateWindow(hWnd);

    public static void RestoreWindow(nint hWnd) => Win32WindowOps.RestoreWindow(hWnd);

    public static bool IsFullScreenMode(IntPtr hWnd) => Win32WindowOps.IsFullScreenMode(hWnd);

    public static void RestartApplication(string[] newArgs, bool graceful = false)
    {
        // 获取当前程序路径
        using var currentProcess = Process.GetCurrentProcess();
        string exePath = currentProcess.MainModule!.FileName;

        // 构建参数字符串
        var restartArgs = new List<string>(newArgs);
        var instanceType = InstanceBootstrap.Current.Context.InstanceType;
        if (instanceType == BetterGiInstanceType.ChildSession)
        {
            restartArgs.Add(CommandLineOptions.InstanceArgument);
            restartArgs.Add("childSession");
        }
        else if (instanceType == BetterGiInstanceType.WebView)
        {
            restartArgs.Add(CommandLineOptions.InstanceArgument);
            restartArgs.Add("webview");
            restartArgs.Add(CommandLineOptions.InstanceNameArgument);
            restartArgs.Add(InstanceBootstrap.Current.Context.InstanceName ?? string.Empty);
        }
        restartArgs.Add(CommandLineOptions.RestartFromProcessIdArgument);
        restartArgs.Add(Environment.ProcessId.ToString());

        // 新进程会立即读取 config.json，先写入防抖窗口内尚未落盘的配置改动
        CoreServices.GetService<IConfigService>()?.Flush();

        // 启动新进程
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false
        };
        foreach (var argument in restartArgs)
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var restartedProcess = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动重启后的程序。");

        // 关闭当前程序
        if (graceful)
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => System.Windows.Application.Current.Shutdown()));
        else
            Environment.Exit(0);
    }
    // private static void StartFromLauncher(string path)
    // {
    //     // 通过launcher启动
    //     var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    //     Thread.Sleep(1000);
    //     // 获取launcher窗口句柄
    //     var hWnd = FindHandleByProcessName("launcher");
    //     var rect = GetWindowRect(hWnd);
    //     var dpiScale = Helpers.DpiHelper.ScaleY;
    //     // 对于launcher，启动按钮的位置时固定的，在launcher窗口的右下角
    //     Thread.Sleep(1000);
    //     Simulation.MouseEvent.Click((int)((float)rect.right * dpiScale) - (rect.Width / 5), (int)((float)rect.bottom * dpiScale) - (rect.Height / 8));
    // }
    //
    // private static void StartCloudYaunShen(string path)
    // {
    //     // 通过launcher启动
    //     var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    //     Thread.Sleep(10000);
    //     // 获取launcher窗口句柄
    //     var hWnd = FindHandleByProcessName("Genshin Impact Cloud Game");
    //     var rect = GetWindowRect(hWnd);
    //     var dpiScale = Helpers.DpiHelper.ScaleY;
    //     // 对于launcher，启动按钮的位置时固定的，在launcher窗口的右下角
    //     Simulation.MouseEvent.Click(rect.right - (rect.Width / 6), rect.bottom - (rect.Height / 13 * 3));
    //     // TODO：点完之后有个15s的倒计时，好像不处理也没什么问题，直接睡个20s吧
    //     Thread.Sleep(20000);
    // }
    /// <summary>
    /// 关闭当前运行环境的游戏。保留为静态入口：OneDragonFlowViewModel 也会被直接 new，无法构造注入
    /// </summary>
    public static void CloseGame()
    {
        var service = CoreServices.GetService<GameRuntimeService>();
        if (service != null)
        {
            service.CloseGame();
        }
        else
        {
            CloseGameProcesses(null);
        }
    }

    /// <summary>
    /// 结束同一 Windows Session 中的原神进程，由 Win32RuntimeProvider 调用
    /// </summary>
    /// <param name="boundWindow">当前绑定的游戏窗口：有值时只结束该窗口的进程名，否则按候选进程名</param>
    public static void CloseGameProcesses(IGameWindow? boundWindow)
    {
        try
        {
            var currentSessionId = Process.GetCurrentProcess().SessionId;
            var processNames = GetGenshinGameProcessNameList(boundWindow);
            var processes = new List<Process>();
            foreach (var name in processNames)
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        if (p.SessionId == currentSessionId)
                            processes.Add(p);
                        else
                            p.Dispose();
                    }
                    catch (InvalidOperationException)
                    {
                        p.Dispose();
                    }
                }
            }
            var targets = processes.GroupBy(p => p.Id).Select(g => g.First()).ToArray();

            if (targets.Length > 0)
            {
                foreach (var process in targets)
                {
                    try
                    {
                        // 尝试正常关闭进程
                        process.CloseMainWindow();
                        
                        // 给进程一些时间来响应关闭请求
                        if (!process.WaitForExit(5000))
                        {
                            // 如果进程没有在5秒内关闭，则强制终止它
                            process.Kill();
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"关闭游戏进程时出错: {ex.Message}");
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CloseGame方法执行出错: {ex.Message}");
        }
    }

    public static void Shutdown()
    {
        try
        {
            // 使用Windows API安全关闭系统
            // 这里使用的是标准的Windows关机命令，需要适当的权限
            Process.Start("shutdown", "/s /t 60 /c \"系统将在60秒后关闭，请保存您的工作。\"");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Shutdown方法执行出错: {ex.Message}");
        }
    }
}
