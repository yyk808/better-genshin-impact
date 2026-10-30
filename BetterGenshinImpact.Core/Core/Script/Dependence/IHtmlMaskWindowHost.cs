using System;

namespace BetterGenshinImpact.Core.Script.Dependence;

/// <summary>
/// 脚本 HTML 遮罩窗口的宿主操作。Windows 实现转发到 HtmlMaskWindow 的静态方法（WebView2 窗口）；
/// 未设置时为不支持 HTML 遮罩的实现：打开窗口时抛出异常，其余查询视为窗口不存在
/// </summary>
public interface IHtmlMaskWindowHost
{
    /// <summary>显示 HTML 遮罩窗口，返回窗口 ID</summary>
    string Show(string url, string? id, string workDir);

    bool Close(string id);

    string[] GetWindowIds();

    bool Exists(string id);

    void SetClickThrough(string windowId, bool enabled);

    bool GetClickThrough(string windowId);

    void ToggleClickThrough(string windowId);

    /// <summary>通知窗口刷新待推送的消息</summary>
    void NotifyFlush(string windowId);
}

public static class HtmlMaskWindowHost
{
    private static IHtmlMaskWindowHost _current = new UnsupportedHtmlMaskWindowHost();

    public static IHtmlMaskWindowHost Current
    {
        get => _current;
        set => _current = value ?? throw new ArgumentNullException(nameof(value));
    }

    private sealed class UnsupportedHtmlMaskWindowHost : IHtmlMaskWindowHost
    {
        public string Show(string url, string? id, string workDir) =>
            throw new InvalidOperationException("当前平台不支持 HTML 遮罩");

        public bool Close(string id) => false;

        public string[] GetWindowIds() => [];

        public bool Exists(string id) => false;

        public void SetClickThrough(string windowId, bool enabled) =>
            throw new InvalidOperationException($"HTML遮罩窗口不存在或已关闭: {windowId}");

        public bool GetClickThrough(string windowId) =>
            throw new InvalidOperationException($"HTML遮罩窗口不存在或已关闭: {windowId}");

        public void ToggleClickThrough(string windowId) =>
            throw new InvalidOperationException($"HTML遮罩窗口不存在或已关闭: {windowId}");

        public void NotifyFlush(string windowId)
        {
        }
    }
}
