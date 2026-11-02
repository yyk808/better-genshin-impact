using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using BetterGenshinImpact.Avalonia.ViewModels;
using BetterGenshinImpact.Model;

namespace BetterGenshinImpact.Avalonia.Views;

public partial class OneDragonPage : UserControl
{
    public OneDragonPage() => InitializeComponent();

    /// <summary>正在拖动的任务（按下左侧把手时记录；按下的元素会捕获指针，松开事件仍发到把手上）</summary>
    private OneDragonTaskItem? _dragging;

    /// <summary>行右键菜单（新增 / 删除 / 从此执行 / 清除标记），与 WPF OneDragonFlowPage 的任务列表右键菜单相同</summary>
    private void OnRowMenuClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string action } menuItem || DataContext is not OneDragonPageViewModel vm) return;
        var item = menuItem.DataContext as OneDragonTaskItem;
        switch (action)
        {
            case "add": vm.AddTaskCommand.Execute(null); break;
            case "delete": vm.DeleteTaskCommand.Execute(item); break;
            case "next": vm.SetTaskAsNextCommand.Execute(item); break;
            case "clear": vm.ClearNextTaskCommand.Execute(null); break;
        }
    }

    private void OnDragHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: OneDragonTaskItem item } || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragging = item;
        e.Handled = true;
    }

    /// <summary>在另一行上松开：移到该行位置（与 WPF 拖动排序相同，排序立即保存）</summary>
    private void OnDragHandleReleased(object? sender, PointerReleasedEventArgs e)
    {
        var item = _dragging;
        _dragging = null;
        if (item is null || DataContext is not OneDragonPageViewModel vm) return;
        var target = TaskAt(e.GetPosition(this));
        if (target is not null && !ReferenceEquals(target, item)) vm.MoveTaskTo(item, target);
        e.Handled = true;
    }

    /// <summary>页面坐标下的任务行</summary>
    internal OneDragonTaskItem? TaskAt(Point point)
    {
        for (var v = this.InputHitTest(point) as Visual; v is not null; v = v.GetVisualParent())
        {
            if (v is Border { DataContext: OneDragonTaskItem item } border && border.Classes.Contains("odRow")) return item;
        }
        return null;
    }
}
