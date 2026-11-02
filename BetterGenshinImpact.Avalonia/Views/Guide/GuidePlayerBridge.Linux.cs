using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using BetterGenshinImpact.Service.Guide;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace BetterGenshinImpact.View.Controls.Guide;

/// <summary>
/// 悬浮小窗播放桥的 Linux 部分：网页引擎为 Avalonia NativeWebView 内的 WebKitGTK（对应 Windows 部分的 WebView2）。
/// 与 Windows 相同：两段页面脚本在文档创建时注入顶层页面（WebKit 用户脚本，DOCUMENT_START），页面经
/// <c>window.chrome.webview.postMessage</c> 发消息（这里由一段垫片转给 WebKit 消息处理器），
/// 命令以 JSON 参数调用 <c>window.__bgiGuide.exec</c>；只允许 http / https / about: 导航，新窗口在当前页打开，网页权限请求一律拒绝。
/// GTK / WebKit 调用都在 GLib 线程上执行，事件回到界面线程触发
/// </summary>
public sealed partial class GuidePlayerBridge : IGuidePlayer, IDisposable
{
    private const string MessageHandler = "bgiGuide";

    /// <summary>与 WebView2 相同形式的 window.chrome.webview.postMessage（对象按 JSON 发出，宿主按 JSON 解析）</summary>
    private const string ChromeWebViewShim = """
        (function () {
          if (window.chrome && window.chrome.webview) return;
          var h = window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.bgiGuide;
          if (!h) return;
          window.chrome = window.chrome || {};
          window.chrome.webview = {
            postMessage: function (m) { h.postMessage(JSON.stringify(m)); },
            addEventListener: function () {}, removeEventListener: function () {}
          };
        })();
        """;

    /// <summary>内容管理器 → 播放桥（消息回调按内容管理器找到对象）</summary>
    private static readonly ConcurrentDictionary<nint, GuidePlayerBridge> ByContentManager = new();

    /// <summary>网页视图 → 播放桥（网页进程结束回调）</summary>
    private static readonly ConcurrentDictionary<nint, GuidePlayerBridge> ByView = new();

    private readonly NativeWebView _webView;
    private nint _view;
    private nint _contentManager;
    private string? _pendingUrl;
    private double _zoom = 1;
    private bool _muted;
    private bool _disposed;

    public GuidePlayerBridge(NativeWebView webView)
    {
        _webView = webView;
        _webView.AdapterCreated += OnAdapterCreated;
        _webView.NavigationStarted += (_, e) =>
        {
            if (!IsAllowed(e.Request))
            {
                e.Cancel = true;
            }
        };
        // 与 Windows 版 NewWindowPolicy.NavigateInPlace 相同：新窗口在当前页打开
        _webView.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (e.Request is { } uri && IsAllowed(uri))
            {
                Navigate(uri.AbsoluteUri);
            }
        };
    }

    /// <summary>网页引擎是否已就绪（WebKitGTK 视图已创建并注入脚本）</summary>
    public bool IsReady => _view != 0 && !_disposed;

    /// <inheritdoc />
    public string? CurrentUrl => _disposed ? null : _webView.Source?.AbsoluteUri;

    /// <inheritdoc />
    public bool CanGoBack => !_disposed && _webView.CanGoBack;

    /// <inheritdoc />
    public bool IsMuted
    {
        get => _muted;
        set
        {
            _muted = value;
            var view = _view;
            if (view != 0)
            {
                OnGtk(() => webkit_web_view_set_is_muted(view, value));
            }
        }
    }

    private static bool IsAllowed(Uri? uri) =>
        uri is not null && (uri.Scheme is "http" or "https" || uri.OriginalString.StartsWith("about:", StringComparison.OrdinalIgnoreCase));

    private unsafe void OnAdapterCreated(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (_webView.TryGetPlatformHandle() is not IGtkWebViewPlatformHandle gtk)
        {
            Log.Warning("悬浮小窗：网页引擎不是 WebKitGTK，无法注入播放桥");
            EventRaised?.Invoke(this, new GuidePlayerEvent(GuidePlayerEventKind.InitializeFailed, "需要 WebKitGTK 4.1"));
            return;
        }

        var view = gtk.WebKitWebView;
        try
        {
            var muted = _muted;
            var zoom = _zoom;
            var manager = RunOnGtk(() =>
            {
                var ucm = webkit_web_view_get_user_content_manager(view);
                webkit_user_content_manager_register_script_message_handler(ucm, MessageHandler);
                g_signal_connect_data(ucm, "script-message-received::" + MessageHandler, (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnScriptMessage, 0, 0, 0);
                AddScript(ucm, ChromeWebViewShim);
                foreach (var script in Scripts.Value)
                {
                    AddScript(ucm, script);
                }

                g_signal_connect_data(view, "permission-request", (nint)(delegate* unmanaged<nint, nint, nint, int>)&OnPermissionRequest, 0, 0, 0);
                g_signal_connect_data(view, "web-process-terminated", (nint)(delegate* unmanaged<nint, int, nint, void>)&OnWebProcessTerminated, 0, 0, 0);
                webkit_web_view_set_zoom_level(view, zoom);
                webkit_web_view_set_is_muted(view, muted);
                return ucm;
            });
            _contentManager = manager;
            ByContentManager[manager] = this;
            ByView[view] = this;
            _view = view;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "悬浮小窗网页初始化失败");
            EventRaised?.Invoke(this, new GuidePlayerEvent(GuidePlayerEventKind.InitializeFailed, ex.Message));
            return;
        }

        if (_pendingUrl is { } url)
        {
            _pendingUrl = null;
            Navigate(url);
        }
    }

    private static void AddScript(nint ucm, string source)
    {
        var script = webkit_user_script_new(source, 1 /* TOP_FRAME */, 0 /* DOCUMENT_START */, 0, 0);
        webkit_user_content_manager_add_script(ucm, script);
        webkit_user_script_unref(script);
    }

    /// <inheritdoc />
    public void Navigate(string url)
    {
        if (!IsReady)
        {
            // 网页引擎创建后（AdapterCreated）再打开
            _pendingUrl = url;
            return;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            _webView.Navigate(uri);
        }
        else
        {
            Log.Debug("悬浮小窗打开网址失败：{Url}", url);
        }
    }

    /// <inheritdoc />
    public void GoBack()
    {
        if (CanGoBack)
        {
            _webView.GoBack();
        }
    }

    /// <inheritdoc />
    public void Reload()
    {
        if (IsReady)
        {
            _webView.Refresh();
        }
    }

    /// <inheritdoc />
    public void SetZoom(double factor)
    {
        _zoom = factor;
        var view = _view;
        if (view != 0 && !_disposed)
        {
            OnGtk(() => webkit_web_view_set_zoom_level(view, factor));
        }
    }

    /// <inheritdoc />
    public async Task<GuideCommandResult> ExecuteAsync(GuidePlayerCommand command, double value = 0)
    {
        if (!IsReady)
        {
            return GuideCommandResult.NotReady;
        }

        // 参数先序列化为 JSON，不把任何字符串直接拼进脚本
        var payload = JsonConvert.SerializeObject(new { cmd = CommandName(command), value });
        try
        {
            var json = await _webView.InvokeScript($"JSON.stringify(window.__bgiGuide ? window.__bgiGuide.exec({payload}) : null)");
            var token = string.IsNullOrEmpty(json) ? null : JToken.Parse(json);
            // 不同网页引擎对字符串结果是否再加一层引号不一致：两种都接受
            if (token?.Type == JTokenType.String)
            {
                token = JToken.Parse(token.Value<string>() ?? "null");
            }

            if (token is not JObject result)
            {
                return GuideCommandResult.NotReady;
            }

            return new GuideCommandResult(result.Value<bool?>("ok") ?? false, result.Value<string?>("reason"));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ObjectDisposedException)
        {
            Log.Debug(e, "悬浮小窗执行命令 {Command} 失败", command);
            return new GuideCommandResult(false, "notReady");
        }
    }

    /// <summary>在页面中执行脚本并返回结果（自测用）</summary>
    public Task<string?> EvalAsync(string script) => _webView.InvokeScript(script);

    [UnmanagedCallersOnly]
    private static void OnScriptMessage(nint manager, nint jsResult, nint userData)
    {
        try
        {
            if (!ByContentManager.TryGetValue(manager, out var bridge) || bridge._disposed)
            {
                return;
            }

            var value = webkit_javascript_result_get_js_value(jsResult);
            var text = jsc_value_to_string(value);
            var raw = Marshal.PtrToStringUTF8(text) ?? "";
            g_free(text);
            if (raw.Length > 64 * 1024)
            {
                return;
            }

            BetterGenshinImpact.Platform.UiHost.Dispatcher.Post(() =>
            {
                if (!bridge._disposed)
                {
                    bridge.HandleMessage(raw);
                }
            });
        }
        catch (Exception e)
        {
            Log.Debug(e, "悬浮小窗页面消息处理失败");
        }
    }

    /// <summary>与 Windows 版相同：网页的权限请求（摄像头、通知、定位等）一律拒绝</summary>
    [UnmanagedCallersOnly]
    private static int OnPermissionRequest(nint view, nint request, nint userData)
    {
        webkit_permission_request_deny(request);
        return 1;
    }

    [UnmanagedCallersOnly]
    private static void OnWebProcessTerminated(nint view, int reason, nint userData)
    {
        if (!ByView.TryGetValue(view, out var bridge) || bridge._disposed || reason == 2 /* 由程序结束 */)
        {
            return;
        }

        BetterGenshinImpact.Platform.UiHost.Dispatcher.Post(() =>
            bridge.EventRaised?.Invoke(bridge, new GuidePlayerEvent(GuidePlayerEventKind.Crashed)));
    }

    private static T RunOnGtk<T>(Func<T> callback) =>
        global::Avalonia.X11.Interop.GtkInteropHelper.RunOnGlibThread(callback).GetAwaiter().GetResult();

    private static void OnGtk(Action callback) =>
        _ = global::Avalonia.X11.Interop.GtkInteropHelper.RunOnGlibThread(() =>
        {
            try
            {
                callback();
            }
            catch (Exception e)
            {
                Log.Debug(e, "悬浮小窗网页调用失败");
            }

            return 0;
        });

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_contentManager != 0)
        {
            ByContentManager.TryRemove(_contentManager, out _);
        }

        if (_view != 0)
        {
            ByView.TryRemove(_view, out _);
        }
    }

    private const string WebKit = "libwebkit2gtk-4.1.so.0";
    private const string Jsc = "libjavascriptcoregtk-4.1.so.0";
    private const string GLib = "libglib-2.0.so.0";
    private const string GObject = "libgobject-2.0.so.0";

    [LibraryImport(WebKit)] private static partial nint webkit_web_view_get_user_content_manager(nint view);
    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool webkit_user_content_manager_register_script_message_handler(nint manager, string name);
    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)] private static partial nint webkit_user_script_new(string source, int frames, int time, nint allow, nint block);
    [LibraryImport(WebKit)] private static partial void webkit_user_content_manager_add_script(nint manager, nint script);
    [LibraryImport(WebKit)] private static partial void webkit_user_script_unref(nint script);
    [LibraryImport(WebKit)] private static partial nint webkit_javascript_result_get_js_value(nint result);
    [LibraryImport(WebKit)] private static partial void webkit_permission_request_deny(nint request);
    [LibraryImport(WebKit)] private static partial void webkit_web_view_set_zoom_level(nint view, double level);
    [LibraryImport(WebKit)] private static partial void webkit_web_view_set_is_muted(nint view, [MarshalAs(UnmanagedType.Bool)] bool muted);
    [LibraryImport(Jsc)] private static partial nint jsc_value_to_string(nint value);
    [LibraryImport(GObject, StringMarshalling = StringMarshalling.Utf8)] private static partial nuint g_signal_connect_data(nint instance, string signal, nint handler, nint data, nint destroy, int flags);
    [LibraryImport(GLib)] private static partial void g_free(nint memory);
}
