using System;
using System.Globalization;

namespace BetterGenshinImpact.Core.Mask;

/// <summary>遮罩配置颜色的解析（原 OverlayStyleHelper.ParseRgbaHexColor 的平台无关部分）</summary>
public static class MaskColorParser
{
    /// <summary>
    /// 解析 #RRGGBB 或 #RRGGBBAA（注意 Alpha 在末尾，与 WPF 的 #AARRGGBB 不同）。
    /// 准星、技能 CD 等配置项使用这种格式。解析失败返回 null
    /// </summary>
    public static MaskColor? ParseRgbaHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var hex = value.Trim().TrimStart('#');
        if ((hex.Length != 6 && hex.Length != 8)
            || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
        {
            return null;
        }

        var r = byte.Parse(hex[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var g = byte.Parse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = byte.Parse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var a = hex.Length == 8 ? byte.Parse(hex[6..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : byte.MaxValue;
        return MaskColor.FromArgb(a, r, g, b);
    }

    /// <summary>
    /// 与 WPF ColorConverter 相同语义的颜色文本解析（支持颜色名），由宿主设置。
    /// 未设置时只支持 #RRGGBB / #AARRGGBB，解析失败抛出 <see cref="FormatException"/>
    /// </summary>
    public static Func<string, MaskColor>? ColorTextConverter { get; set; }

    /// <summary>按 WPF 颜色文本格式（#AARRGGBB、颜色名等）解析，失败时抛出异常</summary>
    public static MaskColor ConvertFromString(string text)
    {
        if (ColorTextConverter is { } converter)
        {
            return converter(text);
        }

        var hex = text.Trim().TrimStart('#');
        if ((hex.Length == 6 || hex.Length == 8)
            && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            var a = hex.Length == 8 ? (byte)(value >> 24) : byte.MaxValue;
            return MaskColor.FromArgb(a, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        }

        throw new FormatException($"无法解析颜色：{text}");
    }
}
