using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>触发器草稿编辑器；所有快捷选项生成同一 Cron，不在输入未校验时修改计划。</summary>
public partial class PuloniaTaskTriggerEditorViewModel : ObservableObject
{
    /// <summary>保存前的独立副本，保留稳定身份。</summary>
    private readonly PuloniaTaskTrigger _original;
    /// <summary>批量初始化时避免反复计算预览。</summary>
    private bool _initializing = true;
    /// <summary>启用开关，只有保存后才影响调度。</summary>
    [ObservableProperty] private bool _enabled;
    /// <summary>原神服务器时区。</summary>
    [ObservableProperty] private PuloniaTaskServerTimeZone _serverTimeZone = PuloniaTaskServerTimeZone.UtcPlus8;
    /// <summary>捕获控件使用的类型化热键。</summary>
    [ObservableProperty] private HotKey _hotkey;
    /// <summary>复用软件现有的两种热键模式。</summary>
    [ObservableProperty] private HotKeyTypeEnum _hotkeyType;
    /// <summary>运行范围，空字符串表示整个计划。</summary>
    [ObservableProperty] private string _targetTaskId = "";
    /// <summary>账号参数资料引用，不执行切号。</summary>
    [ObservableProperty] private string _accountId = "";
    /// <summary>触发后允许延迟启动的分钟数。</summary>
    [ObservableProperty] private string _windowMinutes = "360";
    /// <summary>是否补触发。</summary>
    [ObservableProperty] private bool _catchUp = true;
    /// <summary>忙碌策略。</summary>
    [ObservableProperty] private PuloniaTaskBusyPolicy _busyPolicy;
    /// <summary>计算结果或错误，不把非法表达式提交到存储。</summary>
    [ObservableProperty] private string _previewText = "";
    /// <summary>当前草稿是否通过全部校验。</summary>
    [ObservableProperty] private bool _isValid;

    /// <summary>定时触发使用的 Cron 编辑器，表达式是唯一日程数据源。</summary>
    public PuloniaCronEditorViewModel CronEditor { get; }

    /// <summary>是否编辑热键配置。</summary>
    public bool IsHotkey => _original.Kind == PuloniaTaskTriggerKind.Hotkey;
    /// <summary>是否编辑日程配置。</summary>
    public bool IsSchedule => !IsHotkey;
    /// <summary>捕获控件识别的类型名称，与快捷键设置页保持一致。</summary>
    public string HotkeyTypeName => HotkeyType.ToChineseName();
    /// <summary>界面展示的本地化类型名称。</summary>
    public string LocalizedHotkeyTypeName => HotkeyType.ToLocalizedName();
    /// <summary>当前热键类型支持的输入及生效条件。</summary>
    public string HotkeyHelp => HotkeyType == HotKeyTypeEnum.GlobalRegister
        ? "全局热键：支持组合键或功能键，BGI 运行时生效。普通键请搭配 Ctrl / Alt / Win；F12 为系统保留。"
        : "键鼠监听：支持键盘单键或鼠标侧键，不支持组合键。与现有快捷键一致，启动功能、开启键鼠监听且游戏在前台时生效。";
    /// <summary>原神服务器使用的固定时区选项。</summary>
    public IReadOnlyDictionary<PuloniaTaskServerTimeZone, string> ServerTimeZones { get; } = PuloniaTriggerEditing.ServerTimeZones;
    /// <summary>忙碌策略的用户文本。</summary>
    public IReadOnlyDictionary<PuloniaTaskBusyPolicy, string> BusyPolicies { get; } = PuloniaTriggerEditing.BusyPolicies;
    /// <summary>完整计划或编辑树内可选子树。</summary>
    public IReadOnlyDictionary<string, string> Targets { get; }
    /// <summary>当前计划绑定的账号参数资料。</summary>
    public IReadOnlyDictionary<string, string> Accounts { get; }

    /// <summary>一条龙始终使用自身账号绑定，不能由触发器建立第二套选择。</summary>
    public bool UsesPlanAccountBinding { get; }
    /// <summary>普通计划仍允许选择原有账号参数资料；一条龙不显示账号下拉框。</summary>
    public bool CanSelectAccount => !UsesPlanAccountBinding;
    /// <summary>账号区域的标题：一条龙表示实际运行账号，普通计划表示只提供参数的账号资料。</summary>
    public string AccountSelectionTitle => UsesPlanAccountBinding ? "运行账号" : "账号参数资料（不会自动切换账号）";
    /// <summary>当前入口的账号选择语义。</summary>
    public string AccountSelectionDescription => UsesPlanAccountBinding
        ? "使用这份一条龙配置绑定的账号：运行前切换到该账号并核验 UID。可在一条龙页面的“切换账号”中修改。"
        : "只选择参数使用的账号资料；运行时使用当前登录的账号，不会自动切换。";

    /// <summary>从计划和触发器建立独立草稿。</summary>
    public PuloniaTaskTriggerEditorViewModel(PuloniaTaskTrigger trigger, PuloniaTaskPlan plan)
    {
        _original = trigger.Clone();
        CronEditor = new PuloniaCronEditorViewModel(trigger.Cron);
        CronEditor.PropertyChanged += OnCronEditorPropertyChanged;
        UsesPlanAccountBinding = PuloniaTriggerEditing.UsesPlanAccountBinding(plan);
        Enabled = trigger.Enabled; ServerTimeZone = trigger.ServerTimeZone;
        HotkeyType = trigger.HotkeyType; Hotkey = HotKey.FromString(trigger.Hotkey);
        TargetTaskId = trigger.TargetTaskId ?? ""; AccountId = trigger.AccountId ?? "";
        WindowMinutes = trigger.WindowMinutes.ToString(CultureInfo.InvariantCulture);
        CatchUp = trigger.CatchUp; BusyPolicy = trigger.BusyPolicy;
        Targets = PuloniaTriggerEditing.BuildTargets(plan);
        Accounts = PuloniaTriggerEditing.BuildAccounts(plan);
        if (UsesPlanAccountBinding) AccountId = "";
        _initializing = false;
        RefreshPreview();
    }


    /// <summary>字段变化后更新预览。</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_initializing || e.PropertyName is nameof(PreviewText) or nameof(IsValid)) return;
        RefreshPreview();
    }

    /// <summary>Cron 表达式变化后刷新父编辑器的校验与执行预览。</summary>
    private void OnCronEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_initializing && e.PropertyName == nameof(PuloniaCronEditorViewModel.Expression)) RefreshPreview();
    }

    /// <summary>沿用快捷键页面的类型切换交互，清空不兼容的旧键值，等待用户重新捕获。</summary>
    [RelayCommand]
    private void SwitchHotkeyType()
        => HotkeyType = HotkeyType == HotKeyTypeEnum.GlobalRegister ? HotKeyTypeEnum.KeyboardMonitor : HotKeyTypeEnum.GlobalRegister;

    /// <summary>切换类型时同步输入规则、帮助和预览，不将组合键偷偷转换成监听单键。</summary>
    partial void OnHotkeyTypeChanged(HotKeyTypeEnum value)
    {
        if (!_initializing) Hotkey = HotKey.None;
        OnPropertyChanged(nameof(HotkeyTypeName));
        OnPropertyChanged(nameof(LocalizedHotkeyTypeName));
        OnPropertyChanged(nameof(HotkeyHelp));
    }

    /// <summary>卡片开关只同步已保存的启用状态与生效点，其他未保存字段继续保留为草稿。</summary>
    public void SynchronizeEnabled(PuloniaTaskTrigger saved)
    {
        _original.Enabled = saved.Enabled;
        _original.ActivatedAtUtc = saved.ActivatedAtUtc;
        Enabled = saved.Enabled;
    }


    /// <summary>创建已校验的提交副本，并用当前时间标记本次配置版本。</summary>
    public PuloniaTaskTrigger CreateTrigger()
    {
        var result = BuildTrigger();
        result.ActivatedAtUtc = DateTimeOffset.UtcNow;
        return result;
    }

    /// <summary>构造用于校验和预览的草稿，不改变配置版本。</summary>
    private PuloniaTaskTrigger BuildTrigger()
    {
        var result = _original.Clone();
        result.Enabled = Enabled; result.ServerTimeZone = ServerTimeZone;
        result.Hotkey = Hotkey.ToString(); result.HotkeyType = HotkeyType;
        result.TargetTaskId = string.IsNullOrEmpty(TargetTaskId) ? null : TargetTaskId;
        result.AccountId = UsesPlanAccountBinding || string.IsNullOrEmpty(AccountId) ? null : AccountId;
        result.WindowMinutes = PuloniaTriggerEditing.ParseWindowMinutes(WindowMinutes);
        result.CatchUp = CatchUp; result.BusyPolicy = BusyPolicy;
        if (IsSchedule)
            result.Cron = CronEditor.Expression.Trim();
        PuloniaTaskSchedule.Validate(result);
        // 计划校验不检查键位组合，保存前由热键规则拦截当前模式不支持的键。
        if (IsHotkey)
            PuloniaTaskHotkeyValidator.Validate(result);
        return result;
    }

    /// <summary>预览五次执行，非法配置直接展示错误并阻止保存。</summary>
    private void RefreshPreview()
    {
        try
        {
            var trigger = BuildTrigger();
            if (IsHotkey)
            {
                PreviewText = "保存后在左侧卡片启用。" + HotkeyHelp + " 快捷键冲突会在下方显示。";
                IsValid = true;
                return;
            }
            PreviewText = PuloniaTriggerEditing.PreviewSchedule(trigger, DateTimeOffset.UtcNow);
            IsValid = true;
        }
        catch (Exception ex) { PreviewText = "配置无效：" + ex.Message; IsValid = false; }
    }
}
