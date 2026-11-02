using System.Diagnostics;
using OpenCvSharp;
using Tmds.DBus.Protocol;

namespace BetterGenshinImpact.Platform.Linux.Wayland;

/// <summary>ScreenCast portal 选中的一路视频流</summary>
/// <param name="NodeId">PipeWire 节点 id</param>
/// <param name="Width">流的大小（portal 提供时），未知为 0</param>
/// <param name="SourceType">1 = 显示器，2 = 窗口</param>
/// <param name="X">流在合成器坐标中的位置（只有显示器来源提供；逻辑像素）</param>
public sealed record ScreenCastStream(uint NodeId, int Width, int Height, uint SourceType, int X = 0, int Y = 0, bool HasPosition = false);

/// <summary>
/// xdg-desktop-portal ScreenCast 会话（CreateSession → SelectSources → Start）。
/// 首次会弹出合成器 / portal 的选择对话框（niri 下由 xdg-desktop-portal-gnome 提供，可选窗口或输出）；
/// persist_mode=2 时 portal 返回 restore_token，保存到 <see cref="RestoreTokenPath"/>，下次直接恢复同一来源、不再询问
/// </summary>
public sealed class ScreenCastPortalSession : IAsyncDisposable
{
    private const string Bus = "org.freedesktop.portal.Desktop";
    private const string Path = "/org/freedesktop/portal/desktop";
    private const string Iface = "org.freedesktop.portal.ScreenCast";
    private readonly DBusConnection _conn;
    private string? _session;

    private ScreenCastPortalSession(DBusConnection conn) => _conn = conn;

    /// <summary>restore_token 保存位置：$XDG_CONFIG_HOME/bettergi/screencast-restore-token</summary>
    public static string RestoreTokenPath =>
        System.IO.Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x
            ? x
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "bettergi", "screencast-restore-token");

    public IReadOnlyList<ScreenCastStream> Streams { get; private set; } = [];

    /// <summary>portal 返回的新 restore_token（已写入文件）；不支持持久化时为 null</summary>
    public string? RestoreToken { get; private set; }

    /// <summary>建立会话并开始投屏，等待用户在对话框中选择（<paramref name="timeout"/> 内）</summary>
    /// <param name="sourceTypes">1 显示器、2 窗口，可按位或</param>
    public static async Task<ScreenCastPortalSession> StartAsync(uint sourceTypes = 3, bool useSavedToken = true, TimeSpan? timeout = null)
    {
        var address = DBusAddress.Session ?? throw new InvalidOperationException("没有 D-Bus 会话总线（DBUS_SESSION_BUS_ADDRESS），无法使用 ScreenCast portal");
        var conn = new DBusConnection(address);
        var s = new ScreenCastPortalSession(conn);
        try
        {
            await conn.ConnectAsync();
            await s.RunAsync(sourceTypes, useSavedToken, timeout ?? TimeSpan.FromMinutes(2));
            return s;
        }
        catch
        {
            await s.DisposeAsync();
            throw;
        }
    }

    private string RequestPath(string token) =>
        "/org/freedesktop/portal/desktop/request/" + _conn.UniqueName!.TrimStart(':').Replace('.', '_') + "/" + token;

    // MessageWriter 是结构体，必须按 ref 传，否则写进副本、消息体为空（总线会直接断开连接）
    private delegate void ArgsWriter(ref MessageWriter w, string token);

    private static string NewToken() => "bgi" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>调用一个返回 Request 的方法，并等待 Response 信号</summary>
    private async Task<Dictionary<string, VariantValue>> RequestAsync(string member, string signature, ArgsWriter writeArgs, TimeSpan timeout)
    {
        var token = NewToken();
        var tcs = new TaskCompletionSource<(uint, Dictionary<string, VariantValue>)>(TaskCreationOptions.RunContinuationsAsynchronously);
#pragma warning disable CS0618 // 新重载需要 SynchronizationContext 参数，行为相同
        using var watch = await _conn.WatchSignalAsync(Bus, RequestPath(token), "org.freedesktop.portal.Request", "Response",
            static (Message m, object? _) =>
            {
                var r = m.GetBodyReader();
                var code = r.ReadUInt32();
                return (code, r.ReadDictionaryOfStringToVariantValue());
            },
            (Exception? e, (uint, Dictionary<string, VariantValue>) v) =>
            {
                if (e is not null) tcs.TrySetException(e);
                else tcs.TrySetResult(v);
            }, null, false, ObserverFlags.None);
#pragma warning restore CS0618

        // CreateMessage 接管 writer 的缓冲区（与 Tmds.DBus 生成代码一致，不再 Dispose writer）
        var writer = _conn.GetMessageWriter();
        writer.WriteMethodCallHeader(Bus, Path, Iface, member, signature, MessageFlags.None);
        writeArgs(ref writer, token);
        var msg = writer.CreateMessage();

        await _conn.CallMethodAsync(msg, static (Message m, object? _) => m.GetBodyReader().ReadObjectPathAsString(), null);
        var done = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
        if (done != tcs.Task) throw new TimeoutException($"ScreenCast portal {member} 等待用户选择超时");
        var (response, results) = await tcs.Task;
        return response switch
        {
            0 => results,
            1 => throw new OperationCanceledException("用户取消了屏幕共享"),
            _ => throw new InvalidOperationException($"ScreenCast portal {member} 失败（response={response}）"),
        };
    }

    private static void WriteOptions(ref MessageWriter w, string token, Dictionary<string, VariantValue>? more = null)
    {
        var options = new Dictionary<string, VariantValue> { ["handle_token"] = VariantValue.String(token) };
        if (more is not null)
        {
            foreach (var (k, v) in more) options[k] = v;
        }

        w.WriteDictionary(options);
    }

    private async Task RunAsync(uint sourceTypes, bool useSavedToken, TimeSpan timeout)
    {
        // xdg-desktop-portal-wlr 等后端只支持显示器，请求里带“窗口”会被直接拒绝，所以先与 AvailableSourceTypes 取交集
        var available = await GetAvailableSourceTypesAsync();
        if (available != 0 && (sourceTypes & available) != 0) sourceTypes &= available;

        var created = await RequestAsync("CreateSession", "a{sv}", (ref MessageWriter w, string token) => WriteOptions(ref w, token, new()
        {
            ["session_handle_token"] = VariantValue.String(NewToken()),
        }), timeout);
        _session = created["session_handle"].GetString();

        var saved = useSavedToken && File.Exists(RestoreTokenPath) ? File.ReadAllText(RestoreTokenPath).Trim() : null;
        var select = new Dictionary<string, VariantValue>
        {
            ["types"] = VariantValue.UInt32(sourceTypes),
            ["multiple"] = VariantValue.Bool(false),
            ["cursor_mode"] = VariantValue.UInt32(1), // 隐藏光标
            ["persist_mode"] = VariantValue.UInt32(2), // 一直有效，直到用户撤销
        };
        if (!string.IsNullOrEmpty(saved)) select["restore_token"] = VariantValue.String(saved);
        var session = _session;
        await RequestAsync("SelectSources", "oa{sv}", (ref MessageWriter w, string token) =>
        {
            w.WriteObjectPath(session);
            WriteOptions(ref w, token, select);
        }, timeout);

        var started = await RequestAsync("Start", "osa{sv}", (ref MessageWriter w, string token) =>
        {
            w.WriteObjectPath(session);
            w.WriteString("");
            WriteOptions(ref w, token);
        }, timeout);

        Streams = ParseStreams(started.TryGetValue("streams", out var streams) ? streams : default);
        if (Streams.Count == 0) throw new InvalidOperationException("ScreenCast portal 没有返回视频流");
        if (started.TryGetValue("restore_token", out var rt) && rt.GetString() is { Length: > 0 } t)
        {
            RestoreToken = t;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(RestoreTokenPath)!);
            File.WriteAllText(RestoreTokenPath, t);
        }
    }

    /// <summary>读取 portal 的 AvailableSourceTypes（1 显示器、2 窗口、4 虚拟）；失败返回 0</summary>
    private async Task<uint> GetAvailableSourceTypesAsync()
    {
        try
        {
            var w = _conn.GetMessageWriter();
            w.WriteMethodCallHeader(Bus, Path, "org.freedesktop.DBus.Properties", "Get", "ss", MessageFlags.None);
            w.WriteString(Iface);
            w.WriteString("AvailableSourceTypes");
            var msg = w.CreateMessage();
            return await _conn.CallMethodAsync(msg, static (Message m, object? _) => m.GetBodyReader().ReadVariantValue().GetUInt32(), null);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return 0;
        }
    }

    internal static IReadOnlyList<ScreenCastStream> ParseStreams(VariantValue streams)
    {
        var list = new List<ScreenCastStream>();
        if (streams.Type != VariantValueType.Array) return list;
        for (var i = 0; i < streams.Count; i++)
        {
            var item = streams.GetItem(i);
            var node = item.GetItem(0).GetUInt32();
            var props = item.GetItem(1);
            int w = 0, h = 0, x = 0, y = 0;
            var hasPosition = false;
            uint type = 0;
            for (var j = 0; j < props.Count; j++)
            {
                var (k, v) = props.GetDictionaryEntry(j);
                var key = k.GetString();
                var value = v.Type == VariantValueType.Variant ? v.GetVariantValue() : v;
                if (key == "size")
                {
                    w = value.GetItem(0).GetInt32();
                    h = value.GetItem(1).GetInt32();
                }
                else if (key == "source_type")
                {
                    type = value.GetUInt32();
                }
                else if (key == "position")
                {
                    x = value.GetItem(0).GetInt32();
                    y = value.GetItem(1).GetInt32();
                    hasPosition = true;
                }
            }

            list.Add(new ScreenCastStream(node, w, h, type, x, y, hasPosition));
        }

        return list;
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            try
            {
                var w = _conn.GetMessageWriter();
                w.WriteMethodCallHeader(Bus, _session, "org.freedesktop.portal.Session", "Close", null, MessageFlags.None);
                var msg = w.CreateMessage();
                await _conn.CallMethodAsync(msg);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
            }

            _session = null;
        }

        _conn.Dispose();
    }
}

/// <summary>
/// ScreenCast portal + PipeWire 截图。PipeWire 帧经 GStreamer（gst-launch-1.0 pipewiresrc → videoconvert → BGRA，
/// 需要 gstreamer1.0-pipewire / gstreamer1.0-tools / gstreamer1.0-plugins-base）从标准输出读出；
/// 这样不必在 C# 里实现 SPA 格式协商（niri 只提供 DMA-BUF，由 pipewiresrc 负责映射 / 拷贝）。
/// 选择对话框在第一次 Start 时出现，之后用 restore_token 恢复
/// </summary>
public sealed class PortalScreenCastCapture : IGameCapture
{
    public const string MethodName = "Portal";
    private readonly NiriIpc? _niri;
    private ScreenCastPortalSession? _session;
    private Process? _gst;
    private Thread? _reader;
    private byte[]? _latest;
    private int _width, _height;
    private volatile bool _stop;
    private readonly object _gate = new();

    private readonly Func<IPlatformWindow, WaylandOverlayPlacement?>? _locate;
    private IPlatformWindow? _window;
    private ScreenCastStream? _stream;
    private NiriOutputInfo? _streamOutput;
    private double _streamScale = 1;
    private PixelRect? _crop;
    private long _cropAt;

    private readonly Func<IPlatformWindow, Mat?>? _grabWindow;
    private PixelRect? _matched;
    private long _matchedAt;

    /// <param name="locate">游戏窗口在哪个输出、相对输出的逻辑位置（<see cref="WaylandPlatformServices.LocateOverlay"/>）；用于显示器来源时裁剪到游戏</param>
    /// <param name="grabWindow">直接截游戏窗口本身的画面（X 窗口经 XGetImage）；niri 不报告位置（平铺窗口）时用它在整屏画面里比对定位</param>
    public PortalScreenCastCapture(NiriIpc? niri = null, Func<IPlatformWindow, WaylandOverlayPlacement?>? locate = null, Func<IPlatformWindow, Mat?>? grabWindow = null)
    {
        _niri = niri;
        _locate = locate;
        _grabWindow = grabWindow;
    }

    /// <summary>当前裁剪区域的来源："niri"（IPC 窗口位置）、"画面比对"（平铺窗口兜底），未裁剪时为 null</summary>
    public string? CropSource { get; private set; }

    /// <summary>portal 选中的流（未启动时为 null）</summary>
    public ScreenCastStream? Stream => _stream;

    /// <summary>显示器来源时当前的裁剪区域（帧像素）；窗口来源或不裁剪时为 null</summary>
    public PixelRect? CropRect => _crop;

    /// <summary>是否把显示器来源裁剪到游戏窗口</summary>
    public bool CropsToWindow => _stream is { SourceType: 1 } && _locate is not null && !_noCrop;

    private bool _noCrop;

    public bool IsCapturing => _gst is { HasExited: false };

    public CaptureCapabilities Capabilities { get; } = new(MethodName, CanCaptureOccluded: true, NeedsUserConsent: true);

    /// <summary>最近一次错误（GStreamer 退出、portal 拒绝等）</summary>
    public string? LastError { get; private set; }

    /// <summary>portal 与 GStreamer 是否齐备（诊断用）</summary>
    public static string ProbeAvailability()
    {
        var bus = DBusAddress.Session is null ? "无会话总线" : "会话总线可用";
        var gst = FindOnPath("gst-launch-1.0") is null ? "缺 gst-launch-1.0（gstreamer1.0-tools）" : "gst-launch-1.0 可用";
        var plugin = new[] { "/usr/lib/x86_64-linux-gnu/gstreamer-1.0/libgstpipewire.so", "/usr/lib/gstreamer-1.0/libgstpipewire.so", "/usr/lib64/gstreamer-1.0/libgstpipewire.so" }
            .Any(File.Exists) ? "pipewiresrc 可用" : "缺 pipewiresrc（gstreamer1.0-pipewire）";
        return $"{bus}，{gst}，{plugin}";
    }

    private static string? FindOnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':').Select(d => System.IO.Path.Combine(d, exe)).FirstOrDefault(File.Exists);

    /// <param name="settings">
    /// "portalSourceTypes"（uint，1 显示器 / 2 窗口 / 3 都可）、"portalForget"（true 时忽略已保存的 restore_token）、
    /// "portalTimeoutSeconds"（等待用户选择的秒数）、"portalNoCrop"（true 时显示器来源不裁剪，诊断用）
    /// </param>
    public void Start(IPlatformWindow window, Dictionary<string, object>? settings = null)
    {
        Stop();
        uint types = settings?.TryGetValue("portalSourceTypes", out var t) == true ? Convert.ToUInt32(t) : 3u;
        var forget = settings?.TryGetValue("portalForget", out var f) == true && Convert.ToBoolean(f);
        var timeout = settings?.TryGetValue("portalTimeoutSeconds", out var s) == true ? TimeSpan.FromSeconds(Convert.ToDouble(s)) : TimeSpan.FromMinutes(2);
        _session = Task.Run(() => ScreenCastPortalSession.StartAsync(types, !forget, timeout)).GetAwaiter().GetResult();
        _noCrop = settings?.TryGetValue("portalNoCrop", out var nc) == true && Convert.ToBoolean(nc);
        var stream = _session.Streams[0];
        _stream = stream;
        _window = window;
        _crop = null;
        _cropAt = 0;
        if (stream.SourceType == 1)
        {
            // 显示器来源：流大小是该输出的逻辑大小，按输出缩放取物理分辨率（不丢清晰度），之后每帧裁剪到游戏窗口
            _streamOutput = FindStreamOutput(stream);
            _streamScale = _streamOutput?.Scale is > 0 and var sc ? sc : 1;
            _width = stream.Width > 0 ? (int)Math.Round(stream.Width * _streamScale) : _streamOutput is { } o ? (int)Math.Round(o.Width * _streamScale) : Math.Max(1, window.Viewport.Width);
            _height = stream.Height > 0 ? (int)Math.Round(stream.Height * _streamScale) : _streamOutput is { } o2 ? (int)Math.Round(o2.Height * _streamScale) : Math.Max(1, window.Viewport.Height);
        }
        else
        {
            // 窗口来源（或后端没说）：portal 没给大小时，退回窗口大小
            _streamOutput = null;
            _streamScale = 1;
            _width = stream.Width > 0 ? stream.Width : Math.Max(1, window.Viewport.Width);
            _height = stream.Height > 0 ? stream.Height : Math.Max(1, window.Viewport.Height);
        }

        StartPipeline(stream.NodeId);
    }

    private void StartPipeline(uint node)
    {
        var psi = new ProcessStartInfo("gst-launch-1.0")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in BuildPipelineArgs(node, _width, _height)) psi.ArgumentList.Add(a);
        _gst = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 gst-launch-1.0");
        _gst.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) LastError = e.Data;
        };
        _gst.BeginErrorReadLine();
        _stop = false;
        var output = _gst.StandardOutput.BaseStream;
        _reader = new Thread(() => ReadFrames(output)) { IsBackground = true, Name = "portal-screencast" };
        _reader.Start();
    }

    /// <summary>gst-launch-1.0 参数：PipeWire 节点 → BGRA、缩放到固定大小 → 标准输出</summary>
    public static IReadOnlyList<string> BuildPipelineArgs(uint node, int width, int height) =>
    [
        // stream-properties：WirePlumber 0.5 按 media.type 找连接目标，缺了会报 target not found、永远停在 PAUSED
        "-q", "pipewiresrc", $"path={node}", "always-copy=true", "do-timestamp=true",
        "stream-properties=p,media.type=Video,media.category=Capture,media.role=Screen", "!",
        "videoconvert", "!", "videoscale", "!", $"video/x-raw,format=BGRA,width={width},height={height}", "!",
        "fdsink", "fd=1", "sync=false",
    ];

    private void ReadFrames(Stream output)
    {
        var size = _width * _height * 4;
        var buf = new byte[size];
        try
        {
            while (!_stop)
            {
                var read = 0;
                while (read < size)
                {
                    var n = output.Read(buf, read, size - read);
                    if (n <= 0) return;
                    read += n;
                }

                lock (_gate)
                {
                    (_latest, buf) = (buf, _latest ?? new byte[size]);
                }
            }
        }
        catch (IOException e)
        {
            LastError = e.Message;
        }
    }

    public GameCaptureFrame? Capture()
    {
        PixelRect? crop = null;
        if (CropsToWindow)
        {
            crop = CurrentCrop();
            if (crop is null) return null; // 游戏不在共享的显示器上，或位置未知（平铺窗口）
        }

        lock (_gate)
        {
            if (_latest is null) return null;
            var mat = new Mat(_height, _width, MatType.CV_8UC4);
            System.Runtime.InteropServices.Marshal.Copy(_latest, 0, mat.Data, _latest.Length);
            if (crop is not { } c) return new GameCaptureFrame(mat);
            using (mat)
            {
                using var roi = new Mat(mat, new Rect(c.X, c.Y, c.Width, c.Height));
                return new GameCaptureFrame(roi.Clone());
            }
        }
    }

    /// <summary>显示器来源对应的 niri 输出：位置与流位置相同的输出；portal 没给位置时取唯一的输出</summary>
    private NiriOutputInfo? FindStreamOutput(ScreenCastStream stream)
    {
        try
        {
            var outputs = _niri?.Outputs() ?? [];
            if (stream.HasPosition)
            {
                return outputs.FirstOrDefault(o => o.X == stream.X && o.Y == stream.Y && (stream.Width <= 0 || o.Width == stream.Width))
                       ?? outputs.FirstOrDefault(o => o.X == stream.X && o.Y == stream.Y);
            }

            return outputs.Count == 1 ? outputs[0] : null;
        }
        catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or InvalidOperationException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>每 500 ms 按 niri 报告的窗口位置更新一次裁剪区域</summary>
    private PixelRect? CurrentCrop()
    {
        var now = Environment.TickCount64;
        if (_cropAt != 0 && now - _cropAt < 500) return _crop;
        _cropAt = now;
        var p = _window is null ? null : _locate!(_window);
        var crop = p is null ? null : ComputeCrop(_stream!, _streamOutput, p, _width, _height);
        CropSource = crop is null ? null : "niri";
        if (p is null && MatchWindow() is { } matched)
        {
            // niri 不报告平铺窗口的位置：用游戏窗口本身的画面在整屏画面里比对定位
            crop = matched;
            CropSource = "画面比对";
        }
        else if (crop is null && p is not null)
        {
            LastError = $"游戏窗口在输出 {p.Output}，不在共享的显示器（{_streamOutput?.Name ?? "未知"}）上；请在门户对话框里选择游戏所在的显示器或直接选择游戏窗口";
        }
        else if (p is null)
        {
            LastError = "拿不到游戏窗口在屏幕上的位置（niri 只报告浮动和全屏窗口的位置），与整屏画面比对也没有找到游戏，无法从整个显示器的画面里裁出游戏；" +
                        "请把游戏设为浮动或全屏，或在门户对话框里直接选择游戏窗口";
        }

        _crop = crop;
        return crop;
    }

    /// <summary>平铺窗口兜底：每 2 秒用游戏窗口画面在最新整屏帧里比对一次位置</summary>
    private PixelRect? MatchWindow()
    {
        if (_grabWindow is null || _window is null) return null;
        var now = Environment.TickCount64;
        if (_matchedAt != 0 && now - _matchedAt < 2000) return _matched;
        _matchedAt = now;
        using var win = _grabWindow(_window);
        if (win is null || win.Empty()) return _matched = null;
        Mat screen;
        lock (_gate)
        {
            if (_latest is null) return _matched = null;
            screen = new Mat(_height, _width, MatType.CV_8UC4);
            System.Runtime.InteropServices.Marshal.Copy(_latest, 0, screen.Data, _latest.Length);
        }

        using (screen)
        {
            return _matched = LocateByTemplate(screen, win, out _);
        }
    }

    /// <summary>
    /// 在整屏画面里找游戏窗口画面的位置：先缩小 4 倍粗定位（归一化相关系数 ≥ 0.8），再在原分辨率 ±8 像素内精定位（≥ 0.9）；找不到为 null
    /// </summary>
    public static PixelRect? LocateByTemplate(Mat screen, Mat window, out double score)
    {
        score = 0;
        if (window.Width < 32 || window.Height < 32 || window.Width > screen.Width || window.Height > screen.Height) return null;
        const int k = 4;
        using var sg = Gray(screen);
        using var wg = Gray(window);
        using var s4 = sg.Resize(new Size(sg.Width / k, sg.Height / k), 0, 0, InterpolationFlags.Area);
        using var w4 = wg.Resize(new Size(wg.Width / k, wg.Height / k), 0, 0, InterpolationFlags.Area);
        using var r4 = new Mat();
        Cv2.MatchTemplate(s4, w4, r4, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(r4, out _, out var coarse, out _, out var loc);
        if (coarse < 0.8) return null;
        const int pad = 8;
        var x0 = Math.Max(0, loc.X * k - pad);
        var y0 = Math.Max(0, loc.Y * k - pad);
        var x1 = Math.Min(sg.Width, loc.X * k + wg.Width + pad);
        var y1 = Math.Min(sg.Height, loc.Y * k + wg.Height + pad);
        if (x1 - x0 < wg.Width || y1 - y0 < wg.Height) return null;
        using var region = new Mat(sg, new Rect(x0, y0, x1 - x0, y1 - y0));
        using var r = new Mat();
        Cv2.MatchTemplate(region, wg, r, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(r, out _, out score, out _, out var fine);
        return score < 0.9 ? null : new PixelRect(x0 + fine.X, y0 + fine.Y, wg.Width, wg.Height);

        static Mat Gray(Mat m)
        {
            var g = new Mat();
            Cv2.CvtColor(m, g, m.Channels() == 4 ? ColorConversionCodes.BGRA2GRAY : ColorConversionCodes.BGR2GRAY);
            return g;
        }
    }

    /// <summary>
    /// 显示器来源的裁剪区域（帧像素）：窗口相对输出的逻辑位置 → 相对流的位置 × 帧/流比例，与帧求交；
    /// 窗口不在该输出上（或输出未知而窗口不在流的范围内）时为 null
    /// </summary>
    public static PixelRect? ComputeCrop(ScreenCastStream stream, NiriOutputInfo? streamOutput, WaylandOverlayPlacement window, int frameWidth, int frameHeight)
    {
        if (streamOutput is not null && window.Output is not null && window.Output != streamOutput.Name) return null;
        // 逻辑坐标：窗口相对输出 → 全局 → 相对流
        double ox = streamOutput?.X ?? 0, oy = streamOutput?.Y ?? 0;
        double sx = stream.HasPosition ? stream.X : ox, sy = stream.HasPosition ? stream.Y : oy;
        var lw = stream.Width > 0 ? stream.Width : streamOutput?.Width ?? frameWidth;
        var lh = stream.Height > 0 ? stream.Height : streamOutput?.Height ?? frameHeight;
        var kx = frameWidth / (double)lw;
        var ky = frameHeight / (double)lh;
        var x = (int)Math.Round((ox + window.X - sx) * kx);
        var y = (int)Math.Round((oy + window.Y - sy) * ky);
        var w = (int)Math.Round(window.Width * kx);
        var h = (int)Math.Round(window.Height * ky);
        var x0 = Math.Max(0, x);
        var y0 = Math.Max(0, y);
        var x1 = Math.Min(frameWidth, x + w);
        var y1 = Math.Min(frameHeight, y + h);
        return x1 - x0 < 8 || y1 - y0 < 8 ? null : new PixelRect(x0, y0, x1 - x0, y1 - y0);
    }

    public void Stop()
    {
        _stop = true;
        if (_gst is { } p)
        {
            try
            {
                if (!p.HasExited) p.Kill();
                p.WaitForExit(2000);
            }
            catch (InvalidOperationException)
            {
            }

            p.Dispose();
            _gst = null;
        }

        _reader?.Join(1000);
        _reader = null;
        if (_session is { } s)
        {
            Task.Run(() => s.DisposeAsync().AsTask()).Wait(2000);
            _session = null;
        }

        lock (_gate) _latest = null;
    }

    public void Dispose() => Stop();
}
