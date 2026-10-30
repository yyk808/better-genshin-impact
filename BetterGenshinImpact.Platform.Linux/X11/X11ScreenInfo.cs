using static BetterGenshinImpact.Platform.Linux.X11.Native.Xlib;

namespace BetterGenshinImpact.Platform.Linux.X11;

/// <summary>XDisplayWidth / XDisplayHeight 实现的屏幕信息；每次读取新建连接（Xlib 连接不跨线程共享）</summary>
public sealed class X11ScreenInfo(string? display = null) : IScreenInfo
{
    public PixelSize PrimaryScreenSize
    {
        get
        {
            using var c = new X11Connection(display);
            return new PixelSize(XDisplayWidth(c.Display, c.Screen), XDisplayHeight(c.Display, c.Screen));
        }
    }
}
