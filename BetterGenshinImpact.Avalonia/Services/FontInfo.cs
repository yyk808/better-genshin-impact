using Avalonia.Media;

namespace BetterGenshinImpact.Avalonia.Services;

/// <summary>
/// 中文界面字体：按优先级在系统字体中找第一个可用的 CJK 字体（Linux 常见为 Noto Sans CJK / 思源黑体 / 文泉驿），
/// 主窗口用“中文字体, 默认字体”的回退链。找不到时界面会出现方块，环境页给出安装提示
/// </summary>
public static class FontInfo
{
    public static readonly string[] CjkCandidates =
    [
        "Noto Sans CJK SC", "Noto Sans SC", "Source Han Sans SC", "Source Han Sans CN", "WenQuanYi Micro Hei",
        "WenQuanYi Zen Hei", "Microsoft YaHei", "PingFang SC", "Sarasa Gothic SC", "Noto Sans CJK JP", "Droid Sans Fallback",
    ];

    /// <summary>找到的中文字体名（Detect 之后）</summary>
    public static string? CjkFamily { get; private set; }

    /// <summary>界面字体回退链</summary>
    public static FontFamily UiFontFamily { get; private set; } = FontFamily.Default;

    public static void Detect()
    {
        var installed = FontManager.Current.SystemFonts.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        CjkFamily = CjkCandidates.FirstOrDefault(installed.Contains);
        var families = new List<string>();
        if (CjkFamily is not null)
        {
            families.Add(CjkFamily);
        }

        families.Add(FontManager.Current.DefaultFontFamily.Name);
        UiFontFamily = new FontFamily(string.Join(", ", families.Distinct()));
    }

    /// <summary>字体回退（用于 FontManagerOptions，在 Avalonia 初始化前配置）</summary>
    public static IReadOnlyList<FontFallback> Fallbacks =>
        CjkCandidates.Select(name => new FontFallback { FontFamily = new FontFamily(name) }).ToList();
}
