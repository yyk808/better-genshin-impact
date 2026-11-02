using BetterGenshinImpact.Platform.Linux.X11;

namespace BetterGenshinImpact.Platform.Linux.Wayland;

/// <summary>Linux 图形会话种类</summary>
public enum LinuxSessionKind
{
    /// <summary>X11 会话（或显式要求按 X11 运行）</summary>
    X11,

    /// <summary>Wayland 会话（WAYLAND_DISPLAY / XDG_SESSION_TYPE=wayland）</summary>
    Wayland,
}

/// <summary>
/// 运行时选择 Linux 平台后端：
/// <list type="bullet">
/// <item>设置了 BGI_LINUX_BACKEND=x11 / wayland 时按它选；</item>
/// <item>否则 WAYLAND_DISPLAY 非空或 XDG_SESSION_TYPE=wayland 时为 Wayland，其余（含只有 DISPLAY 的纯 X11 会话、Xvfb）为 X11。</item>
/// </list>
/// X11 会话下与以前完全相同（<see cref="X11PlatformServices"/>）
/// </summary>
public static class LinuxSession
{
    /// <summary>强制后端的环境变量</summary>
    public const string OverrideVariable = "BGI_LINUX_BACKEND";

    public static LinuxSessionKind Detect() => Detect(Environment.GetEnvironmentVariable);

    /// <summary>按给定的环境变量读取函数判断（便于测试）</summary>
    public static LinuxSessionKind Detect(Func<string, string?> env)
    {
        var forced = env(OverrideVariable)?.Trim().ToLowerInvariant();
        if (forced == "x11") return LinuxSessionKind.X11;
        if (forced == "wayland") return LinuxSessionKind.Wayland;
        if (!string.IsNullOrEmpty(env("WAYLAND_DISPLAY"))) return LinuxSessionKind.Wayland;
        return string.Equals(env("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)
            ? LinuxSessionKind.Wayland
            : LinuxSessionKind.X11;
    }

    /// <summary>按当前会话创建平台服务。X11 会话返回 <see cref="X11PlatformServices"/>（行为不变）</summary>
    /// <param name="display">X 显示名（X11 会话，或 Wayland 会话里 xwayland-satellite / XWayland 的显示）</param>
    public static IPlatformServices Create(string? display = null) =>
        Detect() == LinuxSessionKind.Wayland ? new WaylandPlatformServices(display) : new X11PlatformServices(display);

    /// <summary>平台服务中的 X11 部分（X11 会话本身，或 Wayland 会话里 xwayland-satellite / XWayland 的显示）；没有时为 null</summary>
    public static X11PlatformServices? X11Of(IPlatformServices platform) =>
        platform as X11PlatformServices ?? (platform as WaylandPlatformServices)?.X11;

    /// <summary>键鼠录制用的 XInput2 原始鼠标移动监听（Wayland 会话下监听 X 显示，只在 X 窗口有焦点时收到）</summary>
    public static X11RelativeMouseInputMonitor CreateRelativeMouseMonitor(IPlatformServices platform, string? display) =>
        X11Of(platform)?.CreateRelativeMouseMonitor() ?? new X11RelativeMouseInputMonitor(display);
}
