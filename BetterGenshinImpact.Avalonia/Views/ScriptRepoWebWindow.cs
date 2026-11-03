using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.WebView;
using BetterGenshinImpact.GameTask.AutoPathing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>
/// 本地脚本仓库网页（对应 WPF 的 WebView2 仓库窗口）：Linux 上由 WebKitGTK 显示同一份网页（Assets/Web/ScriptRepo）。
/// WebView2 的 <c>SetVirtualHostNameToFolderMapping("bettergi.local")</c> 改为 bgi-repo:// 自定义协议；
/// 页面调用的 <c>chrome.webview.hostObjects.repoWebBridge.方法(...)</c> 由注入的脚本转成对 bgi-repo://bettergi.local/__bridge/方法 的 POST 请求，
/// 交给与 Windows 版相同的 <see cref="RepoWebBridge"/>；页面里的外部链接用系统浏览器打开（与 Windows 版只放行 http、https 相同）
/// </summary>
public sealed class ScriptRepoWebWindow : Window
{
    public const string Scheme = "bgi-repo";
    public const string Host = "bettergi.local";
    private const string BridgePrefix = "/__bridge/";

    private static ScriptRepoWebWindow? _current;

    private readonly AllConfig _config;
    private readonly RepoWebBridge _bridge;
    private readonly string _assetsPath;
    private readonly TextBlock _error;
    private readonly NativeWebView _webView;

    /// <summary>注入到 index.html 的桥接脚本：提供与 WebView2 相同形式的 chrome.webview.hostObjects.repoWebBridge</summary>
    private const string BridgeShim = """
        <script>
        (function () {
          if (window.chrome && window.chrome.webview && window.chrome.webview.hostObjects) return;
          function call(name, args) {
            return fetch('/__bridge/' + encodeURIComponent(name), {
              method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(args)
            }).then(function (r) { return r.json(); }).then(function (r) {
              if (!r.ok) throw new Error(r.error || 'bridge error');
              return r.value;
            });
          }
          var bridge = new Proxy({}, {
            get: function (_, name) {
              if (typeof name !== 'string' || name === 'then') return undefined;
              return function () { return call(name, Array.prototype.slice.call(arguments)); };
            }
          });
          window.chrome = window.chrome || {};
          window.chrome.webview = {
            hostObjects: { repoWebBridge: bridge },
            postMessage: function () {}, addEventListener: function () {}, removeEventListener: function () {}
          };
        })();
        </script>
        """;

    private ScriptRepoWebWindow(AllConfig config)
    {
        _config = config;
        _bridge = new RepoWebBridge(config);
        _assetsPath = Path.GetFullPath(Global.Absolute(@"Assets\Web\ScriptRepo"));
        Title = "脚本仓库";
        RestoreBounds(config.ScriptConfig);

        var dark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        _error = new TextBlock { IsVisible = false, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 8), Foreground = Brushes.OrangeRed };
        _webView = new NativeWebView();
        _webView.EnvironmentRequested += (_, e) =>
        {
            if (e is GtkWebViewEnvironmentRequestedEventArgs gtk)
            {
                gtk.ApplicationNameForUserAgent = "BetterGI";
                // 与 Windows 版 App 环境的 Default 配置对应：数据放在 Data/WebKit/App（存储空间中可清理其缓存）
                gtk.BaseDataDirectory = BetterGenshinImpact.Host.Common.Storage.LinuxWebDataPaths.Data(BetterGenshinImpact.Host.Common.Storage.LinuxWebDataPaths.App);
                gtk.BaseCacheDirectory = BetterGenshinImpact.Host.Common.Storage.LinuxWebDataPaths.Cache(BetterGenshinImpact.Host.Common.Storage.LinuxWebDataPaths.App);
            }
        };
        _webView.AdapterCreated += (_, _) =>
        {
            if (_webView.TryGetPlatformHandle() is IGtkWebViewPlatformHandle gtk)
            {
                WebKitSchemeHandler.EnsureContextRegistered(WebKitSchemeHandler.GetContext(gtk.WebKitWebView), Scheme);
                WebKitSchemeHandler.AddAsyncRawHost(Scheme, Host, HandleAsync);
                _webView.Navigate(new Uri($"{Scheme}://{Host}/index.html"));
            }
            else
            {
                ShowError("当前网页引擎不支持自定义协议（需要 WebKitGTK 4.1），无法打开脚本仓库。");
            }
        };
        _webView.NavigationStarted += (_, e) =>
        {
            if (IsPageAddress(e.Request))
            {
                return;
            }

            e.Cancel = true;
            OpenExternal(e.Request);
        };
        _webView.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            OpenExternal(e.Request);
        };
        _webView.NavigationCompleted += (_, e) =>
        {
            if (IsPageAddress(e.Request))
            {
                LoadedPage = e.IsSuccess;
                if (e.IsSuccess) _error.IsVisible = false;
                else ShowError("仓库网页加载失败。");
            }
        };
        Closing += (_, _) => SaveBounds(config.ScriptConfig);
        Closed += (_, _) =>
        {
            WebKitSchemeHandler.RemoveAsyncRawHost(Scheme, Host);
            if (ReferenceEquals(_current, this)) _current = null;
        };

        var panel = new DockPanel();
        DockPanel.SetDock(_error, Dock.Top);
        panel.Children.Add(_error);
        panel.Children.Add(_webView);
        Content = panel;
        Background = dark ? new SolidColorBrush(Color.FromRgb(32, 32, 32)) : new SolidColorBrush(Color.FromRgb(243, 243, 243));
    }

    /// <summary>仓库网页已加载（自测用）</summary>
    public bool LoadedPage { get; private set; }

    /// <summary>显示的错误（自测用）</summary>
    public string? LastError => _error.IsVisible ? _error.Text : null;

    /// <summary>在页面中执行脚本并返回结果（自测用）</summary>
    public Task<string?> EvalAsync(string script) => _webView.InvokeScript(script);

    /// <summary>当前打开的仓库网页窗口（自测用）</summary>
    public static ScriptRepoWebWindow? Current => _current;

    /// <summary>打开或激活仓库网页窗口；无法打开时返回原因</summary>
    public static string? ShowOrActivate(AllConfig config)
    {
        if (HtmlSettingsWindow.Unavailable() is { } reason)
        {
            return reason;
        }

        if (!File.Exists(Path.Combine(Global.Absolute(@"Assets\Web\ScriptRepo"), "index.html")))
        {
            return $"缺少仓库网页文件：{Path.Combine(Global.Absolute(@"Assets\Web\ScriptRepo"), "index.html")}（随发布包提供）";
        }

        if (_current is { IsVisible: true })
        {
            _current.Activate();
            return null;
        }

        _current = new ScriptRepoWebWindow(config);
        _current.Show();
        return null;
    }

    private async Task<SchemeResponse> HandleAsync(string method, string path, Stream? body)
    {
        if (path.StartsWith(BridgePrefix, StringComparison.Ordinal))
        {
            if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                return Text(405, "Method Not Allowed", "text/plain", "");
            }

            var name = path[BridgePrefix.Length..];
            var args = body is null ? new JArray() : JArray.Parse(await new StreamReader(body, Encoding.UTF8).ReadToEndAsync());
            try
            {
                var value = await InvokeBridgeAsync(name, args);
                return Json(new JObject { ["ok"] = true, ["value"] = value is null ? JValue.CreateNull() : JToken.FromObject(value) });
            }
            catch (Exception e)
            {
                Log.Warning(e, "仓库网页桥接调用 {Name} 失败", name);
                return Json(new JObject { ["ok"] = false, ["error"] = e.Message });
            }
        }

        if (!method.Equals("GET", StringComparison.OrdinalIgnoreCase) && !method.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
        {
            return Text(405, "Method Not Allowed", "text/plain", "");
        }

        // 与 WebView2 的文件夹映射相同：只读取网页目录内的文件
        var relative = path.TrimStart('/');
        if (relative.Length == 0) relative = "index.html";
        var file = Path.GetFullPath(Path.Combine(_assetsPath, relative));
        if (!file.StartsWith(_assetsPath + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(file))
        {
            return Text(404, "Not Found", "text/plain", "");
        }

        var bytes = await File.ReadAllBytesAsync(file);
        var type = ContentType(file);
        if (relative.Equals("index.html", StringComparison.OrdinalIgnoreCase))
        {
            bytes = InjectShim(bytes);
        }

        return new SchemeResponse(200, "OK", type, bytes, "Cache-Control: no-store");
    }

    /// <summary>调用桥接方法（方法名与参数与 WebView2 hostObjects 相同）</summary>
    private async Task<object?> InvokeBridgeAsync(string name, JArray args)
    {
        string Arg(int i) => args.Count > i ? args[i].Type == JTokenType.String ? (string)args[i]! : args[i].ToString() : "";
        switch (name)
        {
            case nameof(RepoWebBridge.GetRepoJson): return await _bridge.GetRepoJson();
            case nameof(RepoWebBridge.ImportUri): _bridge.ImportUri(Arg(0)); return null;
            case nameof(RepoWebBridge.GetUserConfigJson): return await _bridge.GetUserConfigJson();
            case nameof(RepoWebBridge.GetSubscribedScriptPaths): return _bridge.GetSubscribedScriptPaths();
            case nameof(RepoWebBridge.GetFile): return await _bridge.GetFile(Arg(0));
            case nameof(RepoWebBridge.UpdateSubscribed): return await _bridge.UpdateSubscribed(Arg(0));
            case nameof(RepoWebBridge.ClearUpdate): return await _bridge.ClearUpdate();
            case nameof(RepoWebBridge.SetGuideStatus): return _bridge.SetGuideStatus(args.Count > 0 && args[0].Type == JTokenType.Boolean && (bool)args[0]);
            case nameof(RepoWebBridge.GetGuideStatus): return _bridge.GetGuideStatus();
            default: throw new InvalidOperationException($"未知的桥接方法：{name}");
        }
    }

    private static byte[] InjectShim(byte[] html)
    {
        var text = Encoding.UTF8.GetString(html);
        var head = text.IndexOf("<head>", StringComparison.OrdinalIgnoreCase);
        text = head >= 0 ? text.Insert(head + "<head>".Length, BridgeShim) : BridgeShim + text;
        return Encoding.UTF8.GetBytes(text);
    }

    private static string ContentType(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".ico" => "image/x-icon",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".ttf" => "font/ttf",
        _ => "application/octet-stream",
    };

    private static SchemeResponse Json(JObject value) =>
        new(200, "OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(value.ToString(Formatting.None)), "Cache-Control: no-store");

    private static SchemeResponse Text(int status, string reason, string type, string text) =>
        new(status, reason, type, Encoding.UTF8.GetBytes(text), "");

    private static bool IsPageAddress(Uri? uri) =>
        uri is not null && uri.Scheme == Scheme && uri.Host.Equals(Host, StringComparison.OrdinalIgnoreCase);

    /// <summary>外部链接用系统浏览器打开，只放行 http、https（与 Windows 版 WebViewSetup 相同）</summary>
    private static void OpenExternal(Uri? uri)
    {
        if (uri is null || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { uri.AbsoluteUri }, UseShellExecute = false });
        }
        catch (Exception e)
        {
            Log.Warning(e, "无法打开链接 {Uri}", uri);
        }
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.IsVisible = true;
    }

    /// <summary>窗口大小与位置：与 Windows 版相同保存在脚本配置（未保存时为屏幕工作区的 0.7 并居中，小屏最大化）</summary>
    private void RestoreBounds(ScriptConfig c)
    {
        var area = Screens.Primary?.WorkingArea;
        var scale = Screens.Primary?.Scaling ?? 1;
        var areaWidth = area is { } a1 ? a1.Width / scale : 1600;
        var areaHeight = area is { } a2 ? a2.Height / scale : 900;
        Width = c.WebviewWidth == 0 ? areaWidth * 0.7 : c.WebviewWidth;
        Height = c.WebviewHeight == 0 ? areaHeight * 0.7 : c.WebviewHeight;
        if (c.WebviewLeft == 0 && c.WebviewTop == 0)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint((int)(c.WebviewLeft * scale), (int)(c.WebviewTop * scale));
        }

        var small = areaWidth <= 1600 || areaHeight <= 900;
        WindowState = small || c.WebviewState == WindowDisplayState.Maximized ? WindowState.Maximized : WindowState.Normal;
    }

    private void SaveBounds(ScriptConfig c)
    {
        var scale = Screens.Primary?.Scaling ?? 1;
        c.WebviewState = WindowState == WindowState.Maximized ? WindowDisplayState.Maximized : WindowDisplayState.Normal;
        if (WindowState == WindowState.Normal)
        {
            c.WebviewLeft = Position.X / scale;
            c.WebviewTop = Position.Y / scale;
            c.WebviewWidth = Width;
            c.WebviewHeight = Height;
        }
    }
}
