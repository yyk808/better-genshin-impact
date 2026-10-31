using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BetterGenshinImpact.GameTask;

namespace BetterGenshinImpact.Core.Script.Dependence;

/// <summary>
/// HTML 遮罩页面的地址映射、消息解析与网络请求权限校验，Windows（WebView2）与 Linux（WebKitGTK）宿主共用
/// </summary>
public static class HtmlMaskPage
{
    /// <summary>注入页面的消息桥（window.htmlMask），经 window.chrome.webview 收发字符串消息</summary>
    public const string BridgeScript = @"
                window.htmlMask = {
                    _callbacks: {},
                    _seq: 0,
                    request: function(url, data) {
                        return new Promise(function(resolve, reject) {
                            var id = '__req_' + (++window.htmlMask._seq);
                            window.htmlMask._callbacks[id] = { resolve: resolve, reject: reject };
                            window.chrome.webview.postMessage(JSON.stringify({
                                url: url,
                                data: data || {},
                                requestId: id
                            }));
                        });
                    },
                    onMessage: null,
                    _dispatch: function(raw) {
                        try {
                            var msg = JSON.parse(raw);
                            if (msg.requestId && window.htmlMask._callbacks[msg.requestId]) {
                                window.htmlMask._callbacks[msg.requestId].resolve(msg);
                                delete window.htmlMask._callbacks[msg.requestId];
                            } else if (window.htmlMask.onMessage) {
                                var result = window.htmlMask.onMessage(msg);
                                if (msg.requestId && result !== undefined) {
                                    Promise.resolve(result).then(function(data) {
                                        window.chrome.webview.postMessage(JSON.stringify({
                                            requestId: msg.requestId,
                                            url: '/__response__',
                                            data: data
                                        }));
                                    });
                                }
                            }
                        } catch(e) {
                            if (window.htmlMask.onMessage) window.htmlMask.onMessage(raw);
                        }
                    }
                };
                window.chrome.webview.addEventListener('message', function(e) {
                    window.htmlMask._dispatch(e.data);
                });
            ";

    /// <summary>脚本目录名对应的虚拟主机名：hm-&lt;SHA256 前 4 字节&gt;.bettergi.local</summary>
    public static string VirtualHostName(string workDir)
    {
        var scriptName = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(workDir)));
        return $"hm-{CreateScriptKey(scriptName)}.bettergi.local";
    }

    public static string CreateScriptKey(string scriptName)
    {
        var normalizedName = scriptName.ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedName));
        return Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant();
    }

    /// <summary>把脚本目录内的 file:// 页面地址换成虚拟主机地址；越出脚本目录时抛出异常</summary>
    public static string CreateVirtualPageUrl(Uri fileUri, string workDir, string virtualHostName, string scheme)
    {
        var pagePath = Path.GetFullPath(fileUri.LocalPath);
        var relativePath = Path.GetRelativePath(workDir, pagePath);

        if (Path.IsPathRooted(relativePath)
            || relativePath.Equals("..", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new ArgumentException($"HTML页面路径越界访问: {fileUri}", nameof(fileUri));
        }

        var escapedPath = string.Join('/', relativePath
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));

        var uriBuilder = new UriBuilder(scheme, virtualHostName)
        {
            Path = $"/{escapedPath}",
            Query = fileUri.Query.TrimStart('?'),
            Fragment = fileUri.Fragment.TrimStart('#')
        };
        return uriBuilder.Uri.AbsoluteUri;
    }

    /// <summary>页面发来的消息交给 <see cref="HtmlMask"/>：解析 url + data + requestId，不是 JSON 时整段作为 data</summary>
    public static void ReceiveFromPage(string windowId, string raw)
    {
        string messageUrl = "";
        string data = raw;
        string? requestId = null;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("url", out var ep))
            {
                messageUrl = ep.GetString() ?? "";
                data = root.TryGetProperty("data", out var d) ? d.GetRawText() : "{}";
            }
            if (root.TryGetProperty("requestId", out var rid))
            {
                requestId = rid.GetString();
            }
        }
        catch { }

        HtmlMask.SendFromHtml(windowId, messageUrl, data, requestId);
    }

    /// <summary>把待推送给页面的消息逐条交给宿主发送（页面加载完成后或收到 NotifyFlush 时调用）</summary>
    public static void FlushToPage(string windowId, Action<string> post) => HtmlMask.FlushPendingMessages(windowId, post);

    /// <summary>脚本目录内文件的内容类型（与 HTML 设置页相同的类型表，未知扩展名按二进制处理）</summary>
    public static string ContentTypeFor(string path) =>
        Settings.ScriptSettingsHtmlPage.ContentTypes.TryGetValue(Path.GetExtension(path), out var type) ? type : "application/octet-stream";

    /// <summary>
    /// HTTP/HTTPS 请求的权限校验，与 JS 脚本使用完全一致：需要当前脚本启用 HTTP 权限且地址匹配 http_allowed_urls。
    /// 允许时返回 null，否则返回拦截原因
    /// </summary>
    public static string? CheckHttpRequest(string uri)
    {
        var currentProject = RunnerContext.Instance.CurrentScriptProject;
        if (currentProject?.AllowJsHTTP != true)
        {
            return "未启用JS HTTP权限，拦截HTML遮罩网络请求";
        }

        var allowedUrls = currentProject?.Project?.Manifest.HttpAllowedUrls ?? [];
        if (allowedUrls.Length == 0)
        {
            return "未配置 http_allowed_urls，拦截HTML遮罩网络请求";
        }

        if (allowedUrls.Any(allowedUrl =>
            {
                var pattern = "^" + Regex.Escape(allowedUrl).Replace("\\*", ".*") + "$";
                return Regex.IsMatch(uri, pattern, RegexOptions.IgnoreCase);
            }))
        {
            return null;
        }

        return "URL不在允许列表中，拦截HTML遮罩网络请求";
    }

    /// <summary>当前脚本允许访问的 HTTP 地址模式（* 为通配符）；未启用 HTTP 权限时为空</summary>
    public static string[] AllowedHttpPatterns()
    {
        var currentProject = RunnerContext.Instance.CurrentScriptProject;
        return currentProject?.AllowJsHTTP == true ? currentProject.Project?.Manifest.HttpAllowedUrls ?? [] : [];
    }
}
