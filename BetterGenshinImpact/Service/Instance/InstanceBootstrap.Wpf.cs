using System;
using System.Diagnostics;
using System.Threading;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.Service.Instance;

/// <summary>实例启动的 WPF 部分：启动参数、网页版实例名互斥与 WinForms 启动提示。与平台无关的部分在 InstanceBootstrap.cs（Linux 版 Avalonia 共用）</summary>
public sealed partial class InstanceBootstrap
{
    private static partial StartupOptions ReadStartupOptions()
    {
        var options = CommandLineOptions.Instance;
        return new StartupOptions(options.HasExplicitInstanceType, options.InstanceType, options.RestartFromProcessId, options.InstanceName);
    }

    /// <summary>
    /// 网页版实例的实例名互斥体，进程存活期间一直持有，进程退出时由系统释放。
    /// 静态字段防止被 GC 回收；由启动线程（UI 线程）获取，线程一直存活
    /// </summary>
    private static Mutex? _webViewInstanceMutex;

    /// <summary>
    /// 校验网页版实例名并获取实例名互斥体。名称缺失或不合法、同名实例已在运行时提示并退出进程。
    /// 此时 WPF 尚未启动，提示使用 WinForms MessageBox（与 App 启动失败的兜底方式一致）
    /// </summary>
    private static partial string AcquireWebViewInstanceName(StartupOptions options)
    {
        if (!WebViewInstanceStore.TryNormalizeName(options.InstanceName, out var name, out var error))
        {
            ShowStartupMessage(
                $"网页版实例需要通过 {CommandLineOptions.InstanceNameArgument} 指定合法的实例名：{error}",
                System.Windows.Forms.MessageBoxIcon.Error);
            Environment.Exit(0xFFFF);
            throw new InvalidOperationException(error);
        }

        var mutex = new Mutex(initiallyOwned: false, WebViewInstanceStore.MutexName(name));
        bool acquired;
        try
        {
            // 应用内重启时旧进程还没退出，最多等它 15 秒（与 WaitForRestartSource 一致）
            acquired = mutex.WaitOne(options.RestartFromProcessId is null ? 0 : 15_000);
        }
        catch (AbandonedMutexException)
        {
            // 旧进程未释放就退出了，互斥体已归当前线程所有
            acquired = true;
        }

        if (!acquired)
        {
            mutex.Dispose();
            ShowStartupMessage($"网页版实例「{name}」已在运行。", System.Windows.Forms.MessageBoxIcon.Information);
            Environment.Exit(0);
            throw new InvalidOperationException($"网页版实例「{name}」已在运行。");
        }

        _webViewInstanceMutex = mutex;
        return name;
    }

    private static void ShowStartupMessage(string message, System.Windows.Forms.MessageBoxIcon icon)
    {
        try
        {
            System.Windows.Forms.MessageBox.Show(message, "BetterGI", System.Windows.Forms.MessageBoxButtons.OK, icon);
        }
        catch (Exception exception)
        {
            Trace.TraceError("显示启动提示失败：{0}，原提示：{1}", exception.Message, message);
        }
    }

}
