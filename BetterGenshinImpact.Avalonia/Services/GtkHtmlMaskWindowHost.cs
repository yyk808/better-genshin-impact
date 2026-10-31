using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using HtmlMaskPage = BetterGenshinImpact.Core.Script.Dependence.HtmlMaskPage;
using IHtmlMaskWindowHost = BetterGenshinImpact.Core.Script.Dependence.IHtmlMaskWindowHost;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Host.Common;
using BetterGenshinImpact.Infrastructure.Storage;
using Serilog;

namespace BetterGenshinImpact.Avalonia.Services;

/// <summary>
/// 脚本 HTML 遮罩（<see cref="IHtmlMaskWindowHost"/>）的 Linux 实现：每个遮罩是一个 WebKitGTK 的 RGBA 顶层窗口，
/// 透明背景、置顶、默认点击穿透（SHAPE 输入区域为空），位置大小跟随游戏画面。
/// Avalonia 的 NativeWebView（嵌入窗口与离屏两种模式）在透明窗口里都会画出不透明的背景，因此直接用 WebKitGTK 建窗口；
/// GTK 调用都在 Avalonia 的 GTK 线程上执行（<c>GtkInteropHelper.RunOnGlibThread</c>，与 NativeWebView 共用）。
/// 与 Windows 版的对应：脚本目录映射到 bgi-mask://hm-xxxx.bettergi.local/（Windows 为 https 虚拟主机），
/// 消息桥 window.htmlMask 相同（页面里提供 window.chrome.webview 的收发接口），网络请求用 WebKit 内容过滤规则限制在
/// 本页面、data:/blob: 与脚本 http_allowed_urls 允许的地址（需启用 JS HTTP 权限）。
/// </summary>
internal sealed unsafe partial class GtkHtmlMaskWindowHost : IHtmlMaskWindowHost, IDisposable
{
    private const int MaxWindows = 5;
    private const string MessageHandler = "bgiHtmlMask";

    /// <summary>注入在消息桥之前：提供 WebView2 的 window.chrome.webview 收发接口，消息经 WebKit 的脚本消息处理器转给宿主</summary>
    private const string ChromeWebViewShim = """
        (function () {
            var listeners = [];
            var post = function (s) { window.webkit.messageHandlers.bgiHtmlMask.postMessage(String(s)); };
            window.chrome = window.chrome || {};
            window.chrome.webview = {
                postMessage: function (m) { post(typeof m === 'string' ? m : JSON.stringify(m)); },
                addEventListener: function (type, cb) { if (type === 'message') listeners.push(cb); },
                removeEventListener: function (type, cb) { var i = listeners.indexOf(cb); if (i >= 0) listeners.splice(i, 1); }
            };
            window.__bgiHtmlMaskDeliver = function (raw) {
                listeners.slice().forEach(function (cb) { try { cb({ data: raw }); } catch (e) { console.error(e); } });
            };
        })();
        """;

    private static readonly ConcurrentDictionary<string, MaskPage> Windows = new();
    private static readonly ConcurrentDictionary<nint, MaskPage> ByView = new();
    private static readonly ConcurrentDictionary<nint, MaskPage> ByContentManager = new();
    private static readonly ConcurrentDictionary<int, MaskPage> PendingFilters = new();
    private static int _filterSeq;
    private static nint _context;

    private readonly GameHost _host;
    private readonly ILogger _log;
    private readonly DispatcherTimer _timer;
    private (int X, int Y, int W, int H) _lastRect;
    private bool _captureWarned;

    public GtkHtmlMaskWindowHost(GameHost host, ILogger log)
    {
        _host = host;
        _log = log;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => UpdatePositions());
        _timer.Start();
    }

    private sealed class MaskPage
    {
        public required string Id { get; init; }
        public required string PageUrl { get; init; }
        public string? VirtualHost { get; init; }
        public required string WorkDir { get; init; }
        public required string[] AllowedHttp { get; init; }
        public nint Window;
        public nint View;
        public nint ContentManager;
        public bool Loaded;
        public bool Closed;
        public volatile bool ClickThrough = true;
    }

    /// <summary>当前游戏画面（屏幕像素），截图器未启动时为空</summary>
    private (int X, int Y, int W, int H)? GameRect()
    {
        var window = _host.RuntimeService.Current?.Window;
        if (window is null || window.IsMinimized)
        {
            return null;
        }

        var r = window.Viewport.ScreenRect;
        return r.Width > 0 && r.Height > 0 ? (r.X, r.Y, r.Width, r.Height) : null;
    }

    private nint GameHandle => _host.RuntimeService.Current?.Window.Handle ?? 0;

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
                Log.Debug(e, "HTML遮罩 GTK 操作失败");
            }

            return 0;
        });

    public string Show(string url, string? id, string workDir)
    {
        workDir = Path.GetFullPath(workDir);
        string pageUrl;
        string? virtualHost = null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var pageUri) && pageUri.IsFile)
        {
            virtualHost = HtmlMaskPage.VirtualHostName(workDir);
            pageUrl = HtmlMaskPage.CreateVirtualPageUrl(pageUri, workDir, virtualHost, WebKitSchemeHandler.MaskScheme);
        }
        else
        {
            pageUrl = url;
        }

        if (!WebKitAvailable())
        {
            _log.Warning("未安装 WebKitGTK（libwebkit2gtk-4.1），无法打开 HTML 遮罩");
            throw new InvalidOperationException("未安装 WebKitGTK（libwebkit2gtk-4.1），无法打开 HTML 遮罩");
        }

        var page = new MaskPage
        {
            Id = id ?? Guid.NewGuid().ToString("N"),
            PageUrl = pageUrl,
            VirtualHost = virtualHost,
            WorkDir = workDir,
            AllowedHttp = HtmlMaskPage.AllowedHttpPatterns(),
        };
        if (!_captureWarned && !string.Equals(GameHost.EffectiveCaptureMode(_host.LinuxConfig), "CompositeWindow", StringComparison.OrdinalIgnoreCase))
        {
            // 屏幕区域截图会截到遮罩页面的内容（Windows 截取的是游戏窗口本身，没有这个问题）
            _captureWarned = true;
            _log.Warning("HTML 遮罩：当前截图方式为 {Mode}，截图器画面会包含遮罩页面的内容；建议在启动页把截图方式改为“Composite 窗口图像”或启用遮罩窗口（启用后自动改用）",
                GameHost.EffectiveCaptureMode(_host.LinuxConfig));
        }

        var rect = GameRect();
        return OnGtk(() =>
        {
            // 指定ID时先关闭已有窗口
            if (id != null && Windows.TryGetValue(id, out var existing))
            {
                CloseOnGtk(existing);
            }

            if (Windows.Count >= MaxWindows)
            {
                throw new InvalidOperationException($"最多同时打开 {MaxWindows} 个HTML遮罩窗口");
            }

            Create(page, rect);
            Windows[page.Id] = page;
            _log.Information("HTML遮罩窗口已打开：{Id} {Url}", page.Id, page.PageUrl);
            return page.Id;
        });
    }

    private static bool? _webKitAvailable;

    private static bool WebKitAvailable() => _webKitAvailable ??=
        NativeLibrary.TryLoad(WebKit, out var handle) && NativeLibrary.TryGetExport(handle, "webkit_web_view_evaluate_javascript", out _);

    private void Create(MaskPage page, (int X, int Y, int W, int H)? rect)
    {
        var context = EnsureContext();
        if (page.VirtualHost is { } host)
        {
            var root = page.WorkDir;
            WebKitSchemeHandler.AddRawHost(WebKitSchemeHandler.MaskScheme, host, (method, path, _) => ServeFile(root, method, path));
        }

        // 消息桥：页面脚本消息 → 宿主；文档创建时注入 chrome.webview 接口与 window.htmlMask
        var ucm = webkit_user_content_manager_new();
        webkit_user_content_manager_register_script_message_handler(ucm, MessageHandler);
        g_signal_connect_data(ucm, "script-message-received::" + MessageHandler, (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnScriptMessage, 0, 0, 0);
        var script = webkit_user_script_new(ChromeWebViewShim + HtmlMaskPage.BridgeScript, 1 /* TOP_FRAME */, 0 /* DOCUMENT_START */, 0, 0);
        webkit_user_content_manager_add_script(ucm, script);
        webkit_user_script_unref(script);

        var view = g_object_new(webkit_web_view_get_type(), "web-context", context, "user-content-manager", ucm, 0);
        var settings = webkit_web_view_get_settings(view);
        webkit_settings_set_enable_developer_extras(settings, false);
        var transparent = default(GdkRGBA);
        webkit_web_view_set_background_color(view, ref transparent);
        g_signal_connect_data(view, "load-changed", (nint)(delegate* unmanaged<nint, int, nint, void>)&OnLoadChanged, 0, 0, 0);
        g_signal_connect_data(view, "decide-policy", (nint)(delegate* unmanaged<nint, nint, int, nint, int>)&OnDecidePolicy, 0, 0, 0);
        // 与 Windows 版 AreDefaultScriptDialogsEnabled = false 相同：不弹出 alert/confirm/prompt
        g_signal_connect_data(view, "script-dialog", (nint)(delegate* unmanaged<nint, nint, nint, int>)&OnScriptDialog, 0, 0, 0);

        var window = gtk_window_new(0);
        gtk_window_set_title(window, "BetterGI HTML 遮罩");
        var visual = gdk_screen_get_rgba_visual(gtk_widget_get_screen(window));
        if (visual != 0)
        {
            gtk_widget_set_visual(window, visual);
        }
        else
        {
            Log.Warning("X 屏幕没有 32 位 RGBA 视觉，HTML 遮罩背景无法透明");
        }

        gtk_widget_set_app_paintable(window, true);
        gtk_window_set_decorated(window, false);
        gtk_window_set_keep_above(window, true);
        gtk_window_set_skip_taskbar_hint(window, true);
        gtk_window_set_skip_pager_hint(window, true);
        gtk_window_set_accept_focus(window, false);
        gtk_window_set_focus_on_map(window, false);
        var (x, y, w, h) = rect ?? (0, 0, 1280, 720);
        var scale = Math.Max(1, gtk_widget_get_scale_factor(window));
        gtk_window_move(window, x / scale, y / scale);
        gtk_window_resize(window, Math.Max(1, w / scale), Math.Max(1, h / scale));
        gtk_container_add(window, view);

        page.Window = window;
        page.View = view;
        page.ContentManager = ucm;
        ByView[view] = page;
        ByContentManager[ucm] = page;

        gtk_widget_show_all(window);
        ApplyClickThrough(page, true);
        if (!X11CompositorPresent(window))
        {
            Log.Warning("没有检测到合成器，HTML 遮罩的透明部分会显示为黑色并挡住游戏画面；请启用桌面合成（如 picom）");
        }

        // 网络请求过滤规则编译完成后再加载页面
        var key = Interlocked.Increment(ref _filterSeq);
        PendingFilters[key] = page;
        var rules = FilterRules(page);
        var bytes = Encoding.UTF8.GetBytes(rules);
        nint gbytes;
        fixed (byte* p = bytes)
        {
            gbytes = g_bytes_new(p, (nuint)bytes.Length);
        }

        var store = webkit_user_content_filter_store_new(Path.Combine(AppPaths.Root, "WebKit", "HtmlMaskFilters"));
        var identifier = "hm-" + Convert.ToHexString(SHA256.HashData(bytes).AsSpan(0, 8)).ToLowerInvariant();
        webkit_user_content_filter_store_save(store, identifier, gbytes, 0, (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnFilterSaved, key);
        g_bytes_unref(gbytes);
        g_object_unref(store);
    }

    /// <summary>
    /// WebKit 内容过滤规则：先拦截全部请求，再放行本页面的虚拟主机、data:/blob:、页面自身地址与 http_allowed_urls 的地址
    /// （与 Windows 版 WebResourceRequested 的判断一致；* 为通配符）
    /// </summary>
    private static string FilterRules(MaskPage page)
    {
        var allow = new List<string> { "^data:", "^blob:", "^about:" };
        if (page.VirtualHost is { } host)
        {
            allow.Add("^" + FilterEscape($"{WebKitSchemeHandler.MaskScheme}://{host}/"));
        }
        else
        {
            allow.Add("^" + FilterEscape(page.PageUrl) + "$");
        }

        foreach (var pattern in page.AllowedHttp)
        {
            if (pattern.Any(c => c > 127))
            {
                Log.Warning("HTML遮罩：http_allowed_urls 中的地址含非 ASCII 字符，WebKit 过滤规则不支持，已忽略：{Pattern}", pattern);
                continue;
            }

            allow.Add("^" + string.Join(".*", pattern.Split('*').Select(FilterEscape)) + "$");
        }

        var rules = new List<object> { new { trigger = new Dictionary<string, object> { ["url-filter"] = ".*" }, action = new { type = "block" } } };
        rules.AddRange(allow.Select(a => (object)new { trigger = new Dictionary<string, object> { ["url-filter"] = a }, action = new { type = "ignore-previous-rules" } }));
        return JsonSerializer.Serialize(rules);
    }

    private static string FilterEscape(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text)
        {
            if (".\\+?*[](){}^$|".Contains(c))
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    [UnmanagedCallersOnly]
    private static void OnFilterSaved(nint source, nint result, nint userData)
    {
        if (!PendingFilters.TryRemove((int)userData, out var page))
        {
            return;
        }

        try
        {
            var filter = webkit_user_content_filter_store_save_finish(source, result, out var error);
            if (filter == 0)
            {
                var message = error != 0 ? Marshal.PtrToStringUTF8(((GError*)error)->message) : "";
                if (error != 0)
                {
                    g_error_free(error);
                }

                Log.Error("HTML遮罩网络过滤规则编译失败，已关闭遮罩：{Message}", message);
                CloseOnGtk(page);
                return;
            }

            if (page.Closed)
            {
                webkit_user_content_filter_unref(filter);
                return;
            }

            webkit_user_content_manager_add_filter(page.ContentManager, filter);
            webkit_user_content_filter_unref(filter);
            webkit_web_view_load_uri(page.View, page.PageUrl);
        }
        catch (Exception e)
        {
            Log.Error(e, "HTML遮罩页面加载失败");
        }
    }

    private static SchemeResponse ServeFile(string root, string method, string path)
    {
        static SchemeResponse Text(int code, string reason, string text) => new(code, reason, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text), "Cache-Control: no-store");
        if (!method.Equals("GET", StringComparison.OrdinalIgnoreCase) && !method.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
        {
            return Text(405, "Method Not Allowed", "只允许 GET");
        }

        var relative = path.TrimStart('/');
        var full = Path.GetFullPath(Path.Combine(root, relative));
        var rootWithSep = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSep, StringComparison.Ordinal))
        {
            Log.Warning("拦截HTML遮罩越级文件访问: {Path}", path);
            return Text(403, "Forbidden", "Blocked");
        }

        // 与 Windows 版虚拟主机映射相同（访问类型 Deny）：链接指向目录外时拒绝
        var info = new FileInfo(full);
        if (!info.Exists)
        {
            return Text(404, "Not Found", "Not Found");
        }

        if (info.ResolveLinkTarget(true) is { } target && !Path.GetFullPath(target.FullName).StartsWith(rootWithSep, StringComparison.Ordinal))
        {
            Log.Warning("拦截HTML遮罩越级文件访问: {Path}", path);
            return Text(403, "Forbidden", "Blocked");
        }

        return new SchemeResponse(200, "OK", HtmlMaskPage.ContentTypeFor(full), File.ReadAllBytes(full), "Cache-Control: no-cache");
    }

    private static nint EnsureContext()
    {
        if (_context != 0)
        {
            return _context;
        }

        // 与 Windows 版的 HtmlMask 配置文件对应：遮罩页面共用一个持久数据目录，与其他网页隔离
        var data = Path.Combine(AppPaths.Root, "WebKit", "HtmlMask");
        Directory.CreateDirectory(data);
        var manager = webkit_website_data_manager_new("base-data-directory", Path.Combine(data, "data"), "base-cache-directory", Path.Combine(data, "cache"), 0);
        _context = webkit_web_context_new_with_website_data_manager(manager);
        g_object_unref(manager);
        WebKitSchemeHandler.EnsureContextRegistered(_context, WebKitSchemeHandler.MaskScheme);
        return _context;
    }

    private static bool X11CompositorPresent(nint window)
    {
        var screen = gtk_widget_get_screen(window);
        return gdk_screen_is_composited(screen);
    }

    [UnmanagedCallersOnly]
    private static void OnScriptMessage(nint manager, nint jsResult, nint userData)
    {
        try
        {
            if (!ByContentManager.TryGetValue(manager, out var page) || page.Closed)
            {
                return;
            }

            var value = webkit_javascript_result_get_js_value(jsResult);
            var text = jsc_value_to_string(value);
            var raw = Marshal.PtrToStringUTF8(text) ?? "";
            g_free(text);
            HtmlMaskPage.ReceiveFromPage(page.Id, raw);
        }
        catch (Exception e)
        {
            Log.Debug(e, "HTML遮罩消息处理异常");
        }
    }

    [UnmanagedCallersOnly]
    private static void OnLoadChanged(nint view, int loadEvent, nint userData)
    {
        if (!ByView.TryGetValue(view, out var page))
        {
            return;
        }

        if (loadEvent == 0 /* STARTED */)
        {
            page.Loaded = false;
        }
        else if (loadEvent == 3 /* FINISHED */)
        {
            page.Loaded = true;
            Flush(page);
        }
    }

    [UnmanagedCallersOnly]
    private static int OnDecidePolicy(nint view, nint decision, int type, nint userData)
    {
        switch (type)
        {
            case 1: // NEW_WINDOW_ACTION：遮罩页面不允许打开新窗口
                webkit_policy_decision_ignore(decision);
                return 1;
            case 2: // RESPONSE：不能显示的内容（下载）一律拒绝
                if (!webkit_response_policy_decision_is_mime_type_supported(decision))
                {
                    webkit_policy_decision_ignore(decision);
                    return 1;
                }

                return 0;
            default:
                return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static int OnScriptDialog(nint view, nint dialog, nint userData) => 1;

    private static void Flush(MaskPage page)
    {
        if (!page.Loaded || page.Closed)
        {
            return;
        }

        HtmlMaskPage.FlushToPage(page.Id, json =>
            webkit_web_view_evaluate_javascript(page.View, "window.__bgiHtmlMaskDeliver && window.__bgiHtmlMaskDeliver(" + JsonSerializer.Serialize(json) + ")", -1, 0, 0, 0, 0, 0));
    }

    private static void ApplyClickThrough(MaskPage page, bool enabled)
    {
        if (enabled)
        {
            var region = cairo_region_create();
            gtk_widget_input_shape_combine_region(page.Window, region);
            cairo_region_destroy(region);
        }
        else
        {
            gtk_widget_input_shape_combine_region(page.Window, 0);
        }

        gtk_window_set_accept_focus(page.Window, !enabled);
        page.ClickThrough = enabled;
        if (!enabled)
        {
            // 禁用穿透：激活遮罩窗口，获得键盘和鼠标焦点
            gtk_window_present(page.Window);
        }
    }

    private static void CloseOnGtk(MaskPage page)
    {
        if (page.Closed)
        {
            return;
        }

        page.Closed = true;
        Windows.TryRemove(new KeyValuePair<string, MaskPage>(page.Id, page));
        ByView.TryRemove(page.View, out _);
        ByContentManager.TryRemove(page.ContentManager, out _);
        if (page.VirtualHost is { } host && !Windows.Values.Any(w => w.VirtualHost == host))
        {
            WebKitSchemeHandler.RemoveRawHost(WebKitSchemeHandler.MaskScheme, host);
        }

        gtk_widget_destroy(page.Window);
        g_object_unref(page.ContentManager);
        Log.Information("HTML遮罩窗口已关闭：{Id}", page.Id);
    }

    public bool Close(string id)
    {
        if (!Windows.TryGetValue(id, out var page))
        {
            return false;
        }

        OnGtk(() =>
        {
            CloseOnGtk(page);
            return 0;
        });
        return true;
    }

    public string[] GetWindowIds() => Windows.Keys.ToArray();

    public bool Exists(string id) => Windows.ContainsKey(id);

    private static MaskPage GetOrThrow(string windowId) =>
        Windows.TryGetValue(windowId, out var page) ? page : throw new InvalidOperationException($"HTML遮罩窗口不存在或已关闭: {windowId}");

    public void SetClickThrough(string windowId, bool enabled)
    {
        var page = GetOrThrow(windowId);
        OnGtk(() =>
        {
            ApplyClickThrough(page, enabled);
            return 0;
        });
        if (enabled)
        {
            // 开启穿透：将焦点还给游戏窗口
            try
            {
                if (GameHandle is var handle and not 0)
                {
                    GameSystem.FocusWindow(handle);
                }
            }
            catch (Exception e)
            {
                _log.Debug(e, "HTML遮罩恢复游戏焦点失败");
            }
        }
    }

    public bool GetClickThrough(string windowId) => GetOrThrow(windowId).ClickThrough;

    public void ToggleClickThrough(string windowId)
    {
        var page = GetOrThrow(windowId);
        SetClickThrough(windowId, !page.ClickThrough);
    }

    public void NotifyFlush(string windowId)
    {
        if (Windows.TryGetValue(windowId, out var page))
        {
            OnGtkAsync(() => Flush(page));
        }
    }

    public void CloseAll()
    {
        if (Windows.IsEmpty)
        {
            return;
        }

        OnGtk(() =>
        {
            foreach (var page in Windows.Values.ToArray())
            {
                CloseOnGtk(page);
            }

            return 0;
        });
    }

    /// <summary>位置大小跟随游戏画面（截图器未启动时不移动）</summary>
    private void UpdatePositions()
    {
        if (Windows.IsEmpty || GameRect() is not { } rect || rect == _lastRect)
        {
            return;
        }

        _lastRect = rect;
        var pages = Windows.Values.ToArray();
        OnGtkAsync(() =>
        {
            foreach (var page in pages.Where(p => !p.Closed))
            {
                var scale = Math.Max(1, gtk_widget_get_scale_factor(page.Window));
                gtk_window_move(page.Window, rect.X / scale, rect.Y / scale);
                gtk_window_resize(page.Window, Math.Max(1, rect.W / scale), Math.Max(1, rect.H / scale));
            }
        });
    }

    public void Dispose()
    {
        _timer.Stop();
        try
        {
            CloseAll();
        }
        catch (Exception e)
        {
            _log.Debug(e, "关闭HTML遮罩窗口失败");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GdkRGBA
    {
        public double R, G, B, A;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GError
    {
        public uint domain;
        public int code;
        public nint message;
    }

    private const string Gtk = "libgtk-3.so.0";
    private const string Gdk = "libgdk-3.so.0";
    private const string WebKit = "libwebkit2gtk-4.1.so.0";
    private const string Jsc = "libjavascriptcoregtk-4.1.so.0";
    private const string GLib = "libglib-2.0.so.0";
    private const string GObject = "libgobject-2.0.so.0";
    private const string Cairo = "libcairo.so.2";

    [LibraryImport(Gtk)] private static partial nint gtk_window_new(int type);
    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)] private static partial void gtk_window_set_title(nint window, string title);
    [LibraryImport(Gtk)] private static partial nint gtk_widget_get_screen(nint widget);
    [LibraryImport(Gdk)] private static partial nint gdk_screen_get_rgba_visual(nint screen);
    [LibraryImport(Gdk)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool gdk_screen_is_composited(nint screen);
    [LibraryImport(Gtk)] private static partial void gtk_widget_set_visual(nint widget, nint visual);
    [LibraryImport(Gtk)] private static partial void gtk_widget_set_app_paintable(nint widget, [MarshalAs(UnmanagedType.Bool)] bool value);
    [LibraryImport(Gtk)] private static partial void gtk_window_set_decorated(nint window, [MarshalAs(UnmanagedType.Bool)] bool value);
    [LibraryImport(Gtk)] private static partial void gtk_window_set_keep_above(nint window, [MarshalAs(UnmanagedType.Bool)] bool value);
    [LibraryImport(Gtk)] private static partial void gtk_window_set_skip_taskbar_hint(nint window, [MarshalAs(UnmanagedType.Bool)] bool value);
    [LibraryImport(Gtk)] private static partial void gtk_window_set_skip_pager_hint(nint window, [MarshalAs(UnmanagedType.Bool)] bool value);
    [LibraryImport(Gtk)] private static partial void gtk_window_set_accept_focus(nint window, [MarshalAs(UnmanagedType.Bool)] bool value);
    [LibraryImport(Gtk)] private static partial void gtk_window_set_focus_on_map(nint window, [MarshalAs(UnmanagedType.Bool)] bool value);
    [LibraryImport(Gtk)] private static partial void gtk_window_present(nint window);
    [LibraryImport(Gtk)] private static partial void gtk_window_move(nint window, int x, int y);
    [LibraryImport(Gtk)] private static partial void gtk_window_resize(nint window, int width, int height);
    [LibraryImport(Gtk)] private static partial int gtk_widget_get_scale_factor(nint widget);
    [LibraryImport(Gtk)] private static partial void gtk_container_add(nint container, nint widget);
    [LibraryImport(Gtk)] private static partial void gtk_widget_show_all(nint widget);
    [LibraryImport(Gtk)] private static partial void gtk_widget_destroy(nint widget);
    [LibraryImport(Gtk)] private static partial void gtk_widget_input_shape_combine_region(nint widget, nint region);
    [LibraryImport(Cairo)] private static partial nint cairo_region_create();
    [LibraryImport(Cairo)] private static partial void cairo_region_destroy(nint region);
    [LibraryImport(WebKit)] private static partial nint webkit_web_view_get_type();
    [LibraryImport(WebKit)] private static partial nint webkit_web_view_get_settings(nint view);
    [LibraryImport(WebKit)] private static partial void webkit_settings_set_enable_developer_extras(nint settings, [MarshalAs(UnmanagedType.Bool)] bool value);
    [LibraryImport(WebKit)] private static partial void webkit_web_view_set_background_color(nint view, ref GdkRGBA color);
    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)] private static partial void webkit_web_view_load_uri(nint view, string uri);
    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)] private static partial void webkit_web_view_evaluate_javascript(nint view, string script, nint length, nint world, nint sourceUri, nint cancellable, nint callback, nint userData);
    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)] private static partial nint webkit_website_data_manager_new(string k1, string v1, string k2, string v2, nint end);
    [LibraryImport(WebKit)] private static partial nint webkit_web_context_new_with_website_data_manager(nint manager);
    [LibraryImport(WebKit)] private static partial nint webkit_user_content_manager_new();
    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool webkit_user_content_manager_register_script_message_handler(nint manager, string name);
    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)] private static partial nint webkit_user_script_new(string source, int frames, int time, nint allowList, nint blockList);
    [LibraryImport(WebKit)] private static partial void webkit_user_content_manager_add_script(nint manager, nint script);
    [LibraryImport(WebKit)] private static partial void webkit_user_script_unref(nint script);
    [LibraryImport(WebKit)] private static partial void webkit_user_content_manager_add_filter(nint manager, nint filter);
    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)] private static partial nint webkit_user_content_filter_store_new(string path);
    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)] private static partial void webkit_user_content_filter_store_save(nint store, string identifier, nint source, nint cancellable, nint callback, nint userData);
    [LibraryImport(WebKit)] private static partial nint webkit_user_content_filter_store_save_finish(nint store, nint result, out nint error);
    [LibraryImport(WebKit)] private static partial void webkit_user_content_filter_unref(nint filter);
    [LibraryImport(WebKit)] private static partial nint webkit_javascript_result_get_js_value(nint result);
    [LibraryImport(WebKit)] private static partial void webkit_policy_decision_ignore(nint decision);
    [LibraryImport(WebKit)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool webkit_response_policy_decision_is_mime_type_supported(nint decision);
    [LibraryImport(Jsc)] private static partial nint jsc_value_to_string(nint value);
    [LibraryImport(GObject, StringMarshalling = StringMarshalling.Utf8)] private static partial nint g_object_new(nint type, string p1, nint v1, string p2, nint v2, nint end);
    [LibraryImport(GObject, StringMarshalling = StringMarshalling.Utf8)] private static partial nuint g_signal_connect_data(nint instance, string signal, nint handler, nint data, nint destroy, int flags);
    [LibraryImport(GObject)] private static partial void g_object_unref(nint obj);
    [LibraryImport(GLib)] private static partial nint g_bytes_new(void* data, nuint size);
    [LibraryImport(GLib)] private static partial void g_bytes_unref(nint bytes);
    [LibraryImport(GLib)] private static partial void g_free(nint memory);
    [LibraryImport(GLib)] private static partial void g_error_free(nint error);
}
