namespace BetterGenshinImpact.Service.Instance;

/// <summary>
/// 实例启动的 Linux 部分：启动参数由宿主设置；没有网页版（云原神 WebView）子实例。
/// “Session”是 Unix 会话（getsid）：同一会话里再次启动转发给已运行的实例并退出，用 setsid 在独立会话里启动（通常配独立显示和 Wine 前缀）的是另一个实例。
/// </summary>
public sealed partial class InstanceBootstrap
{
    /// <summary>宿主在 <see cref="Initialize"/> 前设置的启动参数</summary>
    public static bool HasExplicitChildSessionType { get; set; }

    /// <summary>启动参数里的 --restart-from 进程号</summary>
    public static int? StartupRestartFromProcessId { get; set; }

    /// <summary>宿主调用：建立根实例管道或连接已有根实例（同一会话重复启动时转发参数后退出进程）</summary>
    public static void InitializeForHost() => Initialize();

    private static partial StartupOptions ReadStartupOptions() => new(
        HasExplicitChildSessionType,
        HasExplicitChildSessionType ? BetterGiInstanceType.ChildSession : BetterGiInstanceType.Primary,
        StartupRestartFromProcessId,
        null);

    private static partial string AcquireWebViewInstanceName(StartupOptions options)
        => throw new PlatformNotSupportedException("Linux 版没有网页版（云原神 WebView）子实例。");
}
