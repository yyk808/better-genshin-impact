using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Dynamic;
using Avalonia.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.SoloTask.Execution;
using BetterGenshinImpact.Host.Common;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// “任务计划”页（对应 WPF 的 ScriptControlPage 的主体）：User/ScriptGroup 下的配置组列表、组内项目、
/// “从此执行”标记、运行 / 连续执行 / 停止。配置组文件读写与调度走 Core 的 ScriptGroupStore / ScriptGroupSchedule / ScriptGroupRunner，
/// 项目执行走 Core 的 IScriptService（与 WPF 相同），任务在 TaskRunner 中运行（任务信号量、触发器暂停、结束时释放所有按键）。
/// 项目通用设置、JS 自定义设置（settings.json 表单）与配置组设置在页面内的设置面板中编辑（字段与取值规则来自 Core），
/// 关闭面板时保存配置组文件。地图追踪的选择面板、JS 的 HTML 设置页与拖拽排序尚未迁移
/// </summary>
public enum ScriptSettingsKind
{
    GroupConfig,
    Project,
    JsSettings,
}

/// <summary>当前打开的设置面板</summary>
public sealed record ScriptSettingsEditor(ScriptSettingsKind Kind, string Title, ScriptGroup Group, ScriptGroupProject? Project, List<SettingItem>? Items);

public sealed partial class ScriptControlPageViewModel : PageViewModel
{
    private readonly GameHost? _host;
    private readonly ILogger _log;
    private CancellationTokenSource? _cts;
    private bool _loading;

    [ObservableProperty] private ScriptGroup? _selectedScriptGroup;
    [ObservableProperty] private ScriptGroupProject? _selectedProject;
    [ObservableProperty] private string _newGroupName = string.Empty;
    [ObservableProperty] private string? _selectedKmScript;
    [ObservableProperty] private string _newShellCommand = string.Empty;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _statusText = "空闲";
    [ObservableProperty] private string? _selectedJsScript;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsEditorOpen))] private ScriptSettingsEditor? _editor;

    public ScriptControlPageViewModel(GameHost? host, ILogger log)
    {
        _host = host;
        _log = log;
        Config = host?.Config ?? new AllConfig();
        IsAvailable = host is not null;
        ScriptGroups.CollectionChanged += OnScriptGroupsChanged;
    }

    public override string Title => "任务计划";

    public AllConfig Config { get; }

    public bool IsAvailable { get; }

    public ObservableCollection<ScriptGroup> ScriptGroups { get; } = [];

    /// <summary>User/KeyMouseScript 下的键鼠脚本文件名</summary>
    public ObservableCollection<string> KmScripts { get; } = [];

    /// <summary>User/JsScript 下的 JS 脚本文件夹</summary>
    public ObservableCollection<string> JsScripts { get; } = [];

    public bool IsEditorOpen => Editor is not null;

    public bool GlobalJsNotificationEnabled => Config.NotificationConfig.JsNotificationEnabled;

    public bool HasSelectedGroup => SelectedScriptGroup is not null;

    public string SelectedGroupTitle => SelectedScriptGroup is { } g ? $"配置组 - {g.Name}" : "请在左侧选择或新增配置组";

    public override void OnNavigatedTo()
    {
        if (ScriptGroups.Count == 0)
        {
            Reload();
        }
    }

    /// <summary>重新读取配置组与键鼠脚本列表</summary>
    [RelayCommand]
    public void Reload()
    {
        var selected = SelectedScriptGroup?.Name;
        _loading = true;
        try
        {
            foreach (var g in ScriptGroups)
            {
                Unhook(g);
            }

            ScriptGroups.Clear();
            List<ScriptGroup> groups;
            try
            {
                groups = ScriptGroupStore.ReadAll(Config, (file, e) =>
                {
                    _log.Warning("读取配置组 {File} 失败：{Message}", file, e.Message);
                    UiHost.Dialogs.Toast(DialogKind.Error, "读取配置组配置失败:" + e.Message);
                });
            }
            catch (Exception e)
            {
                _log.Warning(e, "读取配置组配置时失败");
                UiHost.Dialogs.Toast(DialogKind.Error, "读取配置组配置失败！");
                groups = [];
            }

            foreach (var g in groups)
            {
                Hook(g);
                ScriptGroups.Add(g);
            }
        }
        finally
        {
            _loading = false;
        }

        KmScripts.Clear();
        var kmFolder = Global.Absolute(@"User\KeyMouseScript");
        if (Directory.Exists(kmFolder))
        {
            foreach (var f in Directory.GetFiles(kmFolder, "*.json").Select(Path.GetFileName).Order(StringComparer.Ordinal))
            {
                KmScripts.Add(f!);
            }
        }

        SelectedKmScript ??= KmScripts.FirstOrDefault();

        JsScripts.Clear();
        var jsFolder = Global.ScriptPath();
        if (Directory.Exists(jsFolder))
        {
            foreach (var d in Directory.GetDirectories(jsFolder).Where(d => File.Exists(Path.Combine(d, "manifest.json")))
                         .Select(Path.GetFileName).Order(StringComparer.Ordinal))
            {
                JsScripts.Add(d!);
            }
        }

        SelectedJsScript ??= JsScripts.FirstOrDefault();
        SelectedScriptGroup = ScriptGroups.FirstOrDefault(g => g.Name == selected) ?? ScriptGroups.FirstOrDefault();
    }

    partial void OnSelectedScriptGroupChanged(ScriptGroup? value)
    {
        OnPropertyChanged(nameof(HasSelectedGroup));
        OnPropertyChanged(nameof(SelectedGroupTitle));
    }

    // —— 配置组 ——

    [RelayCommand]
    private void AddScriptGroup()
    {
        var name = NewGroupName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, "请输入配置组名称");
            return;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('\\'))
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, "配置组名称不能包含文件名中不允许的字符");
            return;
        }

        if (ScriptGroups.Any(x => x.Name == name))
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, $"配置组 {name} 已经存在，请勿重复添加");
            return;
        }

        var group = new ScriptGroup { Name = name };
        Hook(group);
        ScriptGroups.Add(group);
        SelectedScriptGroup = group;
        NewGroupName = string.Empty;
    }

    [RelayCommand]
    private void DeleteScriptGroup(ScriptGroup? item)
    {
        item ??= SelectedScriptGroup;
        if (item is null)
        {
            return;
        }

        try
        {
            Unhook(item);
            ScriptGroups.Remove(item);
            ScriptGroupStore.Delete(item.Name);
            UiHost.Dialogs.Toast(DialogKind.Information, $"配置组 {item.Name} 已经被删除");
        }
        catch (Exception e)
        {
            _log.Warning(e, "删除配置组配置时失败");
            UiHost.Dialogs.Toast(DialogKind.Error, $"配置组 {item.Name} 删除失败！");
        }
    }

    /// <summary>连续执行时从此组开始（与 WPF 右键菜单“连续任务从此开始执行”相同，再点一次取消）</summary>
    [RelayCommand]
    private void SetGroupNext(ScriptGroup? item)
    {
        item ??= SelectedScriptGroup;
        ScriptGroupSchedule.SetGroupNextFlag(ScriptGroups, item is { NextFlag: true } ? null : item, Config);
        if (item is { NextFlag: false } && Config.NextScriptGroupName == item.Name)
        {
            Config.NextScriptGroupName = string.Empty;
        }
    }

    [RelayCommand]
    private void MoveGroup(string direction)
    {
        if (SelectedScriptGroup is not { } g)
        {
            return;
        }

        var i = ScriptGroups.IndexOf(g);
        var j = direction == "up" ? i - 1 : i + 1;
        if (j < 0 || j >= ScriptGroups.Count)
        {
            return;
        }

        ScriptGroups.Move(i, j);
        SelectedScriptGroup = g;
    }

    // —— 组内项目 ——

    [RelayCommand]
    private void AddKmScript()
    {
        if (SelectedScriptGroup is null || string.IsNullOrEmpty(SelectedKmScript))
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, "请先选择配置组与键鼠脚本（User/KeyMouseScript）");
            return;
        }

        SelectedScriptGroup.AddProject(ScriptGroupProject.BuildKeyMouseProject(SelectedKmScript));
    }

    [RelayCommand]
    private void AddJsScript()
    {
        if (SelectedScriptGroup is null || string.IsNullOrEmpty(SelectedJsScript))
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, "请先选择配置组与 JS 脚本（User/JsScript）");
            return;
        }

        try
        {
            SelectedScriptGroup.AddProject(new ScriptGroupProject(new ScriptProject(SelectedJsScript)));
        }
        catch (Exception e)
        {
            _log.Warning(e, "加载 JS 脚本 {Folder} 失败", SelectedJsScript);
            UiHost.Dialogs.Toast(DialogKind.Error, $"加载 JS 脚本失败：{e.Message}");
        }
    }

    [RelayCommand]
    private void AddShell()
    {
        if (SelectedScriptGroup is null || string.IsNullOrWhiteSpace(NewShellCommand))
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, "请先选择配置组并输入命令");
            return;
        }

        SelectedScriptGroup.AddProject(ScriptGroupProject.BuildShellProject(NewShellCommand.Trim()));
        NewShellCommand = string.Empty;
    }

    [RelayCommand]
    private void DeleteProject(ScriptGroupProject? item)
    {
        if (item is not null)
        {
            SelectedScriptGroup?.Projects.Remove(item);
        }
    }

    [RelayCommand]
    private void ToggleProjectEnabled(ScriptGroupProject? item)
    {
        if (item is not null)
        {
            item.Status = item.Status == "Enabled" ? "Disabled" : "Enabled";
        }
    }

    /// <summary>下一次任务从此处执行（与 WPF 右键菜单相同，写入配置根的 NextScheduledTask）</summary>
    [RelayCommand]
    private void SetProjectNext(ScriptGroupProject? item)
    {
        if (item is null || SelectedScriptGroup is null)
        {
            return;
        }

        ScriptGroupSchedule.SetProjectNextFlag(SelectedScriptGroup, item, Config);
        _host?.Services.GetService<IConfigService>()?.Flush();
    }

    [RelayCommand]
    private void ReverseProjects()
    {
        if (SelectedScriptGroup is not { } g)
        {
            return;
        }

        var projects = g.Projects.Reverse().ToList();
        g.Projects.Clear();
        projects.ForEach(p => g.Projects.Add(p));
    }

    [RelayCommand]
    private void ClearProjects() => SelectedScriptGroup?.Projects.Clear();

    // —— 设置面板 ——

    /// <summary>配置组设置（对应 WPF ScriptGroupConfigView）</summary>
    [RelayCommand]
    public void OpenGroupConfig()
    {
        if (SelectedScriptGroup is { } g)
        {
            Editor = new ScriptSettingsEditor(ScriptSettingsKind.GroupConfig, $"配置组设置 - {g.Name}", g, null, null);
        }
    }

    /// <summary>项目通用设置（对应 WPF ScriptGroupProjectEditor）</summary>
    [RelayCommand]
    public void OpenProjectSettings(ScriptGroupProject? item)
    {
        if (item is not null && SelectedScriptGroup is { } g)
        {
            Editor = new ScriptSettingsEditor(ScriptSettingsKind.Project, $"通用设置 - {item.Name}", g, item, null);
        }
    }

    /// <summary>JS 脚本自定义设置（settings.json 描述的表单，与 WPF 基础设置表单相同）</summary>
    [RelayCommand]
    public void OpenJsSettings(ScriptGroupProject? item)
    {
        if (item is null || SelectedScriptGroup is not { } g)
        {
            return;
        }

        if (item.Type != "Javascript")
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, "只有JS脚本才有自定义配置");
            return;
        }

        if (item.Project == null)
        {
            item.BuildScriptProjectRelation();
        }

        if (item.Project == null)
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, "脚本项目未加载");
            return;
        }

        var manifest = item.Project.Manifest;
        if (string.IsNullOrWhiteSpace(manifest.SettingsUi))
        {
            UiHost.Dialogs.Toast(DialogKind.Warning,
                string.IsNullOrWhiteSpace(manifest.SettingsHtml) ? "此脚本未提供自定义配置" : "此脚本只提供 HTML 设置页，Linux 版暂不支持");
            return;
        }

        List<SettingItem> items;
        try
        {
            items = manifest.LoadSettingItems(item.Project.ProjectPath);
        }
        catch (Exception e)
        {
            _log.Warning(e, "读取脚本 {Name} 的设置项失败", item.Name);
            UiHost.Dialogs.Toast(DialogKind.Error, "读取脚本设置项失败：" + e.Message);
            return;
        }

        if (items.Count == 0)
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, "此脚本未提供自定义配置");
            return;
        }

        item.JsScriptSettingsObject ??= new ExpandoObject();
        Editor = new ScriptSettingsEditor(ScriptSettingsKind.JsSettings, $"修改JS脚本自定义设置 - {item.Name}", g, item, items);
    }

    /// <summary>关闭设置面板并保存所属配置组（JS 设置对象不触发属性通知，与 WPF 相同需要手动保存）</summary>
    [RelayCommand]
    public void CloseEditor()
    {
        if (Editor is { } e)
        {
            Write(e.Group);
            _log.Information("已保存配置组 {Name} 的{Kind}", e.Group.Name, e.Kind switch
            {
                ScriptSettingsKind.GroupConfig => "配置组设置",
                ScriptSettingsKind.Project => "项目通用设置",
                _ => "JS 自定义设置",
            });
        }

        Editor = null;
    }

    // —— 运行 ——

    /// <summary>运行选中的配置组（与 WPF “运行”按钮相同：RunnerContext.Reset 后在 TaskRunner 中执行 IScriptService.RunMulti）</summary>
    [RelayCommand]
    public Task StartScriptGroup()
    {
        if (SelectedScriptGroup is not { } group)
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, "请先选择一个配置组");
            return Task.CompletedTask;
        }

        return RunAsync(group.Name, (service, runtime, ct) =>
        {
            RunnerContext.Instance.Reset();
            return service.RunMulti(runtime, ScriptGroupSchedule.GetNextProjects(group, Config), group.Name, ct);
        });
    }

    /// <summary>连续执行全部配置组（WPF 中先在对话框里勾选；这里执行列表中的全部组，从“从此组开始”的标记处开始）</summary>
    [RelayCommand]
    public Task StartAllGroups()
    {
        var groups = ScriptGroups.ToList();
        if (groups.Count == 0)
        {
            return Task.CompletedTask;
        }

        _log.Information("开始连续执行选中配置组:{Names}", string.Join(",", groups.Select(x => x.Name)));
        return RunAsync("连续执行", async (service, runtime, ct) =>
        {
            RunnerContext.Instance.IsContinuousRunGroup = true;
            try
            {
                await ScriptGroupRunner.RunGroupsAsync(service, runtime, groups, false, Config, ct);
            }
            finally
            {
                RunnerContext.Instance.Reset();
            }
        });
    }

    [RelayCommand]
    public void Stop()
    {
        if (_cts is { IsCancellationRequested: false } cts)
        {
            StatusText = "正在停止";
            cts.Cancel();
        }
    }

    private async Task RunAsync(string name, Func<IScriptService, GameTask.Runtime.GameRuntime, CancellationToken, Task> action)
    {
        if (_host is null)
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, "平台服务未初始化，无法运行配置组");
            return;
        }

        if (IsRunning)
        {
            Stop();
            return;
        }

        var service = _host.Services.GetRequiredService<IScriptService>();
        _host.Services.GetService<IConfigService>()?.Flush();
        using var cts = new CancellationTokenSource();
        _cts = cts;
        IsRunning = true;
        StatusText = $"运行中：{name}";
        SoloTaskRunResult? result = null;
        try
        {
            result = await Task.Run(() => new TaskRunner().RunCurrentWithResultAsync((runtime, ct) => action(service, runtime, ct),
                requestToken: cts.Token, taskName: name));
        }
        catch (Exception e)
        {
            _log.Error(e, "配置组 {Name} 执行异常", name);
        }
        finally
        {
            _cts = null;
            IsRunning = false;
            StatusText = result is null ? "空闲" : $"空闲（上次：{name} {result.Outcome}）";
            // 执行过程中 RunMulti 会更新项目状态与“从此执行”标记
            _host.Services.GetService<IConfigService>()?.Flush();
            Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(ScriptGroups)));
        }
    }

    // —— 自动保存（与 WPF 相同：组列表或组内项目变化时补 Index 并写文件）——

    private void Hook(ScriptGroup g)
    {
        g.Projects.CollectionChanged += OnProjectsChanged;
        foreach (var p in g.Projects)
        {
            p.PropertyChanged += OnProjectPropertyChanged;
        }
    }

    private void Unhook(ScriptGroup g)
    {
        g.Projects.CollectionChanged -= OnProjectsChanged;
        foreach (var p in g.Projects)
        {
            p.PropertyChanged -= OnProjectPropertyChanged;
        }
    }

    private void OnScriptGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var i = 1;
        foreach (var group in ScriptGroups)
        {
            group.Index = i++;
        }

        foreach (var group in ScriptGroups)
        {
            Write(group);
        }
    }

    private void OnProjectsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (ScriptGroupProject p in e.NewItems)
            {
                p.PropertyChanged += OnProjectPropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (ScriptGroupProject p in e.OldItems)
            {
                p.PropertyChanged -= OnProjectPropertyChanged;
            }
        }

        var group = ScriptGroups.FirstOrDefault(g => ReferenceEquals(g.Projects, sender));
        if (group is null)
        {
            return;
        }

        var i = 1;
        foreach (var project in group.Projects)
        {
            project.Index = i++;
        }

        Write(group);
    }

    private void OnProjectPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is ScriptGroupProject { GroupInfo: { } group } && ScriptGroups.Contains(group))
        {
            Write(group);
        }
    }

    private void Write(ScriptGroup group)
    {
        try
        {
            ScriptGroupStore.Save(group);
        }
        catch (Exception e)
        {
            _log.Warning(e, "保存配置组配置时失败");
            UiHost.Dialogs.Toast(DialogKind.Error, $"{group.Name} 保存失败！");
        }
    }
}
