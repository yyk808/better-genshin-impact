using System.Collections.ObjectModel;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.Host.Common;
using BetterGenshinImpact.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ILogger = Serilog.ILogger;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>全自动 → JS 脚本 / 地图追踪 / 录制回放 列表中的一项</summary>
public sealed record AutoScriptItem(string Name, string Detail, string Description, string Key);

/// <summary>
/// “全自动”页（对应 WPF 导航中的“全自动”分组：调度器、JS 脚本、地图追踪、录制回放）。
/// 调度器为 ScriptControlPageViewModel；JS 脚本、地图追踪、键鼠脚本列出 User 下的文件，可以添加到调度器中选中的配置组，
/// 键鼠脚本可以直接回放（Core 的 KeyMouseMacroPlayer，在 TaskRunner 中运行）。脚本仓库、订阅、录制尚未迁移
/// </summary>
public sealed partial class AutoPageViewModel : PageViewModel
{
    private readonly GameHost? _host;
    private readonly ILogger _log;
    private CancellationTokenSource? _playCts;

    [ObservableProperty] private int _selectedTab;
    [ObservableProperty] private bool _isPlaying;

    public AutoPageViewModel(GameHost? host, ScriptControlPageViewModel scheduler, ILogger log)
    {
        _host = host;
        _log = log;
        Scheduler = scheduler;
    }

    public override string Title => "全自动";

    /// <summary>调度器（配置组）</summary>
    public ScriptControlPageViewModel Scheduler { get; }

    public ObservableCollection<AutoScriptItem> JsScripts { get; } = [];

    public ObservableCollection<AutoScriptItem> PathingScripts { get; } = [];

    public ObservableCollection<AutoScriptItem> KmScripts { get; } = [];

    public string JsFolder => Global.ScriptPath();

    public string PathingFolder => PathingPaths.PathJsonPath;

    public string KmFolder => Global.Absolute(@"User\KeyMouseScript");

    public override void OnNavigatedTo()
    {
        Scheduler.OnNavigatedTo();
        Refresh();
    }

    [RelayCommand]
    public void Refresh()
    {
        JsScripts.Clear();
        if (Directory.Exists(JsFolder))
        {
            foreach (var dir in Directory.GetDirectories(JsFolder).Order(StringComparer.Ordinal))
            {
                var folder = Path.GetFileName(dir);
                try
                {
                    var p = new ScriptProject(folder);
                    JsScripts.Add(new AutoScriptItem(p.Manifest.Name, $"{folder} · v{p.Manifest.Version}", p.Manifest.Description, folder));
                }
                catch (Exception e)
                {
                    JsScripts.Add(new AutoScriptItem(folder, "无法读取 manifest.json", e.Message, folder));
                }
            }
        }

        PathingScripts.Clear();
        if (Directory.Exists(PathingFolder))
        {
            foreach (var file in Directory.EnumerateFiles(PathingFolder, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var rel = Path.GetRelativePath(PathingFolder, Path.GetDirectoryName(file)!);
                PathingScripts.Add(new AutoScriptItem(Path.GetFileName(file), rel == "." ? "" : rel, "", file));
            }
        }

        KmScripts.Clear();
        if (Directory.Exists(KmFolder))
        {
            foreach (var file in Directory.GetFiles(KmFolder, "*.json").Order(StringComparer.Ordinal))
            {
                var info = new FileInfo(file);
                KmScripts.Add(new AutoScriptItem(info.Name, $"{info.LastWriteTime:yyyy-MM-dd HH:mm} · {info.Length / 1024.0:F1} KB", "", info.Name));
            }
        }
    }

    private ScriptGroup? TargetGroup()
    {
        if (Scheduler.SelectedScriptGroup is { } g)
        {
            return g;
        }

        UiHost.Dialogs.Toast(DialogKind.Warning, "请先在“调度器”中选择配置组");
        return null;
    }

    [RelayCommand]
    private void AddJs(AutoScriptItem? item)
    {
        if (item is null || TargetGroup() is not { } g)
        {
            return;
        }

        try
        {
            g.AddProject(new ScriptGroupProject(new ScriptProject(item.Key)));
            UiHost.Dialogs.Toast(DialogKind.Information, $"已添加到配置组 {g.Name}");
        }
        catch (Exception e)
        {
            UiHost.Dialogs.Toast(DialogKind.Error, $"添加失败：{e.Message}");
        }
    }

    [RelayCommand]
    private void AddPathing(AutoScriptItem? item)
    {
        if (item is null || TargetGroup() is not { } g)
        {
            return;
        }

        // 与 WPF 相同：文件名 + 相对 User/AutoPathing 的目录
        var relativePath = Path.GetRelativePath(PathingFolder, Path.GetDirectoryName(item.Key)!);
        g.AddProject(ScriptGroupProject.BuildPathingProject(item.Name, relativePath));
        UiHost.Dialogs.Toast(DialogKind.Information, $"已添加到配置组 {g.Name}");
    }

    [RelayCommand]
    private void AddKm(AutoScriptItem? item)
    {
        if (item is null || TargetGroup() is not { } g)
        {
            return;
        }

        g.AddProject(ScriptGroupProject.BuildKeyMouseProject(item.Key));
        UiHost.Dialogs.Toast(DialogKind.Information, $"已添加到配置组 {g.Name}");
    }

    /// <summary>回放键鼠脚本（与 WPF 录制回放页相同：3 秒倒计时后回放；再点一次停止）</summary>
    [RelayCommand]
    private async Task PlayKm(AutoScriptItem? item)
    {
        if (IsPlaying)
        {
            _playCts?.Cancel();
            return;
        }

        if (item is null || _host is null)
        {
            return;
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(Path.Combine(KmFolder, item.Key));
        }
        catch (Exception e)
        {
            UiHost.Dialogs.Toast(DialogKind.Error, $"读取键鼠脚本失败：{e.Message}");
            return;
        }

        using var cts = new CancellationTokenSource();
        _playCts = cts;
        IsPlaying = true;
        try
        {
            await Task.Run(() => new TaskRunner().RunCurrentWithResultAsync(
                (runtime, ct) => KeyMouseMacroPlayer.PlayMacro(runtime, json, ct), requestToken: cts.Token, taskName: $"回放 {item.Name}"));
        }
        catch (Exception e)
        {
            _log.Error(e, "回放键鼠脚本失败");
        }
        finally
        {
            _playCts = null;
            IsPlaying = false;
        }
    }
}
