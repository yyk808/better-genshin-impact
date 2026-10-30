using Vanara.PInvoke;

// 放在 BetterGenshinImpact.Platform 命名空间：主程序已全局导入，Win32 边界处直接 .ToPixelRect() / .ToRECT()
namespace BetterGenshinImpact.Platform;

/// <summary>Win32 RECT 与平台无关 <see cref="PixelRect"/> 的互转（只在 Win32 实现与 Win32 API 边界使用）</summary>
public static class Win32RectExtensions
{
    public static PixelRect ToPixelRect(this RECT rect) => PixelRect.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);

    public static PixelRect? ToPixelRect(this RECT? rect) => rect?.ToPixelRect();

    public static RECT ToRECT(this PixelRect rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);
}
