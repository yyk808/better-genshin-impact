using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Input.Backends.Win32;
using Fischless.GameCapture;
using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Platform.Windows;

/// <summary>Windows（含 Wine 中运行的 Windows 版）平台服务集合</summary>
public sealed class Win32PlatformServices(CaptureModes captureMode = CaptureModes.BitBlt) : IPlatformServices
{
    public string Name => Win32Environment.IsRunningOnWine ? $"Win32 (Wine {Win32Environment.WineVersion})" : "Win32";

    public IWindowManager Windows { get; } = new Win32WindowManager();

    public IHotkeyService Hotkeys { get; } = new Win32HotkeyService();

    public IGameCapture CreateCapture() => GameCaptureFactory.Create(captureMode);

    public IInputBackend CreateInput(IPlatformWindow window) => new Win32InputBackend(window.Handle);

    public PlatformProbeResult Probe()
    {
        var items = new Dictionary<string, string>
        {
            ["os"] = Environment.OSVersion.VersionString,
            ["wine"] = Win32Environment.WineVersion ?? "no",
            ["captureMode"] = captureMode.ToString(),
            ["input"] = "SendInput + PostMessage",
        };
        return new PlatformProbeResult(Win32Environment.Kind, items);
    }

    public void Dispose()
    {
        Windows.Dispose();
        Hotkeys.Dispose();
    }
}
