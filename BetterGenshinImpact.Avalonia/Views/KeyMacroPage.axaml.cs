using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BetterGenshinImpact.Avalonia.ViewModels;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>按键宏页（对应 WPF KeyMacroPage）</summary>
public partial class KeyMacroPage : UserControl
{
    public KeyMacroPage()
    {
        InitializeComponent();
        // 快捷键录制：隧道阶段拦截按键（与设置页、计划快捷键录制相同）
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not KeyMacroPageViewModel { SelectedMacro: { IsRecording: true } item }) return;
        var m = e.KeyModifiers;
        if (item.OnRecordKey(e.Key.ToString(), m.HasFlag(KeyModifiers.Control), m.HasFlag(KeyModifiers.Shift), m.HasFlag(KeyModifiers.Alt),
                m.HasFlag(KeyModifiers.Meta)))
        {
            e.Handled = true;
        }
    }
}
