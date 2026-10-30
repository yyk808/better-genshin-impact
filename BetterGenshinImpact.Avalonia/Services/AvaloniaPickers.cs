using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using BetterGenshinImpact.Avalonia.Views;
using BetterGenshinImpact.Host.Common;

namespace BetterGenshinImpact.Avalonia.Services;

/// <summary>需要主窗口作为所有者的选择对话框（窗口选择、文件选择）</summary>
public static class AvaloniaPickers
{
    public static Window? MainWindow =>
        (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    /// <summary>最近打开的窗口选择对话框（无人值守验证用）</summary>
    public static WindowPickerWindow? CurrentWindowPicker { get; private set; }

    public static async Task<WindowInfo?> PickWindowAsync(Func<IReadOnlyList<WindowInfo>> list, nint? current)
    {
        var owner = MainWindow;
        var picker = new WindowPickerWindow(list, current);
        CurrentWindowPicker = picker;
        try
        {
            return owner is null ? null : await picker.ShowDialog<WindowInfo?>(owner);
        }
        finally
        {
            CurrentWindowPicker = null;
        }
    }

    public static async Task<string?> PickFileAsync(string title)
    {
        if (MainWindow?.StorageProvider is not { CanOpen: true } storage)
        {
            return null;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("可执行文件") { Patterns = ["*.exe"] }, FilePickerFileTypes.All],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
