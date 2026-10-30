using System.Collections.ObjectModel;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>任务设置面板：名称、启用、分组执行次数与按 Schema 生成的参数字段（规则在 Core 的 PuloniaParameterForm）</summary>
public sealed partial class TaskNodeEditor : ObservableObject
{
    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _repeatText;

    public TaskNodeEditor(TaskPlanNodeRow row, PuloniaTaskDefinition? definition)
    {
        Row = row;
        Definition = definition;
        _name = row.Task.Name;
        _isEnabled = row.Task.IsEnabled;
        _repeatText = (row.Task.RepeatCount ?? 1).ToString();
        IsGroup = row.Task.TaskType == "group" && row.Task.Source is null;
        if (definition is not null)
        {
            foreach (var field in PuloniaParameterForm.Describe(definition, row.Task))
            {
                Fields.Add(new TaskParameterFieldEditor(field));
            }
        }
    }

    public TaskPlanNodeRow Row { get; }

    public PuloniaTaskDefinition? Definition { get; }

    public bool IsGroup { get; }

    public string TypeText => Row.TypeText;

    public ObservableCollection<TaskParameterFieldEditor> Fields { get; } = [];

    public bool HasFields => Fields.Count > 0;

    public string FieldsHint => Definition is null ? "未注册的任务类型，只能修改名称与启用状态。" : HasFields ? "" : "此任务没有可编辑的参数。";

    /// <summary>收集修改过的参数</summary>
    public JObject CollectChanges()
    {
        var changes = new JObject();
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

    public string KindText => trigger.Kind == PuloniaTaskTriggerKind.Hotkey ? "快捷键（Linux 尚未注册计划快捷键）" : "定时";

    public string Text => trigger.DisplayText;

    public string Detail => trigger.Kind == PuloniaTaskTriggerKind.Hotkey
        ? ""
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
        Refresh();
    }

    private static readonly List<PuloniaTaskServerTimeZone> TimeZoneKeys = PuloniaTriggerEditing.ServerTimeZones.Keys.ToList();
    private static readonly List<PuloniaTaskBusyPolicy> PolicyKeys = PuloniaTriggerEditing.BusyPolicies.Keys.ToList();

    public bool IsNew { get; }

    public string Title => IsNew ? "新增定时触发" : "编辑定时触发";

    public IReadOnlyList<string> ServerTimeZones { get; } = PuloniaTriggerEditing.ServerTimeZones.Values.ToList();

    public IReadOnlyList<string> BusyPolicies { get; } = PuloniaTriggerEditing.BusyPolicies.Values.ToList();

    public string Id => _original.Id;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(PreviewText) or nameof(IsValid)))
        {
            Refresh();
        }
    }

    /// <summary>按输入建立触发器并校验（与 WPF 编辑器相同的 Core 规则）</summary>
    public PuloniaTaskTrigger Build()
    {
        var result = _original.Clone();
        result.Enabled = Enabled;
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
            PreviewText = PuloniaTriggerEditing.PreviewSchedule(Build(), DateTimeOffset.UtcNow);
            IsValid = true;
        }
        catch (Exception ex)
        {
            PreviewText = "配置无效：" + ex.Message;
            IsValid = false;
        }
    }
}
