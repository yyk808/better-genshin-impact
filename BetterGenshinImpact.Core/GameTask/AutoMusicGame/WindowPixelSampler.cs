using System;

namespace BetterGenshinImpact.GameTask.AutoMusicGame;

/// <summary>窗口某一点的像素颜色（取代 Win32 的 COLORREF）</summary>
public readonly record struct WindowPixelColor(byte R, byte G, byte B);

/// <summary>直接读取窗口客户区某一点颜色的宿主能力（自动音游的高频取色用，不经过截图管线）</summary>
public interface IWindowPixelSampler
{
    WindowPixelColor GetPixel(nint hWnd, int x, int y);
}

/// <summary>取色能力的宿主入口；Windows 主程序启动时设为 GDI 实现</summary>
public static class WindowPixelSampler
{
    public static IWindowPixelSampler Current { get; set; } = UnsupportedWindowPixelSampler.Instance;

    private sealed class UnsupportedWindowPixelSampler : IWindowPixelSampler
    {
        public static readonly UnsupportedWindowPixelSampler Instance = new();

        public WindowPixelColor GetPixel(nint hWnd, int x, int y)
            => throw new PlatformNotSupportedException("当前平台尚未实现窗口直接取色，自动音游不可用。");
    }
}
