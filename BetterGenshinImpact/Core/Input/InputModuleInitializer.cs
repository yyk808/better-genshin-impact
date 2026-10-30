using BetterGenshinImpact.Core.Input.Backends.Win32;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Platform.Windows;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.CompilerServices;

namespace BetterGenshinImpact.Core.Input;

internal static class InputModuleInitializer
{
    /// <summary>
    /// InputChannelBase 已移入 BetterGenshinImpact.Core，不能直接访问 App；
    /// 主程序集加载时把日志来源接回 App.GetLogger，与迁移前的懒加载行为一致（首次记录日志时才解析）
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        InputChannelBase.LoggerFactory ??= static () => CoreServices.GetLogger<InputChannelBase>();

        // InputHub、Region 等已移入 Core，Windows 主程序的默认实现在这里接回：
        // 未 Attach 时是未绑定窗口的 Win32 后端（前台可用，后台 warn），光标 / 屏幕大小走 User32 / Gdi32，与迁移前相同
        CoreServices.SetProvider(static () => App.ServiceProvider, static () => System.Windows.Application.Current is App);
        InputHub.DefaultBackendFactory ??= static () => new Win32InputBackend(IntPtr.Zero);
        PlatformHost.KeyState = Win32KeyStateProvider.Instance;
        PlatformHost.Screen = Win32ScreenInfo.Instance;
        BetterGenshinImpact.GameTask.GameSystem.Control = BetterGenshinImpact.GameTask.SystemControlAdapter.Instance;
        BetterGenshinImpact.Core.Mask.MaskColorParser.ColorTextConverter = static text =>
            BetterGenshinImpact.Core.Mask.MaskWpfConversions.ToMaskColor((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(text));
        BetterGenshinImpact.GameTask.HotkeyHoldState.Query = BetterGenshinImpact.Model.HoldHotkeyQuery.IsHotkeyPressed;
        BetterGenshinImpact.GameTask.RunnerSuspend.Provider = static () => BetterGenshinImpact.GameTask.RunnerContext.Instance;
        BetterGenshinImpact.GameTask.AutoFight.Model.ESkillClassifyMonitor.Sink =
            static result => BetterGenshinImpact.ViewModel.Windows.ESkillClassifyViewModel.Instance.Result = result;
        BetterGenshinImpact.GameTask.AutoSkip.Audio.ProcessAudioCapture.Factory =
            static pid => new BetterGenshinImpact.GameTask.AutoSkip.Audio.ProcessLoopbackAudioCapture(pid);
        PlatformHost.IsMicaBackdropSupported = static () => BetterGenshinImpact.Helpers.OsVersionHelper.IsWindows11_22523_OrGreater;
    }
}
