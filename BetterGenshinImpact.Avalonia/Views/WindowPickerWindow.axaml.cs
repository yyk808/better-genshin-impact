using Avalonia.Controls;
using Avalonia.Interactivity;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Host.Common;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>手动选择捕获窗口（对应 WPF 的“选择捕获窗口”）</summary>
public partial class WindowPickerWindow : Window
{
    private readonly Func<IReadOnlyList<WindowInfo>>? _list;
    private readonly nint? _current;

    public WindowPickerWindow() => InitializeComponent();

    public WindowPickerWindow(Func<IReadOnlyList<WindowInfo>> list, nint? current)
    {
        InitializeComponent();
        FontFamily = FontInfo.UiFontFamily;
        _list = list;
        _current = current;
        Refresh();
    }

    public IReadOnlyList<WindowInfo> Items { get; private set; } = [];

    private void Refresh()
    {
        Items = _list?.Invoke() ?? [];
        WindowList.ItemsSource = Items;
        WindowList.SelectedItem = Items.FirstOrDefault(w => w.Handle == _current) ?? Items.FirstOrDefault(w => w.IsGameCandidate);
    }

    /// <summary>选中指定窗口并确定（无人值守验证用）</summary>
    public bool SelectAndConfirm(Func<WindowInfo, bool> predicate)
    {
        var item = Items.FirstOrDefault(predicate);
        if (item is null)
        {
            return false;
        }

        WindowList.SelectedItem = item;
        Close(item);
        return true;
    }

    private void OnRefresh(object? sender, RoutedEventArgs e) => Refresh();

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        if (WindowList.SelectedItem is WindowInfo info)
        {
            Close(info);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
