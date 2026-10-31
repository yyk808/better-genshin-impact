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

    /// <summary>
    /// 战斗策略参数值（策略名、auto: 引用或 User/AutoFight 下的路径）→ 下拉框中的规范名称；
    /// 默认值为策略根目录路径时显示“根据队伍自动选择”（与 WPF 策略选择控件相同）。选中项为空时不写回。
    /// </summary>
    public static readonly IValueConverter CombatStrategyValue = new CombatStrategyValueConverter();

    private sealed class CombatStrategyValueConverter : IValueConverter
    {
        public object? Convert(object? value, System.Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => BetterGenshinImpact.GameTask.AutoFight.Strategy.CombatStrategyResolver.Parse(value as string).Value;

        public object? ConvertBack(object? value, System.Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => value as string is { Length: > 0 } name ? name : global::Avalonia.Data.BindingOperations.DoNothing;
    }
}
