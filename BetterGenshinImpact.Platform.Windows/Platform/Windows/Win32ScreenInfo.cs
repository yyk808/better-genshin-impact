using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.Platform.Windows;

/// <summary>GetDeviceCaps(HORZRES / VERTRES) 实现的屏幕信息，与 <see cref="PrimaryScreen.WorkingArea"/> 完全相同</summary>
public sealed class Win32ScreenInfo : IScreenInfo
{
    public static Win32ScreenInfo Instance { get; } = new();

    public PixelSize PrimaryScreenSize
    {
        get
        {
            var s = PrimaryScreen.WorkingArea;
            return new PixelSize(s.Width, s.Height);
        }
    }
}
