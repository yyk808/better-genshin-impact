using Avalonia.Data.Converters;
using Avalonia.Media;
using Serilog.Events;

namespace BetterGenshinImpact.Avalonia.Services;

public static class Converters
{
    private static readonly IBrush Debug = new SolidColorBrush(Color.Parse("#8A8A8A"));
    private static readonly IBrush Info = new SolidColorBrush(Color.Parse("#DADADA"));
    private static readonly IBrush Warning = new SolidColorBrush(Color.Parse("#F5C451"));
    private static readonly IBrush Error = new SolidColorBrush(Color.Parse("#FF6B6B"));

    /// <summary>日志级别 → 文字颜色</summary>
    public static readonly IValueConverter LogLevelBrush = new FuncValueConverter<LogEventLevel, IBrush>(level => level switch
    {
        <= LogEventLevel.Debug => Debug,
        LogEventLevel.Information => Info,
        LogEventLevel.Warning => Warning,
        _ => Error,
    });

    /// <summary>日志面板展开状态 → 按钮文字</summary>
    public static readonly IValueConverter ExpandGlyph = new FuncValueConverter<bool, string>(expanded => expanded ? "收起" : "展开");

    /// <summary>截图器运行状态 → 状态点颜色</summary>
    public static readonly IValueConverter RunningBrush = new FuncValueConverter<bool, IBrush>(running =>
        running ? new SolidColorBrush(Color.Parse("#46A758")) : new SolidColorBrush(Color.Parse("#6B6B6B")));
}
