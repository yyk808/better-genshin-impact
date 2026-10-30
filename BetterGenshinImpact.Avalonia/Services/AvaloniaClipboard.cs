using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using BetterGenshinImpact.Platform;

namespace BetterGenshinImpact.Avalonia.Services;

/// <summary>
/// UiHost.Clipboard 的 Avalonia 实现：经主窗口的 TopLevel.Clipboard 读写文本。
/// 剪贴板接口是异步的：写入排队到 UI 线程；在非 UI 线程读取时等待结果，
/// 在 UI 线程上同步读取会死锁，此时返回本进程最近写入的文本
/// </summary>
public sealed class AvaloniaClipboard(Func<TopLevel?> topLevel) : IClipboardService
{
    private volatile string? _lastText;

    public void SetText(string text)
    {
        _lastText = text;
        Dispatcher.UIThread.Post(async () =>
        {
            if (topLevel()?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
            }
        });
    }

    public string? GetText()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return _lastText;
        }

        return Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var clipboard = topLevel()?.Clipboard;
            return clipboard is null ? _lastText : await clipboard.TryGetTextAsync();
        }).GetAwaiter().GetResult();
    }

    public void Clear()
    {
        _lastText = null;
        Dispatcher.UIThread.Post(async () =>
        {
            if (topLevel()?.Clipboard is { } clipboard)
            {
                await clipboard.ClearAsync();
            }
        });
    }
}
