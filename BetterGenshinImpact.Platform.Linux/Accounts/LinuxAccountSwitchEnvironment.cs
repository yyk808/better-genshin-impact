using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Platform.Linux.Wayland;
using BetterGenshinImpact.Platform.Linux.X11;
using BetterGenshinImpact.Pulonia.Services;

namespace BetterGenshinImpact.Platform.Linux.Accounts;

/// <summary>
/// Linux 的账户切换运行环境检查（对应 Windows 的“Win32 输入后端 + YuanShen 进程”）：
/// 输入必须是本机键鼠后端（X11 / XWayland 窗口为 XTest，niri 原生窗口为 uinput），云原神（网页 SDK 输入）排除；
/// 游戏窗口的进程号（X 窗口 <c>_NET_WM_PID</c>，niri 原生窗口为 IPC 的 pid）对应的进程必须是 Wine / Proton 下的 YuanShen.exe。
/// </summary>
public sealed class LinuxAccountSwitchEnvironment : IPuloniaAccountSwitchEnvironment
{
    /// <summary>/proc 根目录（测试可替换）</summary>
    public string ProcRoot { get; init; } = "/proc";

    /// <inheritdoc />
    public string? GetUnsupportedReason(GameRuntime runtime)
    {
        if (InputHub.Backend is not (XTestInputBackend or UinputInputBackend))
            return "自动切换账户只支持本机 PC 官服窗口，云原神实例不能切换账户";
        var pid = runtime.Window.ProcessId;
        if (pid <= 0)
            return "拿不到游戏窗口的进程号（X 窗口没有 _NET_WM_PID），无法确认是 PC 官服";
        return CheckGenshinProcess(pid, ProcRoot);
    }

    /// <summary>
    /// 判断进程是不是 Wine / Proton 下的 YuanShen.exe：/proc/&lt;pid&gt;/cmdline 的第一个参数（Wine 改写为 Windows 路径）文件名为 YuanShen.exe，
    /// 且 /proc/&lt;pid&gt;/exe 指向 wine / wine64 / wine(64)-preloader（读不到 exe 链接时，以 cmdline 是 Windows 路径为准）。
    /// </summary>
    /// <returns>可以切换时为 null，否则为原因</returns>
    public static string? CheckGenshinProcess(int pid, string procRoot = "/proc")
    {
        string cmdline;
        try { cmdline = File.ReadAllText(Path.Combine(procRoot, pid.ToString(), "cmdline")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"游戏进程 {pid} 已不存在或无法读取（{e.Message}）";
        }

        var argv0 = cmdline.Split('\0')[0];
        var name = argv0[(argv0.LastIndexOfAny(['/', '\\']) + 1)..];
        string? exe = null;
        try { exe = new FileInfo(Path.Combine(procRoot, pid.ToString(), "exe")).LinkTarget; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        var exeName = exe is null ? null : Path.GetFileName(exe);
        var isWine = exeName is not null
            ? exeName.StartsWith("wine", StringComparison.OrdinalIgnoreCase)
            : argv0.Length > 2 && (argv0[1] == ':' || argv0.Contains('\\'));
        if (!isWine)
            return $"游戏进程不是 Wine / Proton 进程（{exeName ?? argv0}）：自动切换账户只支持在 Wine / Proton 中运行的 PC 官服";
        // B 服同为 YuanShen 进程，但登录界面与官服模板不同，会在模板匹配阶段停止而不会误操作（与 Windows 版相同）
        return string.Equals(name, "YuanShen.exe", StringComparison.OrdinalIgnoreCase)
            ? null
            : $"自动切换账户只支持本机 PC 官服（游戏进程是 {name}，不是 YuanShen.exe）";
    }
}
