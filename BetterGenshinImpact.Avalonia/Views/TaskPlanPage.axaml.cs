using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BetterGenshinImpact.Avalonia.ViewModels;

namespace BetterGenshinImpact.Avalonia.Views;

public partial class TaskPlanPage : UserControl
{
    public TaskPlanPage()
    {
        InitializeComponent();
        // 快捷键触发的录制：隧道阶段拦截按键（与设置页快捷键录制相同）
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not TaskPlanPageViewModel { TriggerEditor: { IsRecording: true } editor }) return;
        var m = e.KeyModifiers;
        if (editor.OnRecordKey(e.Key.ToString(), m.HasFlag(KeyModifiers.Control), m.HasFlag(KeyModifiers.Shift), m.HasFlag(KeyModifiers.Alt),
                m.HasFlag(KeyModifiers.Meta)))
        {
            e.Handled = true;
        }
    }
}
