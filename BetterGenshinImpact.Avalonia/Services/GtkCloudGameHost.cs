using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using BetterGenshinImpact.Core.Input.Backends.WebSdk;
using BetterGenshinImpact.Host.Common;
using BetterGenshinImpact.Infrastructure.Storage;
using Serilog;
using ILogger = Serilog.ILogger;

namespace BetterGenshinImpact.Avalonia.Services;

/// <summary>
/// 云原神（网页版）的 Linux 宿主（对应 Windows 的 CloudWebHostWindow）：一个 1920x1080 的 WebKitGTK 顶层窗口，
/// 文档创建时注入与 Windows 相同的输入 SDK（ys-input-inject.js）和分发脚本；截图器截取这个窗口，
/// 输入经 <see cref="IWebInputBridge"/> 在页面里调用 SDK（RTC 数据通道发往云端），不经 XTest，也不需要前台。
/// 网站数据（登录状态）保存在 WebKit/CloudGame 目录，与其他网页隔离。
/// GTK 调用都在 Avalonia 的 GTK 线程上执行（与 NativeWebView、HTML 遮罩共用）
/// </summary>
internal sealed unsafe partial class GtkCloudGameHost : ICloudGamePageHost
{
    private const string MessageHandler = "bgiCloud";
    private const int ClientWidth = 1920;
    private const int ClientHeight = 1080;

    /// <summary>已就绪后连续多少次状态未就绪才判定为断开（与 Windows 相同），避免单次查询失败就停掉任务</summary>
    private const int DisconnectThreshold = 3;

    /// <summary>
    /// 分发脚本（只在顶层文档生效）：按到达顺序串行调用 window.__ysInputInject 上的函数，失败时回报；
    /// 每秒回报一次 SDK 状态（RTC 数据通道、游戏数据是否开始）
    /// </summary>
    private const string BootstrapScript = """
        (() => {
          if (window.top !== window || window.__bgiCloudBridge) return;
          window.__bgiCloudBridge = true;
          const post = m => { try { window.webkit.messageHandlers.bgiCloud.postMessage(JSON.stringify(m)); } catch (e) { } };
          let queue = Promise.resolve();
          window.__bgiCloudInvoke = ({ method, args = [] }) => {
            queue = queue
              .then(() => {
                const sdk = window.__ysInputInject;
                if (!sdk || !Object.hasOwn(sdk, method) || typeof sdk[method] !== "function") {
                  throw new Error(`unknown sdk method: ${method}`);
                }
                return sdk[method](...args);
              })
              .catch(e => post({ channel: "bgi.input.error", method, error: String(e?.message ?? e) }));
          };
          setInterval(() => {
            let s;
            try {
              const st = window.__ysInputInject?.status();
              s = st ? { rtc: st.rtcDataChannelState ?? null, started: st.gameDataStarted ?? null } : { error: "sdk not installed" };
            } catch (e) {
              s = { error: String(e?.message ?? e) };
            }
            post({ channel: "bgi.status", ...s });
          }, 1000);
        })();
        """;

    private static readonly ILogger Log = Serilog.Log.ForContext("SourceContext", "CloudGame");
    private static readonly object Gate = new();
    private static GtkCloudGameHost? _instance;
    private static string? _sdkScript;

    private nint _window;
    private nint _view;
    private nint _xid;
    private volatile bool _closed = true;
    private volatile bool _ready;
    private int _notReady;
    private string? _lastReason;
    private string? _url;
    private TaskCompletionSource<bool> _readyOrClosed = NewTcs();
    private readonly GtkWebInputBridge _bridge;
    private long _lastErrorLog;

    public GtkCloudGameHost()
    {
        _instance = this;
        _bridge = new GtkWebInputBridge(this);
    }

    private static TaskCompletionSource<bool> NewTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static T OnGtk<T>(Func<T> callback) =>
        global::Avalonia.X11.Interop.GtkInteropHelper.RunOnGlibThread(callback).GetAwaiter().GetResult();

    private static void OnGtkAsync(Action callback) =>
        _ = global::Avalonia.X11.Interop.GtkInteropHelper.RunOnGlibThread(() =>
        {
            try
            {
                callback();
            }
            catch (Exception e)
            {
                Log.Debug(e, "云原神窗口 GTK 操作失败");
            }

            return 0;
        });

    public Task<CloudGamePage?> OpenAndWaitReadyAsync(string url, CancellationToken ct)
    {
        Task<bool> wait;
        lock (Gate)
        {
            EnsureWindow(url, false);
            if (_ready)
            {
                return Task.FromResult<CloudGamePage?>(CreatePage());
            }

            wait = _readyOrClosed.Task;
        }

        return wait.WaitAsync(ct).ContinueWith(t => t.Result ? CreatePage() : null, ct,
            TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    private CloudGamePage CreatePage() => new(_xid, _bridge, () => _ready && !_closed);

    public void ShowForInteraction(string url)
    {
        lock (Gate)
        {
            EnsureWindow(url, true);
        }
    }

    public void Close()
    {
        if (_closed)
        {
            return;
        }

        // 页面销毁前释放所有按下的按键
        try
        {
            _bridge.Invoke("releaseAll");
        }
        catch (InvalidOperationException)
        {
        }

        OnGtkAsync(() =>
        {
            if (_window != 0)
            {
                gtk_widget_destroy(_window);
            }
        });
    }

    /// <summary>窗口未打开时新建并加载页面（调用方持有 Gate）</summary>
    private void EnsureWindow(string url, bool present)
    {
        if (!WebKitAvailable())
        {
            throw new InvalidOperationException("未安装 WebKitGTK（libwebkit2gtk-4.1），无法打开云原神窗口");
        }

        if (!_closed)
        {
            if (present)
            {
                OnGtkAsync(() => gtk_window_present(_window));
            }

            return;
        }

        _url = url;
        _ready = false;
        _notReady = 0;
        _lastReason = null;
        _readyOrClosed = NewTcs();
        _closed = false;
        _xid = OnGtk(() => Create(url));
        Log.Information("云原神窗口已打开：0x{Xid:x} {Url}", _xid, url);
    }

    private nint Create(string url)
    {
        var context = CreateContext();
        var ucm = webkit_user_content_manager_new();
        webkit_user_content_manager_register_script_message_handler(ucm, MessageHandler);
        g_signal_connect_data(ucm, "script-message-received::" + MessageHandler, (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnScriptMessage, 0, 0, 0);
        // SDK 本身不改动，外面包一层只在顶层文档执行（与 Windows 相同）
        AddScript(ucm, $"if (window.top === window) {{\n{SdkScript()}\n}}");
        AddScript(ucm, BootstrapScript);

        var view = g_object_new(webkit_web_view_get_type(), "web-context", context, "user-content-manager", ucm, 0);
        g_object_unref(context);
        var settings = webkit_web_view_get_settings(view);
        webkit_settings_set_enable_developer_extras(settings, false);
        // 页面在后台时不降低帧率与计时器频率（对应 Windows 的禁用遮挡检测和后台降频）
        g_signal_connect_data(view, "web-process-terminated", (nint)(delegate* unmanaged<nint, int, nint, void>)&OnWebProcessTerminated, 0, 0, 0);

        var window = gtk_window_new(0);
        gtk_window_set_title(window, "云原神 · BetterGI");
        var scale = Math.Max(1, gtk_widget_get_scale_factor(window));
        // 客户区锁定为 1080P（与 Windows 宿主相同）
        gtk_widget_set_size_request(view, ClientWidth / scale, ClientHeight / scale);
        gtk_window_set_resizable(window, false);
        gtk_container_add(window, view);
        g_signal_connect_data(window, "destroy", (nint)(delegate* unmanaged<nint, nint, void>)&OnDestroy, 0, 0, 0);
        gtk_widget_show_all(window);
        _window = window;
        _view = view;
        webkit_web_view_load_uri(view, url);
        return gdk_x11_window_get_xid(gtk_widget_get_window(window));
    }

    private static void AddScript(nint ucm, string source)
    {
        var script = webkit_user_script_new(source, 1 /* TOP_FRAME */, 0 /* DOCUMENT_START */, 0, 0);
        webkit_user_content_manager_add_script(ucm, script);
        webkit_user_script_unref(script);
    }

    private static nint CreateContext()
    {
        var data = Path.Combine(AppPaths.Root, "WebKit", "CloudGame");
        Directory.CreateDirectory(data);
        var manager = webkit_website_data_manager_new("base-data-directory", Path.Combine(data, "data"), "base-cache-directory", Path.Combine(data, "cache"), 0);
        // 登录状态（Cookie）持久保存
        webkit_cookie_manager_set_persistent_storage(webkit_website_data_manager_get_cookie_manager(manager), Path.Combine(data, "cookies.sqlite"), 1 /* SQLITE */);
        var context = webkit_web_context_new_with_website_data_manager(manager);
        g_object_unref(manager);
        return context;
    }

    private static string SdkScript()
    {
        if (_sdkScript is { } cached)
        {
            return cached;
        }

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BetterGenshinImpact.Resources.JavaScript.ys-input-inject.js")
                           ?? throw new InvalidOperationException("缺少云原神输入 SDK 资源 ys-input-inject.js");
        using var reader = new StreamReader(stream);
        return _sdkScript = reader.ReadToEnd();
    }

    /// <summary>在页面里调用 SDK（GTK 线程上按投递顺序执行）</summary>
    internal void Post(string method, string json)
    {
        if (_closed)
        {
            throw new InvalidOperationException("云原神窗口已关闭");
        }

        OnGtkAsync(() =>
        {
            if (_view != 0 && !_closed)
            {
                webkit_web_view_evaluate_javascript(_view, "window.__bgiCloudInvoke && window.__bgiCloudInvoke(" + json + ")", -1, 0, 0, 0, 0, 0);
            }
        });
    }

    private void OnMessage(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        var channel = root.TryGetProperty("channel", out var c) ? c.GetString() : null;
        if (channel == "bgi.input.error")
        {
            // 同类错误 10 秒内只记录一次，避免每次按键都刷日志
            var now = Environment.TickCount64;
            if (now - _lastErrorLog > 10_000)
            {
                _lastErrorLog = now;
                Log.Warning("云原神输入 SDK 调用失败：{Method} {Error}", root.GetProperty("method").GetString(), root.GetProperty("error").GetString());
            }

            return;
        }

        if (channel != "bgi.status")
        {
            return;
        }

        var rtc = root.TryGetProperty("rtc", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
        var started = root.TryGetProperty("started", out var s) && s.ValueKind == JsonValueKind.True;
        var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
        UpdateReady(rtc == "open" && started, error ?? $"rtc={rtc ?? "null"} started={started}");
    }

    private void UpdateReady(bool isReady, string reason)
    {
        if (isReady)
        {
            _notReady = 0;
            _lastReason = null;
            if (!_ready)
            {
                _ready = true;
                Log.Information("云原神已进入游戏");
                _readyOrClosed.TrySetResult(true);
            }

            return;
        }

        if (_ready && ++_notReady < DisconnectThreshold)
        {
            return;
        }

        if (_ready)
        {
            _ready = false;
            _readyOrClosed = NewTcs();
            Log.Warning("云原神连接已断开：{Reason}", reason);
        }
        else if (reason != _lastReason)
        {
            Log.Debug("云原神未就绪：{Reason}", reason);
        }

        _lastReason = reason;
    }

    private void OnClosed()
    {
        _closed = true;
        _ready = false;
        _window = 0;
        _view = 0;
        _xid = 0;
        _readyOrClosed.TrySetResult(false);
        Log.Information("云原神窗口已关闭");
    }

    [UnmanagedCallersOnly]
    private static void OnScriptMessage(nint manager, nint jsResult, nint userData)
    {
        try
        {
            var value = webkit_javascript_result_get_js_value(jsResult);
            var text = jsc_value_to_string(value);
            var raw = Marshal.PtrToStringUTF8(text) ?? "";
            g_free(text);
            _instance?.OnMessage(raw);
        }
        catch (Exception e)
        {
            Log.Debug(e, "云原神页面消息处理异常");
        }
    }

    [UnmanagedCallersOnly]
    private static void OnDestroy(nint widget, nint userData)
    {
        if (_instance is { } host && host._window == widget)
        {
            host.OnClosed();
        }
    }

    [UnmanagedCallersOnly]
    private static void OnWebProcessTerminated(nint view, int reason, nint userData) =>
        Log.Warning("云原神网页进程已退出（原因 {Reason}），请关闭云原神窗口后重新启动截图器", reason);

    private static bool? _webKitAvailable;

    private static bool WebKitAvailable() => _webKitAvailable ??=
        NativeLibrary.TryLoad(WebKit, out var handle) && NativeLibrary.TryGetExport(handle, "webkit_web_view_evaluate_javascript", out _);

    /// <summary>页面 SDK 调用桥：投递到 GTK 线程，按先后顺序在页面里执行，不等待结果</summary>
    private sealed class GtkWebInputBridge(GtkCloudGameHost host) : IWebInputBridge
    {
        public void Invoke(string method, params object[] args)
        {
            ArgumentException.ThrowIfNullOrEmpty(method);
            host.Post(method, JsonSerializer.Serialize(new { method, args }));
        }
    }

    private const string Gtk = "libgtk-3.so.0";
    private const string Gdk = "libgdk-3.so.0";
    private const string WebKit = "libwebkit2gtk-4.1.so.0";
    private const string Jsc = "libjavascriptcoregtk-4.1.so.0";
    private const string GLib = "libglib-2.0.so.0";
    private const string GObject = "libgobject-2.0.so.0";

    [LibraryImport(Gtk)] private static partial nint gtk_window_new(int type);
    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)] private static partial void gtk_window_set_title(nint window, string title);
    [LibraryImport(Gtk)] private static partial void gtk_window_set_resizable(nint window, [MarshalAs(UnmanagedType.Bool)] bool value);
    [LibraryImport(Gtk)] private static partial void gtk_window_present(nint window);
    [LibraryImport(Gtk)] private static partial int gtk_widget_get_scale_factor(nint widget);
    [LibraryImport(Gtk)] private static partial void gtk_widget_set_size_request(nint widget, int width, int height);
    [LibraryImport(Gtk)] private static partial void gtk_container_add(nint container, nint widget);
    [LibraryImport(Gtk)] private static partial void gtk_widget_show_all(nint widget);
    [LibraryImport(Gtk)] private static partial void gtk_widget_destroy(nint widget);
    [LibraryImport(Gtk)] private static partial nint gtk_widget_get_window(nint widget);
    [LibraryImport(Gdk)] private static partial nint gdk_x11_window_get_xid(nint window);
    [LibraryImport(WebKit)] private static partial nint webkit_web_view_get_type();
    [LibraryImport(WebKit)] private static partial nint webkit_web_view_get_settings(nint view);
    [LibraryImport(WebKit)] private static partial void webkit_settings_set_enable_developer_extras(nint settings, [MarshalAs(UnmanagedType.Bool)] bool value);
    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)] private static partial void webkit_web_view_load_uri(nint view, string uri);
    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)] private static partial void webkit_web_view_evaluate_javascript(nint view, string script, nint length, nint world, nint sourceUri, nint cancellable, nint callback, nint userData);
    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)] private static partial nint webkit_website_data_manager_new(string k1, string v1, string k2, string v2, nint end);
    [LibraryImport(WebKit)] private static partial nint webkit_website_data_manager_get_cookie_manager(nint manager);
    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)] private static partial void webkit_cookie_manager_set_persistent_storage(nint manager, string filename, int storage);
    [LibraryImport(WebKit)] private static partial nint webkit_web_context_new_with_website_data_manager(nint manager);
    [LibraryImport(WebKit)] private static partial nint webkit_user_content_manager_new();
    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool webkit_user_content_manager_register_script_message_handler(nint manager, string name);
    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)] private static partial nint webkit_user_script_new(string source, int frames, int time, nint allowList, nint blockList);
    [LibraryImport(WebKit)] private static partial void webkit_user_content_manager_add_script(nint manager, nint script);
    [LibraryImport(WebKit)] private static partial void webkit_user_script_unref(nint script);
    [LibraryImport(WebKit)] private static partial nint webkit_javascript_result_get_js_value(nint result);
    [LibraryImport(Jsc)] private static partial nint jsc_value_to_string(nint value);
    [LibraryImport(GObject, StringMarshalling = StringMarshalling.Utf8)] private static partial nint g_object_new(nint type, string p1, nint v1, string p2, nint v2, nint end);
    [LibraryImport(GObject, StringMarshalling = StringMarshalling.Utf8)] private static partial nuint g_signal_connect_data(nint instance, string signal, nint handler, nint data, nint destroy, int flags);
    [LibraryImport(GObject)] private static partial void g_object_unref(nint obj);
    [LibraryImport(GLib)] private static partial void g_free(nint memory);
}
