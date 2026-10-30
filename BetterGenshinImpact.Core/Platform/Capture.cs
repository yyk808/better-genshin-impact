using OpenCvSharp;
using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Platform;

/// <summary>截图方式的能力描述，供上层选择截图方式和提示用户</summary>
/// <param name="Method">方式名，例如 BitBlt、WindowsGraphicsCapture、X11Shm</param>
/// <param name="CanCaptureOccluded">窗口被遮挡时是否仍能截到窗口自身内容；为 false 时上层需要先激活窗口并等待重绘</param>
/// <param name="NeedsUserConsent">是否需要用户授权（例如 Wayland ScreenCast portal）</param>
/// <param name="SupportsHdr">是否支持 HDR 画面</param>
public sealed record CaptureCapabilities(string Method, bool CanCaptureOccluded, bool NeedsUserConsent = false, bool SupportsHdr = false)
{
    public static CaptureCapabilities Unknown { get; } = new("Unknown", CanCaptureOccluded: false);
}

/// <summary>
/// 截图器。Start 接收平台窗口对象而不是 hWnd；帧为 Mat（BGRA 或 BGR）。
/// </summary>
public interface IGameCapture : IDisposable
{
    public bool IsCapturing { get; }

    /// <summary>能力描述。默认为 <see cref="CaptureCapabilities.Unknown"/></summary>
    public CaptureCapabilities Capabilities => CaptureCapabilities.Unknown;

    public void Start(IPlatformWindow window, Dictionary<string, object>? settings = null);

    public GameCaptureFrame? Capture();

    public void Stop();
}

/// <summary>一帧截图</summary>
public sealed class GameCaptureFrame : IDisposable
{
    public GameCaptureFrame(Mat frame, PixelRect? captureRect = null)
    {
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));
        CaptureRect = captureRect;
    }

    public Mat Frame { get; }

    /// <summary>截取区域在屏幕上的位置（游戏客户区）；实现不知道时为 null</summary>
    public PixelRect? CaptureRect { get; }

    /// <summary>
    /// 把一块 BGRA8888、按 <paramref name="stride"/> 存放的像素零拷贝包装成 Mat（CV_8UC4）。
    /// 调用方负责保证 <paramref name="data"/> 在 Mat 使用期间有效；需要独立数据时再 Clone
    /// </summary>
    public static GameCaptureFrame WrapBgra(nint data, int width, int height, int stride, PixelRect? captureRect = null)
        => new(Mat.FromPixelData(height, width, MatType.CV_8UC4, data, stride), captureRect);

    public void Dispose()
    {
        Frame.Dispose();
        GC.SuppressFinalize(this);
    }
}
