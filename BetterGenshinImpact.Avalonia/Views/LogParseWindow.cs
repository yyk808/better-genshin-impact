using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>
/// 调度器“日志分析”的网页窗口（对应 WPF WebpageWindow.NavigateToHtml）：显示 Core LogParse 生成的 HTML，
/// 或其超过 1 MB 时保存的 log/logparse/*.html；网页由 Avalonia NativeWebView（Linux 上为 WebKitGTK）显示，
/// 不保存网站数据；页面内的外部链接（如米游社 Cookie 获取说明）用系统浏览器（xdg-open）打开
/// </summary>
public sealed class LogParseWindow : Window
{
    private readonly NativeWebView _webView;

    /// <summary>最近打开的窗口及其内容（界面自测用；网页内容无法从窗口读出）</summary>
    public static LogParseWindow? Current { get; private set; }

    public string Html { get; }

    private LogParseWindow(string title, string html)
    {
        Html = html;
        Title = title;
        Width = 800;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var dark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        Background = dark ? new SolidColorBrush(Color.FromRgb(32, 32, 32)) : new SolidColorBrush(Color.FromRgb(243, 243, 243));
        _webView = new NativeWebView();
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
            // 内容过大时 Core 返回已保存文件的 file:// 地址，否则为 HTML 文本（同 WPF NavigateToHtml）
            if (Uri.TryCreate(html, UriKind.Absolute, out var file) && file.IsFile)
            {
                _webView.Navigate(file);
            }
            else
            {
                _webView.NavigateToString(html, null!);
            }
        };
        _webView.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            OpenExternal(e.Request);
        };
        Content = _webView;
        Closed += (_, _) =>
        {
            if (Current == this) Current = null;
        };
    }

    /// <summary>打开日志分析窗口；<paramref name="modal"/> 时等待关闭（同 WPF 结果窗口 ShowDialog），否则立即返回。网页引擎不可用时返回原因</summary>
    public static async Task<string?> ShowAsync(string title, string html, Window? owner, bool modal)
    {
        if (HtmlSettingsWindow.Unavailable() is { } reason)
        {
            return reason;
        }

        var window = new LogParseWindow(title, html);
        Current = window;
        if (owner is null)
        {
            window.Show();
        }
        else if (modal)
        {
            await window.ShowDialog(owner);
        }
        else
        {
            window.Show(owner);
        }

        return null;
    }

    private static void OpenExternal(Uri? uri)
    {
        if (uri is null || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { uri.AbsoluteUri }, UseShellExecute = false });
        }
        catch (Exception)
        {
            // 没有 xdg-open 时忽略（同 WPF 打开失败不提示）
        }
    }
}
