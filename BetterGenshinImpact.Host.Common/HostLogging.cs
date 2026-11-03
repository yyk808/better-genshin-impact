using BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace BetterGenshinImpact.Host.Common;

/// <summary>
/// 宿主共用的 Serilog 配置：控制台 + 可选日志文件，宿主可再追加自己的输出（如界面日志面板）。
/// 另有“软件日志文件”（<see cref="FileLog"/>）：与 Windows 版相同写到 log/better-genshin-impact日期.log，
/// 日志目录要等宿主确定素材目录、读到 Linux 宿主设置后才知道，此前的日志先缓存
/// </summary>
public static class HostLogging
{
    public const string OutputTemplate = "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    /// <summary>软件日志文件（log 目录，按天滚动）</summary>
    public static DeferredFileLogSink FileLog { get; } = new();

    /// <summary>
    /// 外部访问的实时日志分发器（WebSocket /ws/v1/logs）：与 WPF 相同先于 Serilog 创建，启动阶段的日志也进入回放缓冲
    /// </summary>
    public static BetterGenshinImpact.Service.ExternalAccess.Logging.ExternalLogHub ExternalLog { get; } = new();

    /// <summary>外部日志里的实例标识（同 WPF：类型:S会话:P进程:T启动毫秒），多实例上下文确定后由宿主设置</summary>
    public static string ExternalLogInstance
    {
        get => _externalSink.Identity;
        set => _externalSink.Identity = value;
    }

    private static readonly ExternalLogForwarder _externalSink = new();

    /// <summary>转交给 <see cref="BetterGenshinImpact.Service.ExternalAccess.Logging.ExternalLogSink"/>，实例标识可在启动后更新</summary>
    private sealed class ExternalLogForwarder : ILogEventSink
    {
        private BetterGenshinImpact.Service.ExternalAccess.Logging.ExternalLogSink _sink = new(ExternalLog, Default);
        private string _identity = Default;

        private static string Default => $"Primary:S0:P{Environment.ProcessId}:T{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        public string Identity
        {
            get => _identity;
            set
            {
                _identity = value;
                _sink = new(ExternalLog, value);
            }
        }

        public void Emit(LogEvent logEvent) => _sink.Emit(logEvent);
    }

    public static void Configure(string? logFile, Action<LoggerConfiguration>? extra = null)
    {
        var configuration = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .WriteTo.Console(outputTemplate: OutputTemplate)
            .WriteTo.Sink(FileLog)
            .WriteTo.Sink(_externalSink, LogEventLevel.Debug);
        if (!string.IsNullOrEmpty(logFile))
        {
            configuration.WriteTo.File(logFile, outputTemplate: OutputTemplate);
        }

        extra?.Invoke(configuration);
        Log.Logger = configuration.CreateLogger();
    }
}

/// <summary>
/// 延后启用的日志文件输出：启用前缓存最近的日志（最多 5000 条），启用时先写入缓存；关闭后不再写文件。
/// 文件格式与 Windows 版相同（按天滚动、多进程共享、保留 31 个文件 / 21 天，不记录剧情语音的悬浮摘要）
/// </summary>
public sealed class DeferredFileLogSink : ILogEventSink, IDisposable
{
    private const int MaxPending = 5000;
    private const string FileTemplate = "[{Timestamp:HH:mm:ss.fff}] [{Level:u3}] {SourceContext}{NewLine}{Message}{NewLine}{Exception}{NewLine}";
    private readonly object _gate = new();
    private Queue<LogEvent>? _pending = new();
    private Logger? _file;

    /// <summary>日志目录（启用后）</summary>
    public string? Directory { get; private set; }

    public bool Enabled => _file is not null;

    public void Emit(LogEvent logEvent)
    {
        lock (_gate)
        {
            if (_file is not null)
            {
                _file.Write(logEvent);
            }
            else if (_pending is not null)
            {
                _pending.Enqueue(logEvent);
                if (_pending.Count > MaxPending)
                {
                    _pending.Dequeue();
                }
            }
        }
    }

    /// <summary>开始写入 <paramref name="directory"/> 下的 better-genshin-impact日期.log（启动前的缓存一并写入）</summary>
    public void Enable(string directory)
    {
        lock (_gate)
        {
            if (_file is not null && Directory == directory)
            {
                return;
            }

            _file?.Dispose();
            System.IO.Directory.CreateDirectory(directory);
            Directory = directory;
            _file = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .Filter.ByExcluding(QuestVoiceLog.IsSummary)
                .WriteTo.File(Path.Combine(directory, "better-genshin-impact.log"),
                    outputTemplate: FileTemplate,
                    rollingInterval: RollingInterval.Day,
                    shared: true,
                    retainedFileCountLimit: 31,
                    retainedFileTimeLimit: TimeSpan.FromDays(21))
                .CreateLogger();
            if (_pending is not null)
            {
                foreach (var e in _pending)
                {
                    _file.Write(e);
                }

                _pending = null;
            }
        }
    }

    /// <summary>不写日志文件（丢弃缓存）</summary>
    public void Disable()
    {
        lock (_gate)
        {
            _pending = null;
            _file?.Dispose();
            _file = null;
        }
    }

    /// <summary>最新的日志文件；没有时为 null</summary>
    public string? LatestFile()
    {
        var dir = Directory;
        if (dir is null || !System.IO.Directory.Exists(dir))
        {
            return null;
        }

        return new DirectoryInfo(dir).GetFiles("better-genshin-impact*.log")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault()?.FullName;
    }

    public void Dispose() => Disable();
}
