using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Core.Script.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>
/// 展示脚本作者实现的 HTML 设置页（对应 WPF JsScriptHtmlSettingsWindow）。
/// 网页由 Avalonia NativeWebView（Linux 上为系统 WebKitGTK）显示，页面的全部请求经 bgi-settings:// 协议交给
/// Core 的 <see cref="ScriptSettingsRequestHandler"/>，不联网、不监听端口；跳转到其他地址一律取消
/// </summary>
public sealed class HtmlSettingsWindow : Window
{
    private readonly ScriptSettingsHtmlPage _page;
    private readonly string _baseTitle;
    private readonly TextBlock _error;
    private readonly NativeWebView _webView;

    private HtmlSettingsWindow(ScriptSettingsEditRequest request)
    {
        _page = request.Page;
        _baseTitle = $"脚本设置：{_page.ScriptName} {_page.ScriptVersion} · 页面由脚本作者提供";
        Title = _baseTitle;
        Width = 960;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var dark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        var handler = new ScriptSettingsRequestHandler(request with
        {
            // 保存成功后在标题中显示时间（保存在 WebKit 线程上执行，标题回到界面线程更新）
            Save = settings =>
            {
                request.Save(settings);
                Dispatcher.UIThread.Post(() => Title = $"{_baseTitle} · 已保存 {DateTime.Now:HH:mm:ss}");
            }
        }, dark ? "dark" : "light", () => Dispatcher.UIThread.Post(Close), NullLogger.Instance);

        _error = new TextBlock { IsVisible = false, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 8), Foreground = Brushes.OrangeRed };
        _webView = new NativeWebView();
        // 不保存网站数据（表单、缓存），与 Windows 版关闭自动填充一致
        _webView.EnvironmentRequested += (_, e) =>
        {
            if (e is GtkWebViewEnvironmentRequestedEventArgs gtk)
            {
                gtk.EphemeralDataManager = true;
                gtk.ApplicationNameForUserAgent = "BetterGI";
            }
        };
        _webView.AdapterCreated += (_, _) =>
        {
            if (_webView.TryGetPlatformHandle() is IGtkWebViewPlatformHandle gtk)
            {
                WebKitSchemeHandler.EnsureRegistered(gtk.WebKitWebView);
                WebKitSchemeHandler.AddHost(_page.HostName, handler.Handle);
                _webView.Navigate(WebKitSchemeHandler.EntryUri(_page));
            }
            else
            {
                ShowError("当前网页引擎不支持自定义协议（需要 WebKitGTK 4.1），无法打开设置页。");
            }
        };
        _webView.NavigationStarted += (_, e) => e.Cancel = !IsPageAddress(e.Request);
        _webView.NewWindowRequested += (_, e) => e.Handled = true;
        _webView.NavigationCompleted += (_, e) =>
        {
            if (IsPageAddress(e.Request))
            {
                if (e.IsSuccess) _error.IsVisible = false;
                else ShowError("页面加载失败。修改页面文件后可以重新打开设置窗口。");
            }
        };
        Closed += (_, _) => WebKitSchemeHandler.RemoveHost(_page.HostName);

        var panel = new DockPanel();
        DockPanel.SetDock(_error, Dock.Top);
        panel.Children.Add(_error);
        panel.Children.Add(_webView);
        Content = panel;
        Background = dark ? new SolidColorBrush(Color.FromRgb(32, 32, 32)) : new SolidColorBrush(Color.FromRgb(243, 243, 243));
    }

    /// <summary>系统中可用的网页引擎；不可用时返回原因</summary>
    public static string? Unavailable()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        try
        {
            var gtk = WebViewAdapterInfo.GetAdapterInfo(WebViewAdapterType.WebKitGtk);
            return gtk.IsInstalled ? null : "未安装 WebKitGTK 4.1（Debian/Ubuntu：libwebkit2gtk-4.1-0）：" + gtk.UnavailableReason;
        }
        catch (Exception e)
        {
            return "无法检测网页引擎：" + e.Message;
        }
    }

    /// <summary>打开设置页；无法打开时返回原因</summary>
    public static string? Show(ScriptSettingsEditRequest request, Window? owner)
    {
        if (Unavailable() is { } reason)
        {
            return reason;
        }

        var window = new HtmlSettingsWindow(request);
        if (owner is not null)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }

        return null;
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.IsVisible = true;
    }

    private bool IsPageAddress(Uri? uri)
        => uri is not null && uri.Scheme == WebKitSchemeHandler.Scheme && uri.Host.Equals(_page.HostName, StringComparison.OrdinalIgnoreCase);
}
