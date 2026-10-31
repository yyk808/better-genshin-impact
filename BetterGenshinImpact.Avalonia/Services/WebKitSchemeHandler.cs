using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using BetterGenshinImpact.Core.Script.Settings;
using Serilog;

namespace BetterGenshinImpact.Avalonia.Services;

/// <summary>
/// 在 WebKitGTK 中注册 bgi-settings:// 自定义协议，把 HTML 设置页的全部请求交给 Core 的 <see cref="ScriptSettingsRequestHandler"/>：
/// 与 Windows 版拦截 WebView2 请求的做法相同，不经过网络、不监听端口。
/// 回调在 WebKitGTK 的 GLib 线程上执行；各设置页按主机名区分（主机名由 <see cref="ScriptSettingsHtmlPage"/> 按脚本生成）。
/// HTML 遮罩用同一套处理注册 bgi-mask:// 协议（<see cref="MaskScheme"/>），把脚本目录映射到虚拟主机（对应 Windows 版的 SetVirtualHostNameToFolderMapping）
/// </summary>
internal static unsafe partial class WebKitSchemeHandler
{
    public const string Scheme = "bgi-settings";

    /// <summary>HTML 遮罩页面使用的协议</summary>
    public const string MaskScheme = "bgi-mask";

    private const string WebKit = "libwebkit2gtk-4.1.so.0";
    private const string Gio = "libgio-2.0.so.0";
    private const string GLib = "libglib-2.0.so.0";
    private const string GObject = "libgobject-2.0.so.0";
    private const string Soup = "libsoup-3.0.so.0";

    /// <summary>"协议:主机名" → 请求处理（方法、已解码路径、请求体 → 响应）；按协议区分，遮罩页面不能经主机名访问设置页</summary>
    private static readonly ConcurrentDictionary<string, Func<string, string, Stream?, SchemeResponse>> Hosts = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<(nint Context, string Scheme)> RegisteredContexts = [];

    /// <summary>为 WebKitWebView 所属的上下文注册设置页协议（每个上下文只注册一次）</summary>
    public static void EnsureRegistered(nint webKitWebView) => EnsureContextRegistered(webkit_web_view_get_context(webKitWebView), Scheme);

    /// <summary>为 WebKit 上下文注册指定协议（每个上下文、协议只注册一次）</summary>
    public static void EnsureContextRegistered(nint context, string scheme)
    {
        lock (RegisteredContexts)
        {
            if (!RegisteredContexts.Add((context, scheme)))
            {
                return;
            }
        }

        webkit_web_context_register_uri_scheme(context, scheme, &OnRequest, 0, 0);
        // 视为安全来源（与 https 相同的安全上下文），并允许页面用 fetch / XHR 访问自身
        var security = webkit_web_context_get_security_manager(context);
        webkit_security_manager_register_uri_scheme_as_secure(security, scheme);
        webkit_security_manager_register_uri_scheme_as_cors_enabled(security, scheme);
    }

    public static void AddHost(string host, Func<string, string, Stream?, ScriptSettingsResponse> handler) =>
        Hosts[Key(Scheme, host)] = (method, path, body) => SchemeResponse.From(handler(method, path, body));

    /// <summary>注册主机，响应头由调用方决定（HTML 遮罩不使用设置页的内容安全策略）</summary>
    public static void AddRawHost(string scheme, string host, Func<string, string, Stream?, SchemeResponse> handler) => Hosts[Key(scheme, host)] = handler;

    public static void RemoveRawHost(string scheme, string host) => Hosts.TryRemove(Key(scheme, host), out _);

    private static string Key(string scheme, string host) => scheme + ":" + host;

    public static void RemoveHost(string host) => Hosts.TryRemove(Key(Scheme, host), out _);

    public static Uri EntryUri(ScriptSettingsHtmlPage page) => new($"{Scheme}://{page.HostName}/{Uri.EscapeDataString(page.EntryFile)}");

    [UnmanagedCallersOnly]
    private static void OnRequest(nint request, nint userData)
    {
        SchemeResponse response;
        try
        {
            var uri = new Uri(Marshal.PtrToStringUTF8(webkit_uri_scheme_request_get_uri(request)) ?? "");
            var method = Marshal.PtrToStringUTF8(webkit_uri_scheme_request_get_http_method(request)) ?? "GET";
            using var body = ReadBody(request);
            response = Hosts.TryGetValue(Key(uri.Scheme, uri.Host), out var handler)
                ? handler(method, Uri.UnescapeDataString(uri.AbsolutePath), body)
                : BlockedResponse;
        }
        catch (Exception e)
        {
            Log.Warning(e, "设置页请求处理异常");
            response = BlockedResponse;
        }

        try
        {
            Finish(request, response);
        }
        catch (Exception e)
        {
            Log.Warning(e, "设置页响应写回失败");
        }
    }

    /// <summary>读取 POST 请求体（WebKitGTK 2.40 起提供），上限与 Windows 版相同</summary>
    private static MemoryStream? ReadBody(nint request)
    {
        var stream = webkit_uri_scheme_request_get_http_body(request);
        if (stream == 0)
        {
            return null;
        }

        try
        {
            var result = new MemoryStream();
            var buffer = new byte[16 * 1024];
            while (result.Length <= ScriptSettingsRequestHandler.MaxBodyBytes)
            {
                nuint read;
                nint error;
                fixed (byte* p = buffer)
                {
                    g_input_stream_read_all(stream, p, (nuint)buffer.Length, out read, 0, out error);
                }

                if (error != 0)
                {
                    g_error_free(error);
                    break;
                }

                if (read == 0)
                {
                    break;
                }

                result.Write(buffer, 0, (int)read);
                if ((int)read < buffer.Length)
                {
                    break;
                }
            }

            result.Position = 0;
            return result;
        }
        finally
        {
            g_object_unref(stream);
        }
    }

    private static SchemeResponse BlockedResponse => SchemeResponse.From(ScriptSettingsRequestHandler.Blocked);

    private static void Finish(nint request, SchemeResponse response)
    {
        var bytes = response.Body;
        nint copy;
        fixed (byte* p = bytes)
        {
            copy = g_memdup2(p, (nuint)bytes.Length);
        }

        var input = g_memory_input_stream_new_from_data(copy, bytes.Length, (nint)(delegate* unmanaged<nint, void>)&FreeData);
        var native = webkit_uri_scheme_response_new(input, bytes.Length);
        webkit_uri_scheme_response_set_status(native, (uint)response.StatusCode, response.ReasonPhrase);
        webkit_uri_scheme_response_set_content_type(native, response.ContentType);
        // 安全响应头（内容安全策略等）与 Windows 版相同；响应对象接管头部对象
        var headers = soup_message_headers_new(1 /* SOUP_MESSAGE_HEADERS_RESPONSE */);
        foreach (var line in response.Headers.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = line.IndexOf(':');
            if (colon > 0)
            {
                soup_message_headers_append(headers, line[..colon].Trim(), line[(colon + 1)..].Trim());
            }
        }

        webkit_uri_scheme_response_set_http_headers(native, headers);
        webkit_uri_scheme_request_finish_with_response(request, native);
        g_object_unref(native);
        g_object_unref(input);
    }

    [UnmanagedCallersOnly]
    private static void FreeData(nint data) => g_free(data);

    [LibraryImport(WebKit)] private static partial nint webkit_web_view_get_context(nint view);
    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)] private static partial void webkit_web_context_register_uri_scheme(nint context, string scheme, delegate* unmanaged<nint, nint, void> callback, nint userData, nint destroy);
    [LibraryImport(WebKit)] private static partial nint webkit_web_context_get_security_manager(nint context);
    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)] private static partial void webkit_security_manager_register_uri_scheme_as_secure(nint manager, string scheme);
    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)] private static partial void webkit_security_manager_register_uri_scheme_as_cors_enabled(nint manager, string scheme);
    [LibraryImport(WebKit)] private static partial nint webkit_uri_scheme_request_get_uri(nint request);
    [LibraryImport(WebKit)] private static partial nint webkit_uri_scheme_request_get_http_method(nint request);
    [LibraryImport(WebKit)] private static partial nint webkit_uri_scheme_request_get_http_body(nint request);
    [LibraryImport(WebKit)] private static partial nint webkit_uri_scheme_response_new(nint stream, long length);
    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)] private static partial void webkit_uri_scheme_response_set_status(nint response, uint code, string? reason);
    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)] private static partial void webkit_uri_scheme_response_set_content_type(nint response, string contentType);
    [LibraryImport(WebKit)] private static partial void webkit_uri_scheme_response_set_http_headers(nint response, nint headers);
    [LibraryImport(WebKit)] private static partial void webkit_uri_scheme_request_finish_with_response(nint request, nint response);
    [LibraryImport(Soup)] private static partial nint soup_message_headers_new(int type);
    [LibraryImport(Soup, StringMarshalling = StringMarshalling.Utf8)] private static partial void soup_message_headers_append(nint headers, string name, string value);
    [LibraryImport(Gio)] private static partial nint g_memory_input_stream_new_from_data(nint data, nint length, nint destroy);
    [LibraryImport(Gio)] private static partial int g_input_stream_read_all(nint stream, byte* buffer, nuint count, out nuint read, nint cancellable, out nint error);
    [LibraryImport(GLib)] private static partial nint g_memdup2(void* memory, nuint size);
    [LibraryImport(GLib)] private static partial void g_free(nint memory);
    [LibraryImport(GLib)] private static partial void g_error_free(nint error);
    [LibraryImport(GObject)] private static partial void g_object_unref(nint obj);
}

/// <summary>自定义协议的响应：状态、类型、内容与附加响应头（每行一个 "名称: 值"）</summary>
internal sealed record SchemeResponse(int StatusCode, string ReasonPhrase, string ContentType, byte[] Body, string Headers)
{
    public static SchemeResponse From(ScriptSettingsResponse r) => new(r.StatusCode, r.ReasonPhrase, r.ContentType, r.Body, r.Headers);
}
