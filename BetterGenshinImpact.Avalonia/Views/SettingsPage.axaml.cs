using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BetterGenshinImpact.Avalonia.ViewModels;

namespace BetterGenshinImpact.Avalonia.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        // 快捷键录制：在隧道阶段拦截按键，避免焦点控件（按钮等）先处理空格 / 回车
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        // 进入页面时统计存储空间摘要（一分钟内重复进入沿用上次结果，同 WPF）
        AttachedToVisualTree += (_, _) => _ = (DataContext as SettingsPageViewModel)?.RefreshStorageSummaryAsync(force: false);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not SettingsPageViewModel vm)
        {
            return;
        }

        // Avalonia 的 Key 成员名与 WPF 的 Key 相同（F11、D1、OemTilde 等），与 WPF 版的快捷键写法一致
        var m = e.KeyModifiers;
        if (vm.OnRecordKey(e.Key.ToString(), m.HasFlag(KeyModifiers.Control), m.HasFlag(KeyModifiers.Shift), m.HasFlag(KeyModifiers.Alt),
                m.HasFlag(KeyModifiers.Meta)))
        {
            e.Handled = true;
        }
    }
}
