using System.Collections.ObjectModel;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// 共享设置编辑（对照 WPF PuloniaSharedSettingsEditorViewModel）：编辑计划级设置或设置方案中的共享设置项（PuloniaSettingCatalog）。
/// 勾选“覆盖”的项写入目标，未勾选的项继承上层（方案 / 默认值）；保存前按 Core 的 ValidateSettings 校验。
/// 路线 CD 等复合值以 JSON 文本编辑。
/// </summary>
public sealed partial class TaskPlanSharedSettingsEditor : ObservableObject
{
    private readonly JObject _original;
    private readonly Func<JObject, Task<bool>> _save;
    private readonly Func<string, JToken?> _inherited;

    public TaskPlanSharedSettingsEditor(string title, JObject settings, Func<string, JToken?> inherited, Func<JObject, Task<bool>> save)
    {
        Title = title;
        _original = (JObject)settings.DeepClone();
        _inherited = inherited;
        _save = save;
        Categories = PuloniaSettingCatalog.Categories.OrderBy(c => c.Order).ToList();
        _selectedCategory = Categories.FirstOrDefault(c => c.Id == PuloniaSettingCatalog.CollectionCategory) ?? Categories[0];
        foreach (var definition in PuloniaSettingCatalog.Definitions)
            _all.Add(new TaskPlanSharedSettingRow(definition, _original[definition.Key], inherited(definition.Key)));
        Rebuild();
    }

    private readonly List<TaskPlanSharedSettingRow> _all = [];

    public string Title { get; }

    public IReadOnlyList<PuloniaSettingCategory> Categories { get; }

    [ObservableProperty] private PuloniaSettingCategory? _selectedCategory;

    [ObservableProperty] private string _message = "";

    public ObservableCollection<TaskPlanSharedSettingRow> Rows { get; } = [];

    public int OverriddenCount => _all.Count(r => r.IsOverridden);

    partial void OnSelectedCategoryChanged(PuloniaSettingCategory? value)
    {
        if (value is not null) Rebuild();
    }

    private void Rebuild()
    {
        Rows.Clear();
        foreach (var row in _all.Where(r => r.Definition.Category == SelectedCategory?.Id).OrderBy(r => r.Definition.Order)) Rows.Add(row);
    }

    public TaskPlanSharedSettingRow? Row(string key) => _all.FirstOrDefault(r => r.Definition.Key == key);

    /// <summary>合成设置：保留目录外未知键之外的全部覆盖项</summary>
    public JObject Build()
    {
        var result = new JObject();
        foreach (var row in _all.Where(r => r.IsOverridden)) result[row.Definition.Key] = row.GetValue();
        PuloniaTaskValidator.ValidateSettings(result, "settings");
        return result;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        JObject built;
        try
        {
            built = Build();
        }
        catch (Exception e) when (e is PuloniaTaskValidationException or JsonException or FormatException)
        {
            Message = "设置无效：" + e.Message;
            return;
        }

        Message = await _save(built) ? $"已保存（{OverriddenCount} 项覆盖）。" : "保存失败。";
        OnPropertyChanged(nameof(OverriddenCount));
    }
}

/// <summary>一项共享设置</summary>
public sealed partial class TaskPlanSharedSettingRow : ObservableObject
{
    public TaskPlanSharedSettingRow(PuloniaSettingDefinition definition, JToken? own, JToken? inherited)
    {
        Definition = definition;
        _isOverridden = own is not null;
        var value = own ?? inherited ?? definition.DefaultValue;
        InheritedText = Describe(inherited ?? definition.DefaultValue);
        _boolValue = value.Type == JTokenType.Boolean && value.Value<bool>();
        _numberValue = value.Type is JTokenType.Integer or JTokenType.Float ? value.Value<decimal>() : null;
        _textValue = value.Type == JTokenType.String ? value.Value<string>() ?? "" : value.ToString(Formatting.None);
        ChoiceLabels = definition.Choices.Select(c => c.Label).ToList();
        var index = definition.Choices.ToList().FindIndex(c => JToken.DeepEquals(c.Value, value));
        _choiceIndex = Math.Max(0, index);
    }

    public PuloniaSettingDefinition Definition { get; }

    public string Name => Definition.DisplayName + (Definition.Unit is { } unit ? $"（{unit}）" : "");

    public string Description => Definition.Description;

    public string InheritedText { get; }

    public IReadOnlyList<string> ChoiceLabels { get; }

    public bool IsBoolean => Definition.Editor == PuloniaSettingEditorKind.Boolean;

    public bool IsNumber => Definition.Editor is PuloniaSettingEditorKind.Integer or PuloniaSettingEditorKind.Number;

    public bool IsChoice => Definition.Editor == PuloniaSettingEditorKind.Choice && ChoiceLabels.Count > 0;

    public bool IsText => !IsBoolean && !IsNumber && !IsChoice;

    public bool IsJson => Definition.Editor == PuloniaSettingEditorKind.CollectionCooldown || Definition.DefaultValue.Type is JTokenType.Object or JTokenType.Array;

    [ObservableProperty] private bool _isOverridden;

    [ObservableProperty] private bool _boolValue;

    [ObservableProperty] private decimal? _numberValue;

    [ObservableProperty] private string _textValue;

    [ObservableProperty] private int _choiceIndex;

    // 修改值即视为覆盖
    partial void OnBoolValueChanged(bool value) => IsOverridden = true;

    partial void OnNumberValueChanged(decimal? value) => IsOverridden = true;

    partial void OnTextValueChanged(string value) => IsOverridden = true;

    partial void OnChoiceIndexChanged(int value)
    {
        if (value >= 0) IsOverridden = true;
    }

    public JToken GetValue() => Definition.Editor switch
    {
        PuloniaSettingEditorKind.Boolean => new JValue(BoolValue),
        PuloniaSettingEditorKind.Integer => new JValue((long)(NumberValue ?? 0)),
        PuloniaSettingEditorKind.Number => new JValue((double)(NumberValue ?? 0)),
        PuloniaSettingEditorKind.Choice when IsChoice => Definition.Choices[Math.Clamp(ChoiceIndex, 0, ChoiceLabels.Count - 1)].Value.DeepClone(),
        _ when IsJson => JToken.Parse(TextValue),
        _ => new JValue(TextValue),
    };

    private string Describe(JToken value) => Definition.Choices.FirstOrDefault(c => JToken.DeepEquals(c.Value, value))?.Label
                                            ?? (value.Type == JTokenType.Boolean ? (value.Value<bool>() ? "开" : "关") : value.ToString(Formatting.None));
}
