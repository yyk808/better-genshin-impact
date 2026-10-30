using System;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Platform.Windows;
using Fischless.GameCapture;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask;

/// <summary>Windows 主程序的临时截图实现（原 MusicGuidedSession.OpenGameFrame 中的逻辑，行为不变）</summary>
internal static class Win32GameFrameGrabber
{
    public static Mat? Grab(nint gameHandle, AllConfig config)
    {
        CaptureModes mode;
        try
        {
            mode = config.CaptureMode.ToCaptureMode();
        }
        catch (Exception)
        {
            mode = CaptureModes.BitBlt;
        }

        var capture = GameCaptureFactory.Create(mode);
        try
        {
            capture.Start(new Win32PlatformWindow(gameHandle), GameCaptureSettings.From(config));
            var frame = capture.Capture()?.Frame;
            return frame == null || frame.Empty() ? null : frame.Clone();
        }
        finally
        {
            capture.Dispose();
        }
    }
}
