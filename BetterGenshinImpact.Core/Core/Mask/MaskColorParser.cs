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
}
