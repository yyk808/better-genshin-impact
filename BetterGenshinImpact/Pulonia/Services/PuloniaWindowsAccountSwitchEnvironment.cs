using System;
using System.Diagnostics;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Input.Backends.Win32;
using BetterGenshinImpact.GameTask.Runtime;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>Windows 的账户切换运行环境检查：只支持 PC 官服本机窗口（Win32 输入后端、YuanShen 进程）。</summary>
internal sealed class PuloniaWindowsAccountSwitchEnvironment : IPuloniaAccountSwitchEnvironment
{
    /// <inheritdoc />
    public string? GetUnsupportedReason(GameRuntime runtime)
    {
        if (InputHub.Backend is not Win32InputBackend)
            return "自动切换账户只支持本机 PC 官服窗口，云原神实例不能切换账户";
        User32.GetWindowThreadProcessId(runtime.Window.Handle, out var processId);
        using var process = Process.GetProcessById((int)processId);
        // B 服同为 YuanShen 进程，但登录界面与官服模板不同，会在模板匹配阶段停止而不会误操作，因此不再读取 config.ini 区分渠道。
        return string.Equals(process.ProcessName, "YuanShen", StringComparison.OrdinalIgnoreCase)
            ? null
            : "自动切换账户只支持本机 PC 官服";
    }
}
