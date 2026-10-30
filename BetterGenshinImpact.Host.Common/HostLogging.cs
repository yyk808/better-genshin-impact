using Serilog;
using Serilog.Events;

namespace BetterGenshinImpact.Host.Common;

/// <summary>宿主共用的 Serilog 配置：控制台 + 可选日志文件，宿主可再追加自己的输出（如界面日志面板）</summary>
public static class HostLogging
{
    public const string OutputTemplate = "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    public static void Configure(string? logFile, Action<LoggerConfiguration>? extra = null)
    {
        var configuration = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .WriteTo.Console(outputTemplate: OutputTemplate);
        if (!string.IsNullOrEmpty(logFile))
        {
            configuration.WriteTo.File(logFile, outputTemplate: OutputTemplate);
        }

        extra?.Invoke(configuration);
        Log.Logger = configuration.CreateLogger();
    }
}
