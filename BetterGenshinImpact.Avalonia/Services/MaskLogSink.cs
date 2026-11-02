using BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace BetterGenshinImpact.Avalonia.Services;

/// <summary>遮罩日志框里的一段文字（颜色为 #RRGGBB，背景为空表示透明）</summary>
public sealed record MaskLogSegment(string Text, string Color, string? Background = null);

/// <summary>
/// 遮罩日志框的日志来源（对应 WPF 的 BoundedRichTextBoxSink）：信息及以上级别、排除自动配音的原始诊断（QuestVoiceLog.IsOverlayVisible），
/// 按 WPF 相同的模板 <c>[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}</c> 和 Literate 配色切成着色片段；
/// 只保留最近 <see cref="Capacity"/> 条，遮罩按日志框高度显示末尾的若干行
/// </summary>
public sealed class MaskLogSink : ILogEventSink
{
    public const int Capacity = 60;

    // Serilog Literate 主题（RichTextBox 版，与 WPF 遮罩相同）用到的控制台颜色
    public const string White = "#FFFFFF", Gray = "#C0C0C0", DarkGray = "#808080", Yellow = "#FFFF00", Cyan = "#00FFFF",
        Magenta = "#FF00FF", Blue = "#0000FF", Green = "#00FF00", Red = "#FF0000";

    private readonly Lock _lock = new();
    private readonly Queue<IReadOnlyList<MaskLogSegment>> _lines = new();

    public static MaskLogSink Instance { get; } = new();

    /// <summary>
    /// 是否接收日志（同 WPF：只有遮罩启用且显示日志框时才写入，隐藏期间的日志不补显示）；未设置时总是接收
    /// </summary>
    public Func<bool>? IsEnabled { get; set; }

    /// <summary>内容变化计数（遮罩只在变化时重建文字）</summary>
    public long Version { get; private set; }

    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Level < LogEventLevel.Information || IsEnabled?.Invoke() == false || !QuestVoiceLog.IsOverlayVisible(logEvent))
        {
            return;
        }

        var line = Format(logEvent);
        lock (_lock)
        {
            _lines.Enqueue(line);
            while (_lines.Count > Capacity) _lines.Dequeue();
            Version++;
        }
    }

    public IReadOnlyList<IReadOnlyList<MaskLogSegment>> Snapshot()
    {
        lock (_lock) return _lines.ToList();
    }

    public void Clear()
    {
        lock (_lock)
        {
            _lines.Clear();
            Version++;
        }
    }

    /// <summary>按 WPF 遮罩的模板和 Literate 配色格式化一条日志</summary>
    public static IReadOnlyList<MaskLogSegment> Format(LogEvent e)
    {
        var s = new List<MaskLogSegment>
        {
            new("[", DarkGray),
            new(e.Timestamp.ToString("HH:mm:ss"), Gray),
            new(" ", DarkGray),
        };
        s.Add(e.Level switch
        {
            LogEventLevel.Information => new MaskLogSegment("INF", White),
            LogEventLevel.Warning => new MaskLogSegment("WRN", Yellow),
            LogEventLevel.Error => new MaskLogSegment("ERR", White, Red),
            LogEventLevel.Fatal => new MaskLogSegment("FTL", White, Red),
            LogEventLevel.Debug => new MaskLogSegment("DBG", Gray),
            _ => new MaskLogSegment("VRB", Gray),
        });
        s.Add(new("] ", DarkGray));
        foreach (var token in e.MessageTemplate.Tokens)
        {
            if (token is TextToken text)
            {
                s.Add(new(text.Text, White));
            }
            else if (token is PropertyToken property)
            {
                s.Add(e.Properties.TryGetValue(property.PropertyName, out var value)
                    ? FormatValue(value, property.Format)
                    : new MaskLogSegment(property.ToString(), Yellow));
            }
        }

        if (e.Exception is { } ex)
        {
            s.Add(new("\n" + ex, White));
        }

        return s;
    }

    /// <summary>:lj 格式：字符串不加引号（青色），数字品红，布尔与 null 蓝色，其他标量绿色，结构按 JSON 风格白色</summary>
    private static MaskLogSegment FormatValue(LogEventPropertyValue value, string? format)
    {
        if (value is ScalarValue scalar)
        {
            return scalar.Value switch
            {
                null => new MaskLogSegment("null", Blue),
                string str => new MaskLogSegment(str, Cyan),
                bool b => new MaskLogSegment(b ? "true" : "false", Blue),
                byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
                    => new MaskLogSegment(scalar.Value is IFormattable f ? f.ToString(format, null) : scalar.Value.ToString() ?? "", Magenta),
                IFormattable f => new MaskLogSegment(f.ToString(format, null), Green),
                var other => new MaskLogSegment(other.ToString() ?? "", Green),
            };
        }

        using var writer = new StringWriter();
        value.Render(writer, format);
        return new MaskLogSegment(writer.ToString(), White);
    }
}
