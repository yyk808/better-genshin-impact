using System.Diagnostics;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Service.AccountRecords;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// 狛荷屋“自定义提醒规则…”与每分钟刷新（同 WPF EditWarningRules、RefreshTimeWarnings 与 AccountRecordsTimeRefreshBehavior）：
/// 首次打开时由 Core 生成全部禁用的示例规则文件，用系统默认程序打开（xdg-open）；保存后刷新或等待一分钟生效，规则无效时在底部提示
/// </summary>
public partial class AccountRecordsPageViewModel
{
    /// <summary>打开规则文件（自测替换，避免启动外部程序）</summary>
    public Action<string> OpenRulesFile { get; set; } = path =>
        Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { path }, UseShellExecute = false })?.Dispose();

    [RelayCommand]
    private async Task EditWarningRules()
    {
        try
        {
            await AccountRecordsInteractionGate.Instance.ExecuteManualAsync(async () => OpenRulesFile(await _store.GetWarningRulesPathAsync()));
        }
        catch (Exception ex) { await InfoAsync(AccountRecordsErrorMessages.Describe(ex), "打开 Warning 规则", DialogKind.Error); }
    }

    /// <summary>分钟刷新剩余天数及阈值事件后重读当前 UID（Linux 版的行是只读快照，刷新即重建）</summary>
    [RelayCommand]
    private async Task RefreshTimeWarnings()
    {
        if (SelectedUid is not { } uid) return;
        try
        {
            await _store.MaintainAsync(uid);
            if (SelectedUid == uid) await SelectUidAsync(uid);
        }
        catch (Exception ex) { StatusText = AccountRecordsErrorMessages.Describe(ex); }
    }
}
