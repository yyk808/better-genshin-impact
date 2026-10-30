using System;
using System.Drawing;

namespace BetterGenshinImpact.Core.Drawing;

/// <summary>
/// 遮罩窗口上的线条样式（颜色 + 线宽），替代业务代码中原先使用的 GDI+ <c>System.Drawing.Pen</c>。
/// 不持有任何非托管资源，可在 Linux 上使用。<see cref="Color"/> 来自跨平台的 System.Drawing.Primitives。
/// 脚本引擎中仍以 <c>Pen</c> 名称暴露，<c>new Pen(Color.Red, 2)</c> 的写法保持可用
/// </summary>
public sealed class DrawPen : IEquatable<DrawPen>, IDisposable
{
    /// <summary>线宽默认 1，与 System.Drawing.Pen(Color) 相同</summary>
    public DrawPen(Color color) : this(color, 1f)
    {
    }

    public DrawPen(Color color, float width)
    {
        Color = color;
        Width = width;
    }

    public Color Color { get; set; }

    public float Width { get; set; }

    public DrawPen Clone() => new(Color, Width);

    /// <summary>无资源可释放；保留是为了兼容原先对 Pen 调用 Dispose / using 的代码与脚本</summary>
    public void Dispose()
    {
    }

    public bool Equals(DrawPen? other) =>
        other is not null && Color.ToArgb() == other.Color.ToArgb() && Width.Equals(other.Width);

    public override bool Equals(object? obj) => obj is DrawPen p && Equals(p);

    public override int GetHashCode() => HashCode.Combine(Color.ToArgb(), Width);

    public override string ToString() => $"DrawPen({Color.Name}, {Width})";
}

/// <summary>与 System.Drawing.Pens 对应的常用画笔（线宽 1）。每次返回新实例，修改不会影响其他调用方</summary>
public static class DrawPens
{
    public static DrawPen Red => new(Color.Red);
    public static DrawPen Lime => new(Color.Lime);
    public static DrawPen White => new(Color.White);
    public static DrawPen Yellow => new(Color.Yellow);
    public static DrawPen LightPink => new(Color.LightPink);
    public static DrawPen Blue => new(Color.Blue);
    public static DrawPen Green => new(Color.Green);
    public static DrawPen Black => new(Color.Black);
}
