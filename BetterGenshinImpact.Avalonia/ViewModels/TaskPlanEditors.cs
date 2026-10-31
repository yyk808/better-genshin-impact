using System.Collections.ObjectModel;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.ViewModel.SoloTaskSettings;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>任务设置面板：名称、启用、分组执行次数与按 Schema 生成的参数字段（规则在 Core 的 PuloniaParameterForm）</summary>
public sealed partial class TaskNodeEditor : ObservableObject
{
    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _repeatText;

    public TaskNodeEditor(TaskPlanNodeRow row, PuloniaTaskDefinition? definition, IServiceProvider? services = null, string? planId = null)
    {
        Row = row;
        Definition = definition;
        _name = row.Task.Name;
        _isEnabled = row.Task.IsEnabled;
        _repeatText = (row.Task.RepeatCount ?? 1).ToString();
        IsGroup = row.Task.TaskType == "group" && row.Task.Source is null;
        if (definition is not null)
        {
            // 与 WPF PuloniaTaskSettingsEditorViewModel 相同：有专属界面时优先使用，专属界面编辑的字段不再出现在通用字段中
            Dedicated = DedicatedTaskSettings.TryCreate(definition.TaskType, PuloniaParameterForm.Effective(definition, row.Task), services);
            // 首领讨伐显示本任务的累计领奖次数（与 WPF 任务设置对话框相同）
            if (Dedicated is AutoBossSettingsViewModel boss && planId is not null)
            {
                _ = boss.SetProgressScopeAsync(planId, row.Task.Id);
            }

            var edited = new HashSet<string>(Dedicated?.EditedParameterNames ?? [], StringComparer.Ordinal);
            foreach (var field in PuloniaParameterForm.Describe(definition, row.Task))
            {
                if (!edited.Contains(field.Name))
                {
                    Fields.Add(new TaskParameterFieldEditor(field));
                }
            }
        }
    }

    public TaskPlanNodeRow Row { get; }

    public PuloniaTaskDefinition? Definition { get; }

    public bool IsGroup { get; }

    public string TypeText => Row.TypeText;

    public ObservableCollection<TaskParameterFieldEditor> Fields { get; } = [];

    public bool HasFields => Fields.Count > 0;

    /// <summary>专属参数编辑器（VM 在 Core）；没有时为 null</summary>
    public SoloTaskSettingsViewModel? Dedicated { get; }

    public bool HasDedicated => Dedicated is not null;

    public string FieldsHint => Definition is null ? "未注册的任务类型，只能修改名称与启用状态。" : HasFields || HasDedicated ? "" : "此任务没有可编辑的参数。";

    /// <summary>有专属编辑器时通用字段的标题</summary>
    public string FieldsTitle => HasDedicated ? "其他参数" : "参数";

    /// <summary>收集修改过的参数</summary>
    public JObject CollectChanges()
    {
        var changes = new JObject();
        Dedicated?.CollectChanges(changes);
        foreach (var f in Fields)
        {
            if (f.BuildIfChanged() is { } value)
            {
                changes[f.Field.Name] = value;
            }
        }

        return changes;
    }
}

/// <summary>一个参数字段的输入状态</summary>
public sealed partial class TaskParameterFieldEditor : ObservableObject
{
    private readonly string _initialText;
    private readonly bool _initialBool;

    [ObservableProperty] private string _text;
    [ObservableProperty] private bool _boolValue;

    public TaskParameterFieldEditor(PuloniaParameterField field)
    {
        Field = field;
        _text = _initialText = field.Text;
        _boolValue = _initialBool = field.Value?.Type == JTokenType.Boolean && field.Value.Value<bool>();
    }

    public PuloniaParameterField Field { get; }

    public string Name => Field.Name;

    public string Label => Field.DisplayName + (Field.Required ? " *" : "") + (Field.IsOverridden ? "（已修改）" : "");

    public string Description => Field.Description;

    public bool HasDescription => !string.IsNullOrEmpty(Field.Description);

    public bool IsBoolean => Field.Kind == PuloniaParameterFieldKind.Boolean;

    public bool IsChoice => Field.Kind == PuloniaParameterFieldKind.Choice;

    public bool IsTextInput => !IsBoolean && !IsChoice;

    public bool IsMultiline => Field.Kind is PuloniaParameterFieldKind.StringList or PuloniaParameterFieldKind.Json;

    public IReadOnlyList<string> Choices => Field.Choices;

    public string? SelectedChoice
    {
        get => Field.Choices.Contains(Text) ? Text : null;
        set
        {
            Text = value ?? "";
            OnPropertyChanged();
        }
    }

    /// <summary>未修改时返回 null；修改后解析为参数值（无法解析时抛出 FormatException）</summary>
    public JToken? BuildIfChanged()
    {
        if (IsBoolean ? BoolValue == _initialBool : Text == _initialText)
        {
            return null;
        }

        return PuloniaParameterForm.Parse(Field, Text, IsBoolean ? BoolValue : null);
    }
}

/// <summary>触发方式列表中的一行</summary>
public sealed class TaskPlanTriggerRow(TaskPlanPageViewModel owner, PuloniaTaskTrigger trigger)
{
    public PuloniaTaskTrigger Trigger => trigger;

    public string KindText => trigger.Kind == PuloniaTaskTriggerKind.Hotkey ? "快捷键" : "定时";

    public string Text => trigger.DisplayText;

    public string Detail => trigger.Kind == PuloniaTaskTriggerKind.Hotkey
        ? (trigger.HotkeyType == BetterGenshinImpact.Model.HotKeyTypeEnum.KeyboardMonitor ? "键鼠监听（不拦截按键）" : "全局热键（XGrabKey）")
        : $"{PuloniaTriggerEditing.ServerTimeZones.GetValueOrDefault(trigger.ServerTimeZone)} · 允许延迟 {trigger.WindowMinutes} 分钟" + (trigger.CatchUp ? " · 补跑" : "");

    public bool Enabled
    {
        get => trigger.Enabled;
        set => _ = owner.SetTriggerEnabledAsync(this, value);
    }
}

/// <summary>定时触发的编辑草稿（快捷键触发在 Linux 上只能启用/停用/删除）</summary>
public sealed partial class TaskTriggerEditor : ObservableObject
{
    private readonly PuloniaTaskTrigger _original;

    [ObservableProperty] private string _cron;
    [ObservableProperty] private int _serverTimeZoneIndex;
    [ObservableProperty] private int _busyPolicyIndex;
    [ObservableProperty] private string _windowMinutes;
    [ObservableProperty] private bool _catchUp;
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _hotkey;
    [ObservableProperty] private int _hotkeyTypeIndex;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private string _previewText = "";
    [ObservableProperty] private bool _isValid;

    public TaskTriggerEditor(PuloniaTaskTrigger trigger, bool isNew)
    {
        _original = trigger.Clone();
        IsNew = isNew;
        _cron = trigger.Cron;
        _serverTimeZoneIndex = TimeZoneKeys.IndexOf(trigger.ServerTimeZone);
        _busyPolicyIndex = PolicyKeys.IndexOf(trigger.BusyPolicy);
        _windowMinutes = trigger.WindowMinutes.ToString();
        _catchUp = trigger.CatchUp;
        _enabled = trigger.Enabled;
        _hotkey = trigger.Hotkey;
        _hotkeyTypeIndex = trigger.HotkeyType == BetterGenshinImpact.Model.HotKeyTypeEnum.KeyboardMonitor ? 1 : 0;
        Refresh();
    }

    /// <summary>快捷键触发（否则为定时触发）</summary>
    public bool IsHotkey => _original.Kind == PuloniaTaskTriggerKind.Hotkey;

    public bool IsSchedule => !IsHotkey;

    /// <summary>快捷键方式：全局热键（XGrabKey，游戏收不到该键）/ 键鼠监听（不拦截，只接受单键）</summary>
    public IReadOnlyList<string> HotkeyTypes { get; } = ["全局热键", "键鼠监听"];

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToggleRecord() => IsRecording = !IsRecording;

    /// <summary>录制中收到按键（页面隧道阶段调用）；写法与设置页快捷键相同（如 “Ctrl + Alt + F8”）</summary>
    public bool OnRecordKey(string keyName, bool ctrl, bool shift, bool alt, bool win)
    {
        if (!IsRecording) return false;
        if (keyName is "LeftCtrl" or "RightCtrl" or "LeftShift" or "RightShift" or "LeftAlt" or "RightAlt" or "LWin" or "RWin") return true;
        IsRecording = false;
        if (keyName != "Escape")
            Hotkey = (ctrl ? "Ctrl + " : "") + (shift ? "Shift + " : "") + (alt ? "Alt + " : "") + (win ? "Win + " : "") + keyName;
        return true;
    }

    private static readonly List<PuloniaTaskServerTimeZone> TimeZoneKeys = PuloniaTriggerEditing.ServerTimeZones.Keys.ToList();
    private static readonly List<PuloniaTaskBusyPolicy> PolicyKeys = PuloniaTriggerEditing.BusyPolicies.Keys.ToList();

    public bool IsNew { get; }

    public string Title => (IsNew ? "新增" : "编辑") + (IsHotkey ? "快捷键触发" : "定时触发");

    public IReadOnlyList<string> ServerTimeZones { get; } = PuloniaTriggerEditing.ServerTimeZones.Values.ToList();

    public IReadOnlyList<string> BusyPolicies { get; } = PuloniaTriggerEditing.BusyPolicies.Values.ToList();

    public string Id => _original.Id;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(PreviewText) or nameof(IsValid) or nameof(IsRecording)))
        {
            Refresh();
        }
    }

    /// <summary>按输入建立触发器并校验（与 WPF 编辑器相同的 Core 规则）</summary>
    public PuloniaTaskTrigger Build()
    {
        var result = _original.Clone();
        result.Enabled = Enabled;
        if (IsHotkey)
        {
            result.Hotkey = Hotkey.Trim();
            result.HotkeyType = HotkeyTypeIndex == 1 ? BetterGenshinImpact.Model.HotKeyTypeEnum.KeyboardMonitor : BetterGenshinImpact.Model.HotKeyTypeEnum.GlobalRegister;
            // 键位规则与 Linux 注册入口相同（WPF 编辑器保存前同样校验）
            BetterGenshinImpact.Host.Common.LinuxPuloniaTriggerHotkeys.Validate(result);
            return result;
        }

        result.ServerTimeZone = TimeZoneKeys[Math.Max(0, ServerTimeZoneIndex)];
        result.BusyPolicy = PolicyKeys[Math.Max(0, BusyPolicyIndex)];
        result.WindowMinutes = PuloniaTriggerEditing.ParseWindowMinutes(WindowMinutes);
        result.CatchUp = CatchUp;
        result.Cron = Cron.Trim();
        PuloniaTaskSchedule.Validate(result);
        return result;
    }

    private void Refresh()
    {
        try
        {
            var built = Build();
            PreviewText = IsHotkey
                ? $"按下 {built.Hotkey} 时运行本计划" + (built.HotkeyType == BetterGenshinImpact.Model.HotKeyTypeEnum.KeyboardMonitor ? "（键鼠监听，游戏在前台时生效）" : "（全局热键，任何窗口在前台都生效）")
                : PuloniaTriggerEditing.PreviewSchedule(built, DateTimeOffset.UtcNow);
            IsValid = true;
        }
        catch (Exception ex)
        {
            PreviewText = "配置无效：" + ex.Message;
            IsValid = false;
        }
    }
}
