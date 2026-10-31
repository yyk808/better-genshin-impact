using System.Collections.ObjectModel;
using BetterGenshinImpact.Pulonia.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// 任务计划页的设置方案管理（对照 WPF PuloniaSettingsProfileManagerViewModel 与 PuloniaPlanEditingSession.Profiles）：
/// 新建、复制、重命名、删除立即写入存储（与 Windows 版共用 User/Pulonia/profiles）；仍被计划使用的方案不可删除。
/// 计划可引用一个方案作为计划级共享设置（根节点 profile_id），与 WPF 新建计划时选择方案相同。
/// </summary>
public sealed partial class TaskPlanPageViewModel
{
    /// <summary>已加载的设置方案，按名称排序</summary>
    public ObservableCollection<TaskPlanProfileItem> Profiles { get; } = [];

    [ObservableProperty] private TaskPlanProfileItem? _selectedProfile;

    [ObservableProperty] private string _profileName = "";

    /// <summary>计划可选的方案：首项为“不使用方案”</summary>
    public IReadOnlyList<TaskPlanProfileItem> PlanProfileOptions => [TaskPlanProfileItem.None, .. Profiles];

    /// <summary>当前计划引用的方案（计划根节点）</summary>
    public TaskPlanProfileItem? PlanProfile
    {
        get => SelectedPlan?.Plan.RootTask.ProfileId is { } id
            ? Profiles.FirstOrDefault(p => p.Profile?.Id == id) ?? new TaskPlanProfileItem(null, $"（缺失的方案 {id}）")
            : TaskPlanProfileItem.None;
        set
        {
            // 下拉框重建时会写回空值，忽略
            if (value is null || SelectedPlan is not { } item) return;
            var id = value.Profile?.Id;
            if (item.Plan.RootTask.ProfileId == id || value.Profile is null && value != TaskPlanProfileItem.None) return;
            item.Plan.RootTask.ProfileId = id;
            _ = SaveAndRefreshAsync(item, id is null ? "计划不再使用设置方案。" : $"计划已使用设置方案“{value.Name}”。");
        }
    }

    public string ProfileUsageText => SelectedProfile?.Profile is { } profile
        ? GetProfileUsages(profile.Id) is { Count: > 0 } usages ? "使用中：" + string.Join("；", usages) : "未被计划使用。"
        : "";

    partial void OnSelectedProfileChanged(TaskPlanProfileItem? value)
    {
        ProfileName = value?.Name ?? "";
        OnPropertyChanged(nameof(ProfileUsageText));
    }

    private async Task SaveAndRefreshAsync(TaskPlanItem item, string message)
    {
        await SaveAsync(item, message);
        // 保存后计划对象被替换为存盘版本，重建任务行（行内保存的模型链随之更新）
        RebuildNodes();
        OnPropertyChanged(nameof(PlanProfile));
        OnPropertyChanged(nameof(ProfileUsageText));
    }

    /// <summary>正在编辑的共享设置（计划级或方案）</summary>
    [ObservableProperty] private TaskPlanSharedSettingsEditor? _sharedEditor;

    /// <summary>编辑计划级共享设置（计划根节点自身的设置，优先于所用方案）</summary>
    [RelayCommand]
    private void EditPlanSettings()
    {
        if (SelectedPlan is not { } item) return;
        var root = item.Plan.RootTask;
        var probe = new PuloniaTask { Id = root.Id, Name = root.Name, TaskType = root.TaskType, ProfileId = root.ProfileId };
        var inherited = BetterGenshinImpact.Pulonia.Services.PuloniaParameterResolver.ResolveSettings(CreateSettingChain([probe])).Values;
        SharedEditor = new TaskPlanSharedSettingsEditor($"计划“{item.Name}”的共享设置", root.Settings,
            key => inherited.TryGetValue(key, out var v) ? v : null,
            async settings =>
            {
                root.Settings = settings;
                await SaveAndRefreshAsync(item, "已保存计划级共享设置。");
                await RefreshCooldownsAsync();
                return StatusMessage.StartsWith("已保存", StringComparison.Ordinal);
            });
    }

    /// <summary>
    /// 编辑选中分组或任务自身的共享设置（对照 WPF EditNodeSharedSettings）：只列出下属任务实际使用的设置项，
    /// 不使用共享设置的叶子任务不打开；节点设置优先于计划级设置与方案。
    /// </summary>
    [RelayCommand]
    private void EditNodeSettings()
    {
        if (SelectedPlan is not { } item || SelectedNode is not { } row) return;
        var keys = GetSharedSettingKeys(row.Task);
        if (keys is { Count: 0 })
        {
            StatusMessage = $"“{row.Name}”这类任务不使用共享设置，请在任务设置中修改任务参数。";
            return;
        }

        var task = row.Task;
        List<PuloniaTask> chain = [.. row.Chain.Count > 0 ? row.Chain.Take(row.Chain.Count - 1) : [item.Plan.RootTask],
            new PuloniaTask { Id = task.Id, Name = task.Name, TaskType = task.TaskType, ProfileId = task.ProfileId }];
        var inherited = BetterGenshinImpact.Pulonia.Services.PuloniaParameterResolver.ResolveSettings(CreateSettingChain(chain)).Values;
        SharedEditor = new TaskPlanSharedSettingsEditor($"{(task.TaskType == "group" ? "分组" : "任务")}“{task.Name}”的共享设置", task.Settings,
            key => inherited.TryGetValue(key, out var v) ? v : null,
            async settings =>
            {
                task.Settings = settings;
                await SaveAndRefreshAsync(item, $"已保存“{task.Name}”的共享设置。");
                return StatusMessage.StartsWith("已保存", StringComparison.Ordinal);
            }, keys);
        SelectedTab = 3;
    }

    /// <summary>节点下属任务使用的共享设置项；含计划引用时为空（全部设置项）</summary>
    internal IReadOnlySet<string>? GetSharedSettingKeys(PuloniaTask node)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var unknown = false;
        Visit(node);
        return unknown ? null : keys;

        void Visit(PuloniaTask task)
        {
            if (task.Source is { Kind: "plan" })
            {
                unknown = true;
                return;
            }

            if (task.Source is { Kind: "directory", TaskType: { } directoryType })
            {
                Add(directoryType);
                return;
            }

            if (task.TaskType == "group")
            {
                foreach (var child in task.Children) Visit(child);
                return;
            }

            Add(task.TaskType);
        }

        void Add(string taskType)
        {
            if (_tasks?.Definitions.FirstOrDefault(d => d.TaskType == taskType) is { } definition) keys.UnionWith(definition.SettingBindings.Keys);
        }
    }

    /// <summary>编辑选中设置方案的共享设置（影响使用该方案的全部计划）</summary>
    [RelayCommand]
    private void EditProfileSettings()
    {
        if (_store is null || SelectedProfile?.Profile is not { } profile) return;
        SharedEditor = new TaskPlanSharedSettingsEditor($"设置方案“{profile.Name}”", profile.Settings, _ => null,
            async settings =>
            {
                try
                {
                    var draft = Newtonsoft.Json.JsonConvert.DeserializeObject<PuloniaSettingsProfile>(Newtonsoft.Json.JsonConvert.SerializeObject(profile))!;
                    draft.Settings = settings;
                    var saved = await _store.SaveProfileAsync(draft);
                    await ReloadProfilesAsync(saved.Id);
                    RebuildNodes();
                    await RefreshCooldownsAsync();
                    StatusMessage = $"已保存设置方案“{saved.Name}”。";
                    return true;
                }
                catch (Exception e)
                {
                    StatusMessage = "保存设置方案失败：" + e.Message;
                    return false;
                }
            });
    }

    /// <summary>在当前计划末尾追加任务并保存（界面自测用）</summary>
    internal async Task AppendTaskAsync(PuloniaTask task)
    {
        if (SelectedPlan is not { } item) return;
        item.Plan.RootTask.Children.Add(task);
        await SaveAsync(item, $"已添加“{task.Name}”。");
        RebuildNodes();
    }

    /// <summary>列出使用指定方案的节点，形如“计划「每日」· 计划级设置”</summary>
    internal IReadOnlyList<string> GetProfileUsages(string profileId)
    {
        var usages = new List<string>();
        foreach (var plan in Plans)
        {
            Visit(plan.Plan.RootTask, true);

            void Visit(PuloniaTask task, bool isRoot)
            {
                if (task.ProfileId == profileId) usages.Add($"计划「{plan.Name}」· " + (isRoot ? "计划级设置" : $"「{task.Name}」"));
                foreach (var child in task.Children) Visit(child, false);
            }
        }

        return usages;
    }

    public async Task ReloadProfilesAsync(string? selectId = null)
    {
        if (_store is null) return;
        var result = await _store.ListProfilesAsync();
        selectId ??= SelectedProfile?.Profile?.Id;
        Profiles.Clear();
        foreach (var profile in result.Items.OrderBy(p => p.Name, StringComparer.CurrentCulture))
            Profiles.Add(new TaskPlanProfileItem(profile, profile.Name));
        SelectedProfile = Profiles.FirstOrDefault(p => p.Profile?.Id == selectId);
        OnPropertyChanged(nameof(PlanProfileOptions));
        OnPropertyChanged(nameof(PlanProfile));
        if (result.Failures.Count > 0) StatusMessage = $"有 {result.Failures.Count} 个设置方案文件无法读取，已跳过。";
    }

    private string NextProfileName(string baseName)
    {
        var name = baseName;
        for (var i = 2; Profiles.Any(p => p.Name == name); i++) name = $"{baseName} {i}";
        return name;
    }

    [RelayCommand]
    private Task CreateProfileAsync() => CreateProfileCoreAsync(NextProfileName(string.IsNullOrWhiteSpace(ProfileName) || Profiles.Any(p => p.Name == ProfileName.Trim()) ? "新方案" : ProfileName.Trim()), null);

    /// <summary>复制选中方案为新方案（名称加“副本”，设置一并复制）</summary>
    [RelayCommand]
    private Task DuplicateProfileAsync() => SelectedProfile?.Profile is { } source
        ? CreateProfileCoreAsync(NextProfileName(source.Name + " 副本"), source)
        : Task.CompletedTask;

    private async Task CreateProfileCoreAsync(string name, PuloniaSettingsProfile? source)
    {
        if (_store is null) return;
        try
        {
            var profile = new PuloniaSettingsProfile { Name = name, Description = source?.Description, Settings = (Newtonsoft.Json.Linq.JObject?)source?.Settings.DeepClone() ?? [] };
            var saved = await _store.SaveProfileAsync(profile);
            await ReloadProfilesAsync(saved.Id);
            StatusMessage = source is null ? $"已新建设置方案“{name}”。" : $"已复制为设置方案“{name}”。";
        }
        catch (Exception e)
        {
            StatusMessage = "保存设置方案失败：" + e.Message;
        }
    }

    [RelayCommand]
    private async Task RenameProfileAsync()
    {
        if (_store is null || SelectedProfile?.Profile is not { } profile) return;
        var name = ProfileName.Trim();
        if (name.Length == 0 || name == profile.Name) return;
        if (Profiles.Any(p => p.Name == name))
        {
            StatusMessage = $"已有名为“{name}”的设置方案。";
            return;
        }

        try
        {
            var draft = Newtonsoft.Json.JsonConvert.DeserializeObject<PuloniaSettingsProfile>(Newtonsoft.Json.JsonConvert.SerializeObject(profile))!;
            draft.Name = name;
            var saved = await _store.SaveProfileAsync(draft);
            await ReloadProfilesAsync(saved.Id);
            OnPropertyChanged(nameof(ProfileUsageText));
            StatusMessage = $"设置方案已重命名为“{name}”。";
        }
        catch (Exception e)
        {
            StatusMessage = "重命名设置方案失败：" + e.Message;
        }
    }

    /// <summary>删除方案：仍被计划使用时拒绝（与 WPF 相同，存储层再检查已保存的计划）</summary>
    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        if (_store is null || SelectedProfile?.Profile is not { } profile) return;
        var usages = GetProfileUsages(profile.Id);
        if (usages.Count > 0)
        {
            StatusMessage = "以下位置仍在使用此方案，请先改掉后再删除：" + string.Join("；", usages);
            return;
        }

        try
        {
            await _store.DeleteProfileAsync(profile.Id, profile.Revision);
            await ReloadProfilesAsync();
            StatusMessage = $"已删除设置方案“{profile.Name}”。";
        }
        catch (Exception e)
        {
            StatusMessage = "删除设置方案失败：" + e.Message;
        }
    }
}

/// <summary>设置方案选项；Profile 为空表示“不使用方案”或缺失的方案</summary>
public sealed class TaskPlanProfileItem(PuloniaSettingsProfile? profile, string name)
{
    public static TaskPlanProfileItem None { get; } = new(null, "（不使用方案）");

    public PuloniaSettingsProfile? Profile => profile;

    public string Name => name;

    public string Summary => profile is null ? "" : profile.Settings.Count == 0 ? "未设置任何共享设置" : $"{profile.Settings.Count} 项共享设置";

    public override string ToString() => name;
}
