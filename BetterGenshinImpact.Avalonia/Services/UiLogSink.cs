using System.Collections.ObjectModel;
using Avalonia.Threading;
using Serilog.Core;
using Serilog.Formatting.Display;
using Serilog.Events;

namespace BetterGenshinImpact.Avalonia.Services;

/// <summary>界面日志面板的一条日志</summary>
public sealed record LogLine(DateTimeOffset Time, LogEventLevel Level, string Source, string Message)
{
    public string TimeText => Time.ToString("HH:mm:ss.fff");

    public string LevelText => Level switch
    {
        LogEventLevel.Verbose => "VRB",
        LogEventLevel.Debug => "DBG",
        LogEventLevel.Information => "INF",
        LogEventLevel.Warning => "WRN",
        LogEventLevel.Error => "ERR",
        _ => "FTL",
    };

    public string Text => $"{TimeText} {LevelText} {Message}";
}

/// <summary>
/// 把 Serilog 日志转到界面日志面板：任意线程写入，批量排队到 UI 线程，面板最多保留 <see cref="Capacity"/> 条
/// </summary>
public sealed class UiLogSink : ILogEventSink
{
    public const int Capacity = 2000;

    // 与日志文件相同的消息格式（字符串不加引号）
    private static readonly MessageTemplateTextFormatter MessageFormatter = new("{Message:lj}");

    private readonly Lock _lock = new();
    private readonly List<LogLine> _pending = [];
    private bool _flushQueued;

    public static UiLogSink Instance { get; } = new();

    public ObservableCollection<LogLine> Lines { get; } = [];

    /// <summary>最低显示级别（调试日志很多，默认只显示信息及以上）</summary>
    public LogEventLevel MinimumLevel { get; set; } = LogEventLevel.Information;

    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Level < MinimumLevel)
        {
            return;
        }

        var source = logEvent.Properties.TryGetValue("SourceContext", out var value) && value is ScalarValue { Value: string s }
            ? s[(s.LastIndexOf('.') + 1)..]
            : "";
        using var writer = new StringWriter();
        MessageFormatter.Format(logEvent, writer);
        var message = writer.ToString();
        if (logEvent.Exception is { } ex)
        {
            message += $"（{ex.GetType().Name}: {ex.Message}）";
        }

        if (!string.IsNullOrEmpty(source))
        {
            message = $"{source}: {message}";
        }

        lock (_lock)
        {
            _pending.Add(new LogLine(logEvent.Timestamp, logEvent.Level, source, message));
            if (_flushQueued)
            {
                return;
            }

            _flushQueued = true;
        }

        Dispatcher.UIThread.Post(Flush, DispatcherPriority.Background);
    }

    private void Flush()
    {
        LogLine[] batch;
        lock (_lock)
        {
            batch = [.. _pending];
            _pending.Clear();
            _flushQueued = false;
        }

        foreach (var line in batch)
        {
            Lines.Add(line);
        }

        while (Lines.Count > Capacity)
        {
            Lines.RemoveAt(0);
        }

        Flushed?.Invoke();
    }

    /// <summary>一批日志加入面板后（UI 线程）</summary>
    public event Action? Flushed;
}
