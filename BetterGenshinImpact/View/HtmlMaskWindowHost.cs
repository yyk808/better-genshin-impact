using BetterGenshinImpact.Core.Script.Dependence;

namespace BetterGenshinImpact.View;

/// <summary><see cref="IHtmlMaskWindowHost"/> 的 WPF 实现，原样转发到 <see cref="HtmlMaskWindow"/> 的静态方法</summary>
public sealed class WpfHtmlMaskWindowHost : IHtmlMaskWindowHost
{
    public static WpfHtmlMaskWindowHost Instance { get; } = new();

    public string Show(string url, string? id, string workDir) => HtmlMaskWindow.Show(url, id, workDir);

    public bool Close(string id) => HtmlMaskWindow.Close(id);

    public string[] GetWindowIds() => HtmlMaskWindow.GetWindowIds();

    public bool Exists(string id) => HtmlMaskWindow.Exists(id);

    public void SetClickThrough(string windowId, bool enabled) => HtmlMaskWindow.SetClickThrough(windowId, enabled);

    public bool GetClickThrough(string windowId) => HtmlMaskWindow.GetClickThrough(windowId);

    public void ToggleClickThrough(string windowId) => HtmlMaskWindow.ToggleClickThrough(windowId);

    public void NotifyFlush(string windowId) => HtmlMaskWindow.NotifyFlush(windowId);

    public void CloseAll() => HtmlMaskWindow.CloseAll();
}
