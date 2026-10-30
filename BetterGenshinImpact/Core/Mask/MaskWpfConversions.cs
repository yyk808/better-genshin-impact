using System.Windows;
using System.Windows.Media;

namespace BetterGenshinImpact.Core.Mask;

/// <summary>
/// 遮罩绘制模型（Core 中的平台无关类型）与 WPF 类型之间的转换
/// </summary>
public static class MaskWpfConversions
{
    public static MaskColor ToMaskColor(this Color color) => MaskColor.FromArgb(color.A, color.R, color.G, color.B);

    public static Color ToWpfColor(this MaskColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    public static MaskPoint ToMaskPoint(this Point point) => new(point.X, point.Y);

    public static MaskRect ToMaskRect(this Rect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
}
