namespace BetterGenshinImpact.Service.Guide;

/// <summary>
/// 收藏与观看记录的显示文本（WPF 与 Avalonia 设置页共用）
/// </summary>
public static class GuideLibraryPresentation
{
    /// <summary>
    /// 进度说明：已看完 / 看到 m:ss / 总长
    /// </summary>
    public static string DescribeProgress(GuideWatchRecord? record)
    {
        if (record is null)
        {
            return string.Empty;
        }

        if (record.Finished)
        {
            return "已看完";
        }

        return record.Duration > 0
            ? $"看到 {GuideWindowState.FormatTime(record.Position)} / {GuideWindowState.FormatTime(record.Duration)}"
            : $"看到 {GuideWindowState.FormatTime(record.Position)}";
    }
}
