using System.Diagnostics;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.Service;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// 实时触发页“自动配音”卡片（对应 WPF QuestVoiceCardViewModel）：开关、运行状态、阶段进度、当前对白和最近事件；
/// 配音生命周期由 Core 的 <see cref="QuestVoiceService"/> 按开关与截图器状态自动管理。设置窗口（引擎、角色目录）尚未迁移
/// </summary>
public sealed partial class QuestVoiceCardViewModel : ObservableObject
{
    private readonly QuestVoiceService _service;
    private int _updateQueued;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmBackendRestartedCommand))]
    private QuestVoiceStatus _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCommandError))]
    private string _commandError = "";

    public QuestVoiceCardViewModel(AllConfig config, QuestVoiceService service)
    {
        Config = config.QuestVoiceConfig;
        _service = service;
        _status = service.Status;
        service.Changed += OnServiceChanged;
    }

    /// <summary>开关与其他设置，由 AllConfig 自动保存</summary>
    public QuestVoiceConfig Config { get; }

    public bool HasCommandError => !string.IsNullOrWhiteSpace(CommandError);

    /// <summary>阶段颜色：未开始灰、进行中蓝、完成绿、异常红（与 WPF 卡片相同的语义）</summary>
    public static FuncValueConverter<QuestVoiceStageState, IBrush> StageBrush { get; } = new(state => state switch
    {
        QuestVoiceStageState.InProgress => new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)),
        QuestVoiceStageState.Completed => new SolidColorBrush(Color.FromRgb(0x0F, 0x9D, 0x58)),
        QuestVoiceStageState.Failed => new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C)),
        _ => new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A)),
    });

    /// <summary>阶段节点填充：未开始为空心（透明），其余与边框同色</summary>
    public static FuncValueConverter<QuestVoiceStageState, IBrush> StageFill { get; } = new(state =>
        state == QuestVoiceStageState.Pending ? Brushes.Transparent : StageBrush.Convert(state, typeof(IBrush), null, System.Globalization.CultureInfo.InvariantCulture) as IBrush ?? Brushes.Transparent);

    /// <summary>合并高频状态通知，读取最新快照而不是排队时的旧快照</summary>
    private void OnServiceChanged(QuestVoiceStatus value)
    {
        if (Interlocked.Exchange(ref _updateQueued, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _updateQueued, 0);
            Status = _service.Status;
        }, DispatcherPriority.Background);
    }

    /// <summary>执行静态配置校验，结果显示在卡片中</summary>
    [RelayCommand]
    private async Task ValidateAsync(CancellationToken token)
    {
        CommandError = "";
        try { await _service.ValidateAsync(token); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { CommandError = exception.Message; }
    }

    /// <summary>用户手动重启 GPT-SoVITS 后明确确认，之后按开关自动恢复</summary>
    [RelayCommand(CanExecute = nameof(CanConfirmBackendRestarted))]
    private void ConfirmBackendRestarted()
    {
        try { _service.ConfirmBackendRestarted(); }
        catch (Exception exception) { CommandError = exception.Message; }
    }

    private bool CanConfirmBackendRestarted() => Status.BackendUnknown && !Status.IsRunning && !Status.IsBusy;

    /// <summary>用系统文件管理器打开日志目录（xdg-open）</summary>
    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            var logFolder = Global.Absolute("log");
            Directory.CreateDirectory(logFolder);
            Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { logFolder }, UseShellExecute = false });
        }
        catch (Exception exception) { CommandError = exception.Message; }
    }
}
