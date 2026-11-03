using System.Text.Json;
using BetterGenshinImpact.Service.ExternalAccess.Logging;
using Serilog.Events;

namespace BetterGenshinImpact.Core.UnitTest.ExternalAccess;

/// <summary>
/// WebSocket 实时日志按 Web 默认选项序列化 ExternalLogEvent；内部过滤用的 LevelValue 不能让序列化失败，也不能写进 JSON
/// </summary>
public sealed class ExternalLogEventSerializationTests
{
    [Fact]
    public void SerializesWithoutInternalLevelValue()
    {
        var e = new ExternalLogEvent
        {
            Sequence = 7,
            TimestampUtc = DateTimeOffset.UnixEpoch,
            Level = "Information",
            Message = "hello",
            Instance = "Primary:S0:P1:T0",
            LevelValue = LogEventLevel.Information
        };

        var json = JsonSerializer.Serialize(e, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("log", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal(7, doc.RootElement.GetProperty("sequence").GetInt64());
        Assert.Equal("hello", doc.RootElement.GetProperty("message").GetString());
        Assert.False(doc.RootElement.TryGetProperty("levelValue", out _));
    }

    [Fact]
    public void HubSnapshotFiltersByLevelAndSerializes()
    {
        var hub = new ExternalLogHub();
        hub.Publish(DateTimeOffset.UnixEpoch, LogEventLevel.Debug, null, "debug", null, "i");
        hub.Publish(DateTimeOffset.UnixEpoch, LogEventLevel.Warning, "src", "warn", null, "i");
        var sub = hub.Subscribe(LogEventLevel.Information);
        try
        {
            var only = Assert.Single(sub.Snapshot);
            Assert.Equal("warn", only.Message);
            var json = JsonSerializer.Serialize(only, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Contains("\"level\":\"Warning\"", json);
        }
        finally
        {
            hub.Unsubscribe(sub.Id);
        }
    }
}
