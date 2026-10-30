using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.AutoMusicGame;

/// <summary>Windows 下的窗口取色：与迁移前 AutoMusicGameTask 中的调用序列完全相同（GetDC → GetPixel → DeleteDC）</summary>
public sealed class GdiWindowPixelSampler : IWindowPixelSampler
{
    public static readonly GdiWindowPixelSampler Instance = new();

    public WindowPixelColor GetPixel(nint hWnd, int x, int y)
    {
        var hdc = User32.GetDC(hWnd);
        var c = Gdi32.GetPixel(hdc, x, y);
        Gdi32.DeleteDC(hdc);
        return new WindowPixelColor(c.R, c.G, c.B);
    }
}
