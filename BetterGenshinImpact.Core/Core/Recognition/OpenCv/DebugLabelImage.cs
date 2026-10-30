using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OpenCv;

/// <summary>调试标注颜色（ARGB）</summary>
public readonly record struct DebugLabelColor(byte A, byte R, byte G, byte B)
{
    public static DebugLabelColor FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);

    /// <summary>与 System.Drawing.Color.LimeGreen 相同</summary>
    public static DebugLabelColor LimeGreen { get; } = new(255, 50, 205, 50);

    /// <summary>与 System.Drawing.Color.Gold 相同</summary>
    public static DebugLabelColor Gold { get; } = new(255, 255, 215, 0);

    /// <summary>与 System.Drawing.Color.Red 相同</summary>
    public static DebugLabelColor Red { get; } = new(255, 255, 0, 0);
}

/// <summary>调试标注的字体与底色</summary>
/// <param name="FontFamily">字体名</param>
/// <param name="FontSizePixels">字号（像素）</param>
/// <param name="Bold">是否加粗</param>
/// <param name="Background">文字底色，每个标注先填充底色再绘制文字</param>
/// <param name="NoWrapWithEllipsis">不换行，超出时以省略号截断</param>
public sealed record DebugLabelStyle(string FontFamily, float FontSizePixels, bool Bold, DebugLabelColor Background, bool NoWrapWithEllipsis);

/// <summary>一个调试标注：区域内居中绘制的文字</summary>
public readonly record struct DebugLabel(float X, float Y, float Width, float Height, string Text, DebugLabelColor TextColor);

/// <summary>把带文字标注的调试图保存为 PNG</summary>
public interface IDebugLabelImageWriter
{
    /// <param name="image">底图（BGR / BGRA），不会被修改</param>
    /// <param name="style">字体与底色</param>
    /// <param name="labels">按顺序绘制的标注</param>
    /// <param name="path">PNG 文件路径</param>
    void SavePng(Mat image, DebugLabelStyle style, IReadOnlyList<DebugLabel> labels, string path);
}

/// <summary>
/// 调试标注图的保存入口。Windows 主程序设置为 GDI+ 实现（与原来的绘制代码相同）；
/// 未设置时用 OpenCV 绘制（Hershey 字体只能显示 ASCII，中文显示为问号，仅影响调试图）
/// </summary>
public static class DebugLabelImage
{
    private static IDebugLabelImageWriter _writer = new OpenCvDebugLabelImageWriter();

    public static IDebugLabelImageWriter Writer
    {
        get => _writer;
        set => _writer = value ?? throw new ArgumentNullException(nameof(value));
    }
}

/// <summary>用 OpenCV 绘制的调试标注（无 GDI+ 的平台）</summary>
public sealed class OpenCvDebugLabelImageWriter : IDebugLabelImageWriter
{
    public void SavePng(Mat image, DebugLabelStyle style, IReadOnlyList<DebugLabel> labels, string path)
    {
        using var canvas = image.Clone();
        var scale = style.FontSizePixels / 22.0;
        var thickness = style.Bold ? 2 : 1;
        foreach (var label in labels)
        {
            var rect = new Rect((int)label.X, (int)label.Y, Math.Max(1, (int)label.Width), Math.Max(1, (int)label.Height));
            Blend(canvas, rect, style.Background);
            var size = Cv2.GetTextSize(label.Text, HersheyFonts.HersheySimplex, scale, thickness, out var baseline);
            var origin = new Point(rect.X + Math.Max(0, (rect.Width - size.Width) / 2), rect.Y + (rect.Height + size.Height) / 2);
            Cv2.PutText(canvas, label.Text, origin, HersheyFonts.HersheySimplex, scale, ToScalar(label.TextColor), thickness, LineTypes.AntiAlias);
        }

        Cv2.ImWrite(path, canvas);
    }

    private static void Blend(Mat canvas, Rect rect, DebugLabelColor color)
    {
        rect &= new Rect(0, 0, canvas.Width, canvas.Height);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        using var roi = new Mat(canvas, rect);
        using var fill = new Mat(roi.Size(), roi.Type(), ToScalar(color));
        var alpha = color.A / 255.0;
        Cv2.AddWeighted(fill, alpha, roi, 1 - alpha, 0, roi);
    }

    private static Scalar ToScalar(DebugLabelColor c) => new(c.B, c.G, c.R, 255);
}
