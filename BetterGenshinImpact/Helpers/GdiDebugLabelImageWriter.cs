using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using FontStyle = System.Drawing.FontStyle;

namespace BetterGenshinImpact.Helpers;

/// <summary>
/// <see cref="IDebugLabelImageWriter"/> 的 GDI+ 实现，绘制方式与原 RewardResultRecognizer / InventoryCountComparisonTask 中的代码相同
/// </summary>
public sealed class GdiDebugLabelImageWriter : IDebugLabelImageWriter
{
    public static GdiDebugLabelImageWriter Instance { get; } = new();

    public void SavePng(Mat image, DebugLabelStyle style, IReadOnlyList<DebugLabel> labels, string path)
    {
        using var bitmap = image.ToBitmap();
        using var graphics = Graphics.FromImage(bitmap);
        using var font = new Font(style.FontFamily, style.FontSizePixels, style.Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        using var backgroundBrush = new SolidBrush(ToColor(style.Background));
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        if (style.NoWrapWithEllipsis)
        {
            format.FormatFlags = StringFormatFlags.NoWrap;
            format.Trimming = StringTrimming.EllipsisCharacter;
        }

        var brushes = new Dictionary<DebugLabelColor, SolidBrush>();
        try
        {
            foreach (var label in labels)
            {
                if (!brushes.TryGetValue(label.TextColor, out var brush))
                {
                    brush = new SolidBrush(ToColor(label.TextColor));
                    brushes[label.TextColor] = brush;
                }

                var labelRect = new RectangleF(label.X, label.Y, label.Width, label.Height);
                graphics.FillRectangle(backgroundBrush, labelRect);
                graphics.DrawString(label.Text, font, brush, labelRect, format);
            }
        }
        finally
        {
            foreach (var brush in brushes.Values)
            {
                brush.Dispose();
            }
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    private static Color ToColor(DebugLabelColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);
}
