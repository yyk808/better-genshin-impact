using OpenCvSharp;
using Color = System.Drawing.Color;

namespace BetterGenshinImpact.Helpers.Extensions;

/// <summary>
/// System.Drawing.Color（Primitives，跨平台）转 OpenCV 颜色。原在 BitmapExtension 中，因 BitmapExtension 依赖 WPF 拆出，实现未改
/// </summary>
public static class ColorScalarExtension
{
    public static Scalar ToScalar(this Color color)
    {
        return new Scalar(color.R, color.G, color.B);
    }
}
