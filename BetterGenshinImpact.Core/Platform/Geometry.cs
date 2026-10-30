using System;

namespace BetterGenshinImpact.Platform;

/// <summary>
/// 像素矩形（物理像素）。替代接口层中的 Win32 <c>RECT</c>。
/// <para>
/// 以 (X, Y, Width, Height) 存储；同时提供 <see cref="Left"/>/<see cref="Top"/>/<see cref="Right"/>/<see cref="Bottom"/>，
/// 与 Vanara <c>RECT</c> 的只读成员同名、同语义（Right/Bottom 为不含的右/下边界），调用方改类型后无需改读法。
/// 注意构造参数是宽高：Win32 <c>new RECT(l, t, r, b)</c> 对应 <see cref="FromLTRB"/>。
/// </para>
/// </summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public static PixelRect Empty => default;

    public static PixelRect FromLTRB(int left, int top, int right, int bottom) => new(left, top, right - left, bottom - top);

    public int Left => X;

    public int Top => Y;

    public int Right => X + Width;

    public int Bottom => Y + Height;

    public PixelPoint Location => new(X, Y);

    public PixelSize Size => new(Width, Height);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(int x, int y) => x >= X && y >= Y && x < Right && y < Bottom;

    public PixelRect Offset(int dx, int dy) => this with { X = X + dx, Y = Y + dy };

    public PixelRect Intersect(PixelRect other)
    {
        int x1 = Math.Max(X, other.X), y1 = Math.Max(Y, other.Y);
        int x2 = Math.Min(Right, other.Right), y2 = Math.Min(Bottom, other.Bottom);
        return x2 > x1 && y2 > y1 ? new PixelRect(x1, y1, x2 - x1, y2 - y1) : default;
    }

    public override string ToString() => $"{{X={X},Y={Y},Width={Width},Height={Height}}}";

    // System.Drawing.Rectangle 属于 System.Drawing.Primitives（跨平台），可以安全互转
    public static implicit operator System.Drawing.Rectangle(PixelRect r) => new(r.X, r.Y, r.Width, r.Height);

    public static implicit operator PixelRect(System.Drawing.Rectangle r) => new(r.X, r.Y, r.Width, r.Height);

    public OpenCvSharp.Rect ToCvRect() => new(X, Y, Width, Height);

    public static PixelRect FromCvRect(OpenCvSharp.Rect r) => new(r.X, r.Y, r.Width, r.Height);
}

/// <summary>像素点（物理像素）。</summary>
public readonly record struct PixelPoint(int X, int Y);

/// <summary>像素尺寸（物理像素）。</summary>
public readonly record struct PixelSize(int Width, int Height);
