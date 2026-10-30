using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Input.Backends.Win32;
using Fischless.GameCapture;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Platform.Windows;

/// <summary>Windows（含 Wine 中运行的 Windows 版）平台服务集合</summary>
/// <param name="captureMode">CreateCapture 未指定方式时使用的截图方式</param>
/// <param name="logger">窗口查找日志</param>
public sealed class Win32PlatformServices(CaptureModes captureMode = CaptureModes.BitBlt, ILogger? logger = null) : IPlatformServices
{
    public string Name => Win32Environment.IsRunningOnWine ? $"Win32 (Wine {Win32Environment.WineVersion})" : "Win32";

    public IWindowManager Windows { get; } = new Win32WindowManager();

    public IHotkeyService Hotkeys { get; } = new Win32HotkeyService();

    public IGameWindowFinder GameWindows { get; } = new Win32GameWindowFinder(logger);

    public IKeyStateProvider KeyState => Win32KeyStateProvider.Instance;

    /// <summary>安装低级键鼠钩子；须在有消息循环的线程（UI 线程）上调用</summary>
    public IGlobalInputHook CreateGlobalInputHook() => new Win32GlobalInputHook();

    /// <summary>按方式名创建截图器；方式名无效时抛 <see cref="ArgumentException"/>（与 CaptureModeExtensions.ToCaptureMode 一致）</summary>
    public IGameCapture CreateCapture(string? method = null) =>
        GameCaptureFactory.Create(method is null ? captureMode : method.ToCaptureMode());

    public IInputBackend CreateInput(IPlatformWindow window) => new Win32InputBackend(window.Handle);

    public PlatformProbeResult Probe()
    {
        var items = new Dictionary<string, string>
        {
            ["os"] = Environment.OSVersion.VersionString,
            ["wine"] = Win32Environment.WineVersion ?? "no",
            ["captureMode"] = captureMode.ToString(),
            ["input"] = "SendInput + PostMessage",
            ["inputHook"] = "LowLevel hook (MouseKeyHook)",
        };
        return new PlatformProbeResult(Win32Environment.Kind, items);
    }

    public void Dispose()
    {
        Windows.Dispose();
        Hotkeys.Dispose();
    }
}
