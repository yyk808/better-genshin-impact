using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace BetterGenshinImpact.Platform.Linux.Wayland;

/// <summary>niri 事件：<paramref name="Kind"/> 为事件名（如 WindowLayoutsChanged、WindowFocusChanged），<paramref name="WindowIds"/> 为涉及的窗口（未知时为空）</summary>
public sealed record NiriEvent(string Kind, IReadOnlyList<ulong> WindowIds);

/// <summary>
/// niri 事件流（请求 <c>"EventStream"</c> 后连接上持续推送每行一个事件）：窗口打开 / 关闭 / 焦点 / 布局（移动、缩放）/ 工作区 / 输出变化。
/// 后台线程读取，断开后每 2 秒重连；niri 不支持时 <see cref="IsActive"/> 保持 false
/// </summary>
public sealed class NiriEventStream : IDisposable
{
    private readonly string _socketPath;
    private readonly Thread _thread;
    private volatile bool _stop;
    private Socket? _socket;

    public NiriEventStream(string socketPath)
    {
        _socketPath = socketPath;
        _thread = new Thread(Run) { IsBackground = true, Name = "niri-events" };
        _thread.Start();
    }

    /// <summary>事件流已连接（收到了 Ok 回复）</summary>
    public bool IsActive { get; private set; }

    /// <summary>收到的事件数（诊断用）</summary>
    public long Count { get; private set; }

    /// <summary>每个事件（在后台线程上触发）</summary>
    public event Action<NiriEvent>? Received;

    private void Run()
    {
        while (!_stop)
        {
            try
            {
                using var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                _socket = s;
                s.Connect(new UnixDomainSocketEndPoint(_socketPath));
                s.Send("\"EventStream\"\n"u8.ToArray());
                using var reader = new StreamReader(new NetworkStream(s, ownsSocket: false), Encoding.UTF8);
                var first = reader.ReadLine();
                if (first is null || JsonNode.Parse(first)?["Ok"] is null) return; // 不支持事件流（旧版 niri）
                IsActive = true;
                while (!_stop && reader.ReadLine() is { } line)
                {
                    if (Parse(line) is { } ev)
                    {
                        Count++;
                        Received?.Invoke(ev);
                    }
                }
            }
            catch (Exception e) when (e is IOException or SocketException or System.Text.Json.JsonException or ObjectDisposedException)
            {
            }
            finally
            {
                IsActive = false;
                _socket = null;
            }

            for (var i = 0; i < 20 && !_stop; i++) Thread.Sleep(100);
        }
    }

    /// <summary>解析一行事件：{"事件名":{…}}，取出涉及的窗口 id</summary>
    public static NiriEvent? Parse(string line)
    {
        if (JsonNode.Parse(line) is not JsonObject o || o.Count == 0) return null;
        var (kind, body) = o.First();
        var ids = new List<ulong>();
        if (kind == "WindowOpenedOrChanged")
        {
            if (body?["window"]?["id"] is JsonValue id) ids.Add(id.GetValue<ulong>());
        }
        else if (kind is "WindowClosed" or "WindowFocusChanged" or "WindowUrgencyChanged")
        {
            if (body?["id"] is JsonValue id) ids.Add(id.GetValue<ulong>());
        }
        else if (kind == "WindowLayoutsChanged" && body?["changes"] is JsonArray changes)
        {
            foreach (var c in changes)
            {
                if (c is JsonArray pair && pair.Count > 0 && pair[0] is JsonValue v) ids.Add(v.GetValue<ulong>());
            }
        }
        else if (kind == "WindowsChanged" && body?["windows"] is JsonArray windows)
        {
            foreach (var w in windows)
            {
                if (w?["id"] is JsonValue v) ids.Add(v.GetValue<ulong>());
            }
        }

        return new NiriEvent(kind, ids);
    }

    public void Dispose()
    {
        _stop = true;
        try
        {
            _socket?.Shutdown(SocketShutdown.Both);
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
        }
    }
}
