using System;

namespace BetterGenshinImpact.Core.Mask;

/// <summary>
/// 遮罩绘制用的颜色（sRGB，含透明度），替代原先的 System.Windows.Media.Color，使绘制模型不依赖 WPF。
/// 由 UI 宿主在渲染时转换为各自的颜色类型
/// </summary>
public readonly record struct MaskColor(byte A, byte R, byte G, byte B)
{
    public static MaskColor FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);

    /// <summary>不透明颜色，与 WPF Color.FromRgb 相同</summary>
    public static MaskColor FromRgb(byte r, byte g, byte b) => new(255, r, g, b);

    public static MaskColor Red => FromRgb(255, 0, 0);
    public static MaskColor Black => FromRgb(0, 0, 0);
    public static MaskColor White => FromRgb(255, 255, 255);

    public override string ToString() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";
}

/// <summary>遮罩绘制用的点（捕获像素坐标），替代原先的 System.Windows.Point</summary>
public readonly record struct MaskPoint(double X, double Y);

/// <summary>
/// 遮罩绘制用的矩形（捕获像素坐标），替代原先的 System.Windows.Rect。
/// 与 WPF Rect 一样，宽高为负时在构造时抛出 <see cref="ArgumentException"/>
/// </summary>
public readonly record struct MaskRect
{
    public MaskRect(double x, double y, double width, double height)
    {
        if (width < 0 || height < 0)
        {
            throw new ArgumentException("Width and Height must be non-negative.");
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }

    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
}
