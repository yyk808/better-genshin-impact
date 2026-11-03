using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BetterGenshinImpact.Platform.Linux.Wayland;

/// <summary>niri 窗口（niri msg --json windows 的一项）</summary>
/// <param name="ScreenRect">
/// 窗口内容在合成器全局坐标中的位置（逻辑像素 × 所在输出缩放 = 物理像素）。niri IPC 只报告浮动窗口的位置：
/// 平铺窗口除非占满整个输出（全屏）否则为 null；不在活动工作区时也为 null
/// </param>
public sealed record NiriWindowInfo(ulong Id, string Title, string AppId, int Pid, ulong? WorkspaceId, bool IsFocused, bool IsFloating,
    PixelRect? ScreenRect, (int Width, int Height) WindowSize, string? Output = null);

/// <summary>niri 输出（显示器）</summary>
public sealed record NiriOutputInfo(string Name, int X, int Y, int Width, int Height, double Scale);

/// <summary>
/// niri IPC 客户端：连接 $NIRI_SOCKET（Unix 套接字），每个请求一行 JSON，回一行 {"Ok":…} 或 {"Err":"…"}。
/// 协议见 niri-ipc（与 <c>niri msg --json</c> 相同）。每次请求新建连接，线程安全。
/// 窗口 / 输出列表会缓存：事件流可用时缓存到下一个事件（最多 1 秒），否则 100 ms；每帧都查前台窗口时不会每次都走 IPC
/// </summary>
public sealed class NiriIpc : IDisposable
{
    public NiriIpc(string socketPath) => SocketPath = socketPath;

    private readonly object _gate = new();
    private NiriEventStream? _events;
    private long _version;
    private (long Version, long At, IReadOnlyList<NiriWindowInfo> Value)? _windowsCache;
    private (long Version, long At, IReadOnlyList<NiriOutputInfo> Value)? _outputsCache;

    /// <summary>niri 事件流（第一次访问时连接）；窗口移动 / 缩放 / 焦点变化等由它通知，缓存也按它失效</summary>
    public NiriEventStream Events
    {
        get
        {
            lock (_gate)
            {
                if (_events is null)
                {
                    _events = new NiriEventStream(SocketPath);
                    _events.Received += _ => Interlocked.Increment(ref _version);
                }

                return _events;
            }
        }
    }

    /// <summary>不用缓存（测试用）</summary>
    public bool CacheDisabled { get; set; }

    private long CacheMs => _events is { IsActive: true } ? 1000 : 100;

    private T Cached<T>(ref (long Version, long At, T Value)? cache, Func<T> load)
    {
        var now = Environment.TickCount64;
        var version = Interlocked.Read(ref _version);
        lock (_gate)
        {
            if (!CacheDisabled && cache is { } c && c.Version == version && now - c.At < CacheMs) return c.Value;
        }

        var value = load();
        lock (_gate)
        {
            cache = (version, now, value);
        }

        return value;
    }

    public void Dispose() => _events?.Dispose();

    public string SocketPath { get; }

    /// <summary>$NIRI_SOCKET 存在时创建客户端，否则为 null（不是 niri 或 niri 太旧）</summary>
    public static NiriIpc? FromEnvironment()
    {
        var path = Environment.GetEnvironmentVariable("NIRI_SOCKET");
        return !string.IsNullOrEmpty(path) && File.Exists(path) ? new NiriIpc(path) : null;
    }

    /// <summary>发送一个请求（JSON 值，如 <c>"Windows"</c> 或 <c>{"Action":{…}}</c>），返回 Ok 内容；Err 时抛 <see cref="InvalidOperationException"/></summary>
    public JsonNode? Request(string requestJson, int timeoutMs = 2000)
    {
        using var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        s.ReceiveTimeout = timeoutMs;
        s.SendTimeout = timeoutMs;
        s.Connect(new UnixDomainSocketEndPoint(SocketPath));
        s.Send(Encoding.UTF8.GetBytes(requestJson + "\n"));
        var buf = new MemoryStream();
        var chunk = new byte[64 * 1024];
        while (true)
        {
            var n = s.Receive(chunk);
            if (n <= 0) break;
            buf.Write(chunk, 0, n);
            if (chunk[n - 1] == (byte)'\n') break;
        }

        var reply = JsonNode.Parse(Encoding.UTF8.GetString(buf.ToArray()));
        if (reply?["Err"] is { } err) throw new InvalidOperationException("niri IPC 错误：" + err.GetValue<string>());
        return reply?["Ok"];
    }

    /// <summary>执行动作，如 <c>{"FocusWindow":{"id":1}}</c></summary>
    public void Action(string actionJson)
    {
        Request("{\"Action\":" + actionJson + "}");
        Interlocked.Increment(ref _version); // 动作会改变窗口状态，缓存作废
    }

    public void FocusWindow(ulong id) => Action($"{{\"FocusWindow\":{{\"id\":{id}}}}}");

    /// <summary>把窗口改为浮动（niri 只报告浮动窗口的屏幕位置）</summary>
    public void MoveWindowToFloating(ulong id) => Action($"{{\"MoveWindowToFloating\":{{\"id\":{id}}}}}");

    /// <summary>把浮动窗口移到工作区视图中的逻辑坐标（与窗口列表里的 tile_pos_in_workspace_view 相同的坐标系）</summary>
    public void MoveFloatingWindow(ulong id, double x, double y) => Action(FormattableString.Invariant(
        $"{{\"MoveFloatingWindow\":{{\"id\":{id},\"x\":{{\"SetFixed\":{x}}},\"y\":{{\"SetFixed\":{y}}}}}}}"));

    public void CloseWindow(ulong id) => Action($"{{\"CloseWindow\":{{\"id\":{id}}}}}");

    public IReadOnlyList<NiriOutputInfo> Outputs() => Cached(ref _outputsCache, LoadOutputs);

    private IReadOnlyList<NiriOutputInfo> LoadOutputs()
    {
        var list = new List<NiriOutputInfo>();
        if (Request("\"Outputs\"")?["Outputs"] is not JsonObject outputs) return list;
        foreach (var (name, o) in outputs)
        {
            if (o?["logical"] is not JsonObject l) continue;
            list.Add(new NiriOutputInfo(name, l["x"]!.GetValue<int>(), l["y"]!.GetValue<int>(), l["width"]!.GetValue<int>(),
                l["height"]!.GetValue<int>(), l["scale"]!.GetValue<double>()));
        }

        return list;
    }

    /// <summary>窗口列表，并按工作区所在输出算出屏幕位置</summary>
    public IReadOnlyList<NiriWindowInfo> Windows() => Cached(ref _windowsCache, LoadWindows);

    private IReadOnlyList<NiriWindowInfo> LoadWindows()
    {
        var windows = Request("\"Windows\"")?["Windows"] as JsonArray ?? [];
        var workspaces = Request("\"Workspaces\"")?["Workspaces"] as JsonArray ?? [];
        var outputs = LoadOutputs().ToDictionary(o => o.Name);
        var wsOutput = new Dictionary<ulong, string>();
        foreach (var w in workspaces)
        {
            if (w?["id"] is { } id && w["output"]?.GetValue<string>() is { } output) wsOutput[id.GetValue<ulong>()] = output;
        }

        return windows.Where(w => w is not null).Select(w => Parse(w!, wsOutput, outputs)).ToList();
    }

    internal static NiriWindowInfo Parse(JsonNode w, IReadOnlyDictionary<ulong, string> wsOutput, IReadOnlyDictionary<string, NiriOutputInfo> outputs)
    {
        var ws = w["workspace_id"]?.GetValue<ulong>();
        var layout = w["layout"];
        var size = layout?["window_size"] is JsonArray ws2 ? (ws2[0]!.GetValue<int>(), ws2[1]!.GetValue<int>()) : (0, 0);
        PixelRect? rect = null;
        var outputName = ws is { } w0 && wsOutput.TryGetValue(w0, out var n0) ? n0 : null;
        if (ws is not { } wid || !wsOutput.TryGetValue(wid, out var on) || !outputs.TryGetValue(on, out var o))
        {
        }
        else if (layout?["tile_pos_in_workspace_view"] is not JsonArray tile || layout["window_offset_in_tile"] is not JsonArray off)
        {
            // niri 只给浮动窗口报位置；平铺窗口只有占满整个输出（全屏 / 无边框全屏）时能确定位置
            if (size.Item1 == o.Width && size.Item2 == o.Height)
            {
                rect = new PixelRect((int)Math.Round(o.X * o.Scale), (int)Math.Round(o.Y * o.Scale),
                    (int)Math.Round(o.Width * o.Scale), (int)Math.Round(o.Height * o.Scale));
            }
        }
        else
        {
            // 逻辑坐标：输出左上角 + 平铺块在工作区视图中的位置 + 窗口在平铺块中的偏移；再乘输出缩放得到物理像素
            var lx = tile[0]!.GetValue<double>() + off[0]!.GetValue<double>();
            var ly = tile[1]!.GetValue<double>() + off[1]!.GetValue<double>();
            rect = new PixelRect((int)Math.Round((o.X + lx) * o.Scale), (int)Math.Round((o.Y + ly) * o.Scale),
                (int)Math.Round(size.Item1 * o.Scale), (int)Math.Round(size.Item2 * o.Scale));
        }

        return new NiriWindowInfo(w["id"]!.GetValue<ulong>(), w["title"]?.GetValue<string>() ?? "", w["app_id"]?.GetValue<string>() ?? "",
            w["pid"]?.GetValue<int>() ?? 0, ws, w["is_focused"]?.GetValue<bool>() ?? false, w["is_floating"]?.GetValue<bool>() ?? false,
            rect, size, outputName);
    }
}
