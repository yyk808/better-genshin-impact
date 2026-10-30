using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Platform;
using OpenCvSharp;
using System;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 运行环境还未启动时临时抓一帧游戏画面（音游引导识别键位等使用）：建截图器、抓一帧、立即释放。
/// 截图器的创建方式取决于平台，由宿主设置 <see cref="Current"/>：
/// Windows 主程序沿用 GameCaptureFactory + Win32 窗口；Linux 宿主经 <see cref="IPlatformServices"/> 创建
/// </summary>
public static class GameFrameGrabber
{
    /// <summary>参数为游戏窗口句柄与配置根，返回的画面由调用方释放；未设置时不截图</summary>
    public static Func<nint, AllConfig, Mat?>? Current { get; set; }

    /// <summary>临时抓一帧；未设置宿主实现或句柄为 0 时返回 null</summary>
    public static Mat? Grab(nint handle, AllConfig config) => handle == 0 ? null : Current?.Invoke(handle, config);

    /// <summary>经平台服务临时抓一帧（供非 Windows 宿主使用）</summary>
    /// <param name="method">截图方式名，null 为平台默认</param>
    public static Mat? GrabWith(IPlatformServices platform, nint handle, AllConfig config, string? method = null)
    {
        using var window = platform.Windows.FromHandle(handle);
        if (window is null || !window.IsAlive)
        {
            return null;
        }

        using var capture = platform.CreateCapture(method);
        capture.Start(window, GameCaptureSettings.From(config));
        var frame = capture.Capture()?.Frame;
        return frame == null || frame.Empty() ? null : frame.Clone();
    }
}
