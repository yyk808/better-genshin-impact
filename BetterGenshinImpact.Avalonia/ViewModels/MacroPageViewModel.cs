using System.ComponentModel;
using System.Diagnostics;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.QuickClaimReward;
using BetterGenshinImpact.Host.Common;
using BetterGenshinImpact.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ILogger = Serilog.ILogger;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// “辅助操控”页（对应 WPF MacroSettingsPage）：一键宏、一键领取奖励、那维莱特转圈圈、快速强化圣遗物、一键购买、尘歌壶、确认 / 取消、
/// 长按空格 / F 连发。设置项直接编辑 MacroConfig（与 Windows 版共用 config.json）；各功能由“快捷键”页注册的快捷键触发（Core 实现）。
/// 长按连发在 Windows 由 MouseKeyMonitor 的低级键盘钩子实现，Linux 使用 Core 的 KeyHoldContinuationMonitor（全局键鼠监听，开关打开时启用）
/// </summary>
public sealed partial class MacroPageViewModel : PageViewModel, IDisposable
{
    private readonly GameHost? _host;
    private readonly Func<string, bool> _navigate;
    private readonly ILogger _log;
    private KeyHoldContinuationMonitor? _holdMonitor;

    [ObservableProperty] private bool _oneKeyClaimRewardScrollOptionEnabled;
    [ObservableProperty] private bool _oneKeyClaimRewardScrollAmountOptionEnabled;
    [ObservableProperty] private string _holdMonitorStatus = "";

    public MacroPageViewModel(GameHost? host, Func<string, bool> navigate, ILogger log)
    {
        _host = host;
        _navigate = navigate;
        _log = log;
        Config = host?.Config ?? new AllConfig();
        Config.MacroConfig.PropertyChanged += OnMacroConfigPropertyChanged;
        UpdateOneKeyClaimRewardScrollOptionEnabled();
        UpdateHoldMonitor();
    }

    public override string Title => "辅助操控";

    public AllConfig Config { get; }

    public string[] QuickFightMacroHotkeyMode { get; } = [OneKeyFightTask.HoldOnMode, OneKeyFightTask.HoldFinishMode, OneKeyFightTask.TickMode];

    public string[] OneKeyClaimRewardHotkeyMode { get; } = [OneKeyClaimRewardTask.ClickOnceMode, OneKeyClaimRewardTask.HoldMode];

    /// <summary>长按连发监听（自检用）</summary>
    public KeyHoldContinuationMonitor? HoldMonitor => _holdMonitor;

    private void OnMacroConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MacroConfig.OneKeyClaimRewardHotkeyMode) or nameof(MacroConfig.OneKeyClaimRewardScrollDownEnabled))
        {
            UpdateOneKeyClaimRewardScrollOptionEnabled();
        }

        if (e.PropertyName is nameof(MacroConfig.SpacePressHoldToContinuationEnabled) or nameof(MacroConfig.FPressHoldToContinuationEnabled))
        {
            UpdateHoldMonitor();
        }
    }

    /// <summary>与 WPF 相同：滚轮下滑仅在“按住持续”模式可用，幅度仅在滚轮下滑打开时可用</summary>
    private void UpdateOneKeyClaimRewardScrollOptionEnabled()
    {
        OneKeyClaimRewardScrollOptionEnabled = Config.MacroConfig.OneKeyClaimRewardHotkeyMode == OneKeyClaimRewardTask.HoldMode;
        OneKeyClaimRewardScrollAmountOptionEnabled = OneKeyClaimRewardScrollOptionEnabled && Config.MacroConfig.OneKeyClaimRewardScrollDownEnabled;
    }

    /// <summary>任一长按连发开关打开时启用监听，全部关闭时停止</summary>
    private void UpdateHoldMonitor()
    {
        var wanted = Config.MacroConfig.SpacePressHoldToContinuationEnabled || Config.MacroConfig.FPressHoldToContinuationEnabled;
        if (!wanted || _host is null)
        {
            _holdMonitor?.Dispose();
            _holdMonitor = null;
            HoldMonitorStatus = "";
            return;
        }

        if (_holdMonitor is not null) return;
        try
        {
            var host = _host;
            _holdMonitor = new KeyHoldContinuationMonitor(SharedGlobalInputHook.Instance, () => Config,
                () => GameSystem.IsGameForeground(host.RuntimeService.Current?.Window),
                key => InputHub.Background.Keyboard.KeyPress(key));
            HoldMonitorStatus = "长按连发已启用（全局键鼠监听，游戏在前台时生效）";
            _log.Information("辅助操控：长按连发已启用");
        }
        catch (Exception e)
        {
            HoldMonitorStatus = "长按连发不可用：" + e.Message;
            _log.Warning("辅助操控：长按连发不可用（{Message}）", e.Message);
        }
    }

    /// <summary>绑定快捷键：转到“快捷键”页</summary>
    [RelayCommand]
    private void GoToHotKeyPage() => _navigate("快捷键");

    /// <summary>一键宏说明（WPF 打开同一网址）</summary>
    [RelayCommand]
    private void GoToOneKeyMacroUrl()
    {
        try
        {
            Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { "https://www.bettergi.com/feats/macro/onem.html" }, UseShellExecute = false });
        }
        catch (Exception e)
        {
            _log.Warning("打开说明失败：{Message}", e.Message);
        }
    }

    /// <summary>编辑宏配置（WPF 为 JsonMonoDialog）：保存前校验 JSON，一键宏下次触发时按文件修改时间重新读取</summary>
    [RelayCommand]
    private async Task EditAvatarMacroAsync()
    {
        try
        {
            if (await Services.JsonEditDialog.ShowAsync(AvatarMacroJsonPath(), "宏配置"))
            {
                _log.Information("辅助操控：已保存宏配置 {Path}", AvatarMacroJsonPath());
            }
        }
        catch (Exception e)
        {
            _log.Warning("编辑宏配置失败：{Message}", e.Message);
            UiHost.Dialogs.Toast(DialogKind.Error, "编辑宏配置失败：" + e.Message);
        }
    }

    /// <summary>宏配置文件路径（User/avatar_macro.json，没有时从默认配置复制，与 WPF 相同）</summary>
    public static string AvatarMacroJsonPath() => OneKeyFightTask.GetAvatarMacroJsonPath();

    public void Dispose()
    {
        Config.MacroConfig.PropertyChanged -= OnMacroConfigPropertyChanged;
        _holdMonitor?.Dispose();
    }
}
