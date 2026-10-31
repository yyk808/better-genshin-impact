using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// “新一条龙”页（对应 WPF PuloniaOneDragonPage）：左栏为当前一条龙配置的平面任务清单（状态点、启用开关、运行 / 停止），
/// 右栏为配置选择（新建 / 重命名 / 删除）与选中任务的设置。一条龙配置就是用途为 OneDragon 的 Pulonia 计划，
/// 编辑、保存、运行与触发都复用任务计划页的视图模型（Purpose = OneDragon），默认清单来自 Core PuloniaOneDragonDefaults
/// </summary>
public sealed partial class NewOneDragonPageViewModel(TaskPlanPageViewModel plans, Func<string, bool> navigate) : PageViewModel
{
    public override string Title => "新一条龙";

    /// <summary>一条龙配置的编辑（与任务计划页同一实现）</summary>
    public TaskPlanPageViewModel Plans => plans;

    /// <summary>右栏选项卡：0 任务设置，1 触发设置</summary>
    [ObservableProperty] private int _rightTab;

    /// <summary>添加任务面板是否展开</summary>
    [ObservableProperty] private bool _isAddPanelOpen;

    public override void OnNavigatedTo() => plans.OnNavigatedTo();

    /// <summary>执行记录：转到“执行记录”页（WPF 为弹窗）</summary>
    [RelayCommand]
    private void ShowHistory() => navigate("执行记录");

    /// <summary>触发设置：右栏切到触发方式（WPF 为“一条龙触发设置”弹窗）</summary>
    [RelayCommand]
    private void ShowTriggers() => RightTab = 1;

    [RelayCommand]
    private void ToggleAddPanel() => IsAddPanelOpen = !IsAddPanelOpen;
}
