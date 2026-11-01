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
using CommunityToolkit.Mvvm.Messaging;
using BetterGenshinImpact.ViewModel.Message;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>全自动 → JS 脚本 / 地图追踪 / 录制回放 列表中的一项</summary>
public sealed record AutoScriptItem(string Name, string Detail, string Description, string Key);

/// <summary>
/// “全自动”页（对应 WPF 导航中的“全自动”分组：调度器、JS 脚本、地图追踪、录制回放）。
/// 调度器为 ScriptControlPageViewModel；JS 脚本、地图追踪、键鼠脚本列出 User 下的文件，可以添加到调度器中选中的配置组，
/// 键鼠脚本可以直接回放（Core 的 KeyMouseMacroPlayer，在 TaskRunner 中运行），
/// 也可以录制（Core 的 KeyMouseRecordService，按键 / 按钮来自 XInput2 全局监听，相对鼠标来自 XInput2 原始移动）。脚本仓库、订阅尚未迁移
/// </summary>
public sealed partial class AutoPageViewModel : PageViewModel
{
    private readonly GameHost? _host;
    private readonly ILogger _log;
    private CancellationTokenSource? _playCts;

    [ObservableProperty] private int _selectedTab;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _isRecording;

    public AutoPageViewModel(GameHost? host, ScriptControlPageViewModel scheduler, ILogger log)
    {
        _host = host;
        _log = log;
        Scheduler = scheduler;
        WeakReferenceMessenger.Default.Register<KeyMouseRecordStateChangedMessage>(this, (_, m) => IsRecording = m.IsRecording);
        WeakReferenceMessenger.Default.Register<KeyMouseRecordSavedMessage>(this, (_, m) =>
        {
            _log.Information("键鼠录制已保存：{Path}", m.Path);
            Refresh();
        });
    }

    private KeyMouseRecordController? Recorder => _host?.Services.GetService<KeyMouseRecordController>();

    /// <summary>开始录制（与 WPF 录制回放页相同：3 秒倒计时；也可以用“启动/停止键鼠录制”快捷键）</summary>
    [RelayCommand]
    private async Task StartRecord()
    {
        if (Recorder is not { } recorder)
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, "请先在启动页，启动截图器再使用本功能");
            return;
        }

        await recorder.StartAsync();
    }

    /// <summary>停止录制并保存到键鼠脚本目录</summary>
    [RelayCommand]
    private void StopRecord() => Recorder?.Stop();

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
            // 与 WPF 相同：含子目录，按创建时间倒序，显示创建时间（Core KeyMouseScriptItem.Load）
            foreach (var item in BetterGenshinImpact.Model.KeyMouseScriptItem.Load(KmFolder))
            {
                var size = new FileInfo(item.Path).Length / 1024.0;
                KmScripts.Add(new AutoScriptItem(item.Name, $"创建时间 {item.CreateTimeStr} · {size:F1} KB", "", Path.GetRelativePath(KmFolder, item.Path)));
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

    /// <summary>自测替换“用系统程序打开”（目录或网址）</summary>
    public static Action<string>? OpenOverride { get; set; }

    /// <summary>最近一次打开的目录或网址（自测用）</summary>
    public string? LastOpened { get; private set; }

    public string JsTitle => global::BetterGenshinImpact.ViewModel.Pages.AutoScriptPageTexts.JsTitle;
    public string JsDescription => global::BetterGenshinImpact.ViewModel.Pages.AutoScriptPageTexts.JsDescription;
    public string JsTutorialText => global::BetterGenshinImpact.ViewModel.Pages.AutoScriptPageTexts.JsTutorialText;
    public string PathingTitle => global::BetterGenshinImpact.ViewModel.Pages.AutoScriptPageTexts.PathingTitle;
    public string PathingDescription => global::BetterGenshinImpact.ViewModel.Pages.AutoScriptPageTexts.PathingDescription;
    public string PathingTutorialText => global::BetterGenshinImpact.ViewModel.Pages.AutoScriptPageTexts.PathingTutorialText;
    public string KmTitle => global::BetterGenshinImpact.ViewModel.Pages.AutoScriptPageTexts.KmTitle;
    public string KmDescription => global::BetterGenshinImpact.ViewModel.Pages.AutoScriptPageTexts.KmDescription;
    public string KmTutorialText => global::BetterGenshinImpact.ViewModel.Pages.AutoScriptPageTexts.KmTutorialText;
    public string RepoUnavailable => global::BetterGenshinImpact.ViewModel.Pages.AutoScriptPageTexts.RepoUnavailable;

    private void Open(string target)
    {
        LastOpened = target;
        if (OpenOverride is { } o)
        {
            o(target);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("xdg-open") { ArgumentList = { target }, UseShellExecute = false });
        }
        catch (Exception e)
        {
            UiHost.Dialogs.Toast(DialogKind.Error, $"无法打开：{e.Message}");
        }
    }

    /// <summary>打开脚本 / 任务目录（对应 WPF “打开脚本目录”“打开任务目录”，用 xdg-open）</summary>
    [RelayCommand]
    private void OpenFolder(string? kind)
    {
        var folder = kind switch { "js" => JsFolder, "pathing" => PathingFolder, _ => KmFolder };
        Directory.CreateDirectory(folder);
        Open(folder);
    }

    /// <summary>打开教程网页（与 WPF 相同的地址）</summary>
    [RelayCommand]
    private void OpenTutorial(string? kind) => Open(kind switch
    {
        "js" => global::BetterGenshinImpact.ViewModel.Pages.AutoScriptPageTexts.JsTutorialUrl,
        "pathing" => global::BetterGenshinImpact.ViewModel.Pages.AutoScriptPageTexts.PathingTutorialUrl,
        _ => global::BetterGenshinImpact.ViewModel.Pages.AutoScriptPageTexts.KmTutorialUrl,
    });

    /// <summary>直接执行 JS 脚本（对应 WPF “执行脚本”）；脚本带配置时先提示</summary>
    [RelayCommand]
    private async Task RunJs(AutoScriptItem? item)
    {
        if (item is null) return;
        ScriptProject project;
        try
        {
            project = new ScriptProject(item.Key);
        }
        catch (Exception e)
        {
            UiHost.Dialogs.Toast(DialogKind.Error, $"无法执行：{e.Message}");
            return;
        }

        if (!string.IsNullOrEmpty(project.Manifest.SettingsUi) || !string.IsNullOrEmpty(project.Manifest.SettingsHtml))
            UiHost.Dialogs.Toast(DialogKind.Information, global::BetterGenshinImpact.ViewModel.Pages.AutoScriptPageTexts.JsHasSettingsHint);
        await Scheduler.RunProjectsAsync(project.Manifest.Name, [new ScriptGroupProject(project)]);
    }

    /// <summary>直接执行地图追踪任务（对应 WPF 右键“执行”）</summary>
    [RelayCommand]
    private async Task RunPathing(AutoScriptItem? item)
    {
        if (item is null) return;
        var relativePath = Path.GetRelativePath(PathingFolder, Path.GetDirectoryName(item.Key)!);
        await Scheduler.RunProjectsAsync(item.Name, [ScriptGroupProject.BuildPathingProject(item.Name, relativePath)]);
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
