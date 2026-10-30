using OpenCvSharp;
using System;
using Vanara.PInvoke;
using Color = System.Windows.Media.Color;
using Point = OpenCvSharp.Point;

namespace BetterGenshinImpact.Core.Recognition.OpenCv;

/// <summary>
/// 与 WPF / Win32 类型相关的扩展，原在 CommonExtension 中；平台无关的部分已移入 Core
/// </summary>
public static class CommonWindowsExtension
{
    public static System.Windows.Point ToWindowsPoint(this Point point)
    {
        return new System.Windows.Point(point.X, point.Y);
    }

    public static System.Windows.Rect ToWindowsRectangle(this Rect rect)
    {
        return new System.Windows.Rect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    public static System.Windows.Rect ToWindowsRectangleOffset(this Rect rect, int offsetX, int offsetY)
    {
        return new System.Windows.Rect(rect.X + offsetX, rect.Y + offsetY, rect.Width, rect.Height);
    }

    public static Point GetCenterPoint(this RECT rectangle)
    {
        if (rectangle.IsEmpty) throw new ArgumentException("rectangle is empty");

        return new Point(rectangle.X + rectangle.Width / 2, rectangle.Y + rectangle.Height / 2);
    }

    public static Color ToWindowsColor(this System.Drawing.Color color)
    {
        return Color.FromArgb(color.A, color.R, color.G, color.B);
    }
}
