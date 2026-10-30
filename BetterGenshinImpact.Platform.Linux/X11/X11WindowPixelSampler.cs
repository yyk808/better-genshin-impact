using BetterGenshinImpact.GameTask.AutoMusicGame;
using BetterGenshinImpact.Platform.Linux.X11.Native;
using static BetterGenshinImpact.Platform.Linux.X11.Native.Xlib;

namespace BetterGenshinImpact.Platform.Linux.X11;

/// <summary>
/// X11 下的窗口取色（对应 Windows 的 GdiWindowPixelSampler）：对游戏窗口做 1x1 的 XGetImage。
/// 坐标是窗口客户区坐标；读取失败（窗口不可见、越界等）时返回白色，即“无音符”，不会误按键
/// </summary>
public sealed unsafe class X11WindowPixelSampler : IWindowPixelSampler, IDisposable
{
    private static readonly WindowPixelColor Fallback = new(255, 255, 255);
    private readonly string? _displayName;
    private X11Connection? _c;

    public X11WindowPixelSampler(string? displayName = null)
    {
        _displayName = displayName;
    }

    public WindowPixelColor GetPixel(nint hWnd, int x, int y)
    {
        if (hWnd == 0 || x < 0 || y < 0) return Fallback;
        var c = _c ??= new X11Connection(_displayName);
        lock (c.Gate)
        {
            var img = XGetImage(c.Display, hWnd, x, y, 1, 1, AllPlanes, ZPixmap);
            if (img == 0) return Fallback;
            try
            {
                if (XImageLayout.BitsPerPixel(img) != 32) return Fallback;
                // TrueColor 32bpp 小端：B G R X
                var p = (byte*)XImageLayout.Data(img);
                return new WindowPixelColor(p[2], p[1], p[0]);
            }
            finally
            {
                XDestroyImage(img);
            }
        }
    }

    public void Dispose()
    {
        _c?.Dispose();
        _c = null;
    }
}
