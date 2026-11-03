using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// 狛荷屋库存行右键其余项与显示偏好（同 WPF AccountRecordsInventoryViewModel / AccountRecordsPageViewModel）：
/// 置顶 / 取消置顶、归档同类未受阻的完成事件、查看异常、显示过期时间、删除此物品记录；
/// “更多”菜单的“优先显示来源名称”“恢复所有分页默认栏宽”“无图像时显示行号”。偏好写入公共设置，与 Windows 版共用
/// </summary>
public partial class AccountRecordsPageViewModel
{
    /// <summary>来源列优先显示来源名称（默认开）</summary>
    [ObservableProperty] private bool _preferReadableSource = true;
    /// <summary>显示“到期”列（同 WPF 默认隐藏）</summary>
    [ObservableProperty] private bool _showExpirationColumn;
    /// <summary>无图像时显示行号；Linux 版暂不显示物品图像，开启后每行都显示行号（置顶行“1”，其余“1.”，同 WPF）</summary>
    [ObservableProperty] private bool _showMissingIconRowNumbers;
    /// <summary>页面共享的栏宽恢复信号，视图收到后恢复初始栏宽（同 WPF ColumnWidthResetVersion）</summary>
    [ObservableProperty] private int _columnWidthResetVersion;

    /// <summary>从公共设置读取显示偏好</summary>
    private void ApplyDisplaySettings(AccountRecordsSettings settings)
    {
        PreferReadableSource = settings.PreferReadableSource;
        ShowExpirationColumn = settings.ShowExpirationColumn;
        ShowMissingIconRowNumbers = settings.ShowMissingIconRowNumbers;
    }

    /// <summary>修改前读取最新公共设置，避免旧页面快照覆盖其他设置；保存后刷新当前 UID（同 WPF RunDisplayAsync）</summary>
    private async Task UpdateDisplayAsync(Action<AccountRecordsSettings> update, bool reload)
    {
        var uid = SelectedUid;
        try
        {
            ApplyDisplaySettings(await _store.UpdateSettingsAsync(update));
            if (reload && uid != null && SelectedUid == uid) await SelectUidAsync(uid);
        }
        catch (Exception ex) { await InfoAsync(AccountRecordsErrorMessages.Describe(ex), "狛荷屋记录", DialogKind.Error); }
    }

    [RelayCommand]
    private Task ToggleSourceNames() => UpdateDisplayAsync(s => s.PreferReadableSource = !s.PreferReadableSource, true);

    [RelayCommand]
    private Task ToggleExpirationColumn() => UpdateDisplayAsync(s => s.ShowExpirationColumn = !s.ShowExpirationColumn, false);

    [RelayCommand]
    private Task ToggleMissingIconRowNumbers() => UpdateDisplayAsync(s => s.ShowMissingIconRowNumbers = !s.ShowMissingIconRowNumbers, false);

    /// <summary>恢复初始栏宽，不改变排序和内容</summary>
    [RelayCommand]
    private void ResetColumnWidths() => ColumnWidthResetVersion++;

    /// <summary>切换置顶，重载后保持现有排序与筛选</summary>
    [RelayCommand]
    private async Task TogglePrior(InventoryRowSnapshot? row)
    {
        if (SelectedUid is not { } uid || row == null) return;
        try
        {
            await AccountRecordsInteractionGate.Instance.ExecuteManualAsync(() => _store.SetPriorAsync(uid, row.Name, !row.Prior));
            if (SelectedUid == uid) await SelectUidAsync(uid);
        }
        catch (Exception ex) { await InfoAsync(AccountRecordsErrorMessages.Describe(ex), "狛荷屋记录", DialogKind.Error); }
    }

    /// <summary>以右键物品的事件类型为准，归档当前账号内未受阻的同类完成事件（至少两条，同 WPF）</summary>
    [RelayCommand]
    private async Task ArchiveRowCompletedEvents(InventoryRowSnapshot? row)
    {
        if (SelectedUid is not { } uid || row is not { CanBatchArchive: true, Event: { } active }) return;
        try
        {
            var inventory = (await _store.ReadAsync(uid)).Inventory;
            var ids = InventoryEventPolicy.GetCompletedArchiveCandidates(inventory, active.Kind).Select(item => item.Id).ToArray();
            if (ids.Length < 2) return;
            await AccountRecordsInteractionGate.Instance.ExecuteManualAsync(() => _store.ArchiveCompletedEventsAsync(uid, active.Kind, ids));
            if (SelectedUid == uid) await SelectUidAsync(uid);
        }
        catch (Exception ex) { await InfoAsync(AccountRecordsErrorMessages.Describe(ex), "狛荷屋记录", DialogKind.Error); }
    }

    /// <summary>在底部提示行显示该物品的异常 / 事件说明（WPF 为底部提示卡片）</summary>
    [RelayCommand]
    private void ShowStatus(InventoryRowSnapshot? row)
    {
        if (row == null || !Rows.Contains(row)) return;
        StatusText = $"{row.Name}：{row.StatusText}";
    }

    /// <summary>删除单项及其快照副本，防止日后恢复时重新出现（同 WPF）</summary>
    [RelayCommand]
    private async Task DeleteItem(InventoryRowSnapshot? row)
    {
        if (SelectedUid is not { } uid || row == null) return;
        if (!await ConfirmAsync($"删除“{row.Name}”的库存、异常和快照中的记录？", "删除库存记录")) return;
        try
        {
            await AccountRecordsInteractionGate.Instance.ExecuteManualAsync(() => _store.DeleteItemAsync(uid, row.Name));
            if (SelectedUid == uid) await SelectUidAsync(uid);
        }
        catch (Exception ex) { await InfoAsync(AccountRecordsErrorMessages.Describe(ex), "狛荷屋记录", DialogKind.Error); }
    }
}
