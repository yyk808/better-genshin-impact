using BetterGenshinImpact.Core.Input;
using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Platform;

/// <summary>当前运行环境的种类</summary>
public enum PlatformKind
{
    /// <summary>原生 Windows</summary>
    Windows,

    /// <summary>Windows 版 BGI 跑在 Wine/Proton 里</summary>
    WindowsOnWine,

    /// <summary>Linux 原生，X11 / XWayland</summary>
    LinuxX11,

    /// <summary>Linux 原生，纯 Wayland（暂未实现）</summary>
    LinuxWayland,

    Unknown,
}

/// <summary>环境探测结果，用于诊断与给用户提示</summary>
/// <param name="Kind">运行环境</param>
/// <param name="Items">键值形式的探测项（X 扩展、窗口管理器、uinput 权限、会话类型、Wine 版本等）</param>
public sealed record PlatformProbeResult(PlatformKind Kind, IReadOnlyDictionary<string, string> Items);

/// <summary>环境探测</summary>
public interface IPlatformProbe
{
    PlatformProbeResult Probe();
}

/// <summary>
/// 一个平台的服务集合（运行时按平台选择实现）。
/// 输入后端绑定到具体窗口，所以用工厂方法创建
/// </summary>
public interface IPlatformServices : IPlatformProbe, IDisposable
{
    string Name { get; }

    IWindowManager Windows { get; }

    IHotkeyService Hotkeys { get; }

    /// <summary>按进程名 / 窗口类查找游戏窗口</summary>
    IGameWindowFinder GameWindows { get; }

    /// <summary>当前用户注册表（只读）：Windows 为 HKCU，Linux 为 Wine 前缀的 user.reg</summary>
    IUserRegistry UserRegistry { get; }

    /// <summary>系统（物理）键鼠状态</summary>
    IKeyStateProvider KeyState { get; }

    /// <summary>创建全局键鼠监听。Windows 实现须在 UI 线程上调用</summary>
    IGlobalInputHook CreateGlobalInputHook();

    /// <summary>创建截图器（未 Start）</summary>
    /// <param name="method">截图方式名（Windows：BitBlt / DwmGetDxSharedSurface / WindowsGraphicsCapture…；X11：ShmRoot / GetImageWindow…）；null 为平台默认</param>
    IGameCapture CreateCapture(string? method = null);

    /// <summary>创建绑定到 <paramref name="window"/> 的输入后端。<see cref="IInputBackend.Background"/> 可能为 null</summary>
    IInputBackend CreateInput(IPlatformWindow window);
}
