using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.ComponentModel;
using System.Threading.Tasks;
using Windows.System;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Pages.View;

public partial class ScriptGroupConfigViewModel : ObservableObject, IViewModel
{
    /// <summary>
    /// 是否展示行走、战斗与食物设置；Shell 节点只展示 Shell 配置。
    /// </summary>
    [ObservableProperty]
    private bool _showPathingSettings = true;

    /// <summary>
    /// 是否展示 Shell 执行配置。
    /// </summary>
    [ObservableProperty]
    private bool _showShellSettings = true;

    /// <summary>
    /// 是否展示旧调度器的时段和周期设置。
    /// </summary>
    [ObservableProperty]
    private bool _showLegacySchedulingSettings = true;

    /// <summary>
    /// 是否展示内嵌标题栏；嵌入带标题栏的 Pulonia 对话框时关闭。
    /// </summary>
    [ObservableProperty]
    private bool _showTitleBar = true;

    /// <summary>
    /// 是否使用 Pulonia 的进程超时语义说明。
    /// </summary>
    [ObservableProperty]
    private bool _isPulonia;

    [ObservableProperty]
    private AutoFightViewModel _autoFightViewModel;

    [ObservableProperty]
    private ScriptGroupConfig _scriptGroupConfig;

    [ObservableProperty]
    private PathingPartyConfig _pathingConfig;

    [ObservableProperty]
    private ShellConfig _shellConfig;

    [ObservableProperty]
    private bool _enableShellConfig;
    [ObservableProperty]
    private ObservableCollection<KeyValuePair<string, string>> _onlyPickEliteDropsSource = new(
        ScriptGroupConfigFields.OnlyPickEliteDropsModes.Select(o => new KeyValuePair<string, string>((string)o.Value, o.Text)));
    [ObservableProperty]
    private ObservableCollection<KeyValuePair<RecoverTiming, string>> _recoverTimingSource = new(
        ScriptGroupConfigFields.RecoverTimings.Select(o => new KeyValuePair<RecoverTiming, string>((RecoverTiming)o.Value, o.Text)));
    public ScriptGroupConfigViewModel(AllConfig config, ScriptGroupConfig scriptGroupConfig)
    {
        ScriptGroupConfig = scriptGroupConfig;
        PathingConfig = scriptGroupConfig.PathingConfig;
        PathingConfig.PropertyChanged += OnPathingConfigPropertyChanged;
        AutoFightViewModel = new AutoFightViewModel(config);
        ShellConfig = scriptGroupConfig.ShellConfig;
        EnableShellConfig = scriptGroupConfig.EnableShellConfig;
    }

    // 赶路角色为 自动 或 玛薇卡 时显示跳飞相关配置
    public bool IsHurryOnMwkOrAuto => ScriptGroupConfigFields.IsHurryOnMwkOrAuto(PathingConfig);

    // 赶路角色为 自动/玛薇卡/闲云 时显示跳飞间隔配置
    public bool IsHurryOnMwkOrAutoOrXianyun => ScriptGroupConfigFields.IsHurryOnMwkOrAutoOrXianyun(PathingConfig);

    // 跳飞启用距离：仅当角色为 自动/玛薇卡 且启用跳飞时显示
    public bool IsJumpFlyDistanceVisible => ScriptGroupConfigFields.IsJumpFlyDetailVisible(PathingConfig);

    // 跳飞前额外冲刺次数：仅当角色为 自动/玛薇卡 且启用跳飞时显示
    public bool IsJumpFlySprintCountVisible => ScriptGroupConfigFields.IsJumpFlyDetailVisible(PathingConfig);

    // 玛薇卡在车上禁用冲刺：仅当角色为 自动/玛薇卡 时显示
    public bool IsDisableSprintVisible => IsHurryOnMwkOrAuto;

    // 切人步行：仅当角色为 自动 或实际应用了该选项的角色（玛薇卡/希诺宁）时显示
    public bool IsSwitchToWalkVisible => ScriptGroupConfigFields.IsSwitchToWalkVisible(PathingConfig);

    // 接近停车距离：仅当角色为 自动 或存在接近停车逻辑的角色（闲云/法尔伽无此逻辑）时显示
    public bool IsApproachStopDistanceVisible => ScriptGroupConfigFields.IsApproachStopDistanceVisible(PathingConfig);

    private void OnPathingConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PathingPartyConfig.HurryOnAvatar) or nameof(PathingPartyConfig.MwkJumpFlyEnabled))
        {
            OnPropertyChanged(nameof(IsHurryOnMwkOrAuto));
            OnPropertyChanged(nameof(IsHurryOnMwkOrAutoOrXianyun));
            OnPropertyChanged(nameof(IsJumpFlyDistanceVisible));
            OnPropertyChanged(nameof(IsJumpFlySprintCountVisible));
            OnPropertyChanged(nameof(IsDisableSprintVisible));
            OnPropertyChanged(nameof(IsSwitchToWalkVisible));
            OnPropertyChanged(nameof(IsApproachStopDistanceVisible));
        }
    }

    [RelayCommand]
    private void OnStrategyDropDownOpened(string type)
    {
        AutoFightViewModel.OnStrategyDropDownOpened(type);
    }

    [RelayCommand]
    public void OnOpenLocalScriptRepo()
    {
        AutoFightViewModel.OnOpenLocalScriptRepo();
    }
    [RelayCommand]
    public void OnGetExecutionOrder()
    {
        var index = _pathingConfig.TaskCycleConfig.GetExecutionOrder();
        if (index == -1)
        {
            Toast.Error("计算失败，请检查参数！");
        }
        else
        {
            Toast.Success("当前执行序号为："+index);
        }
    }

    [RelayCommand]
    public void OnOpenFightFolder()
    {
        AutoFightViewModel.OnOpenFightFolder();
    }
    
    [RelayCommand]
    private void OnAutoFightEnabledChecked()
    {
        PathingConfig.Enabled = true;
    }

    [RelayCommand]
    private async Task OnGoToAutoEatUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/dev/js/dispatcher.html#autoeat-自动吃食物"));
    }
}
