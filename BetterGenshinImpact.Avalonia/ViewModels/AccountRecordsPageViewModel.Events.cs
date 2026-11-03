using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Avalonia.Views;
using BetterGenshinImpact.Service.AccountRecords;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// 狛荷屋“事件记录…”（同 WPF 库存页签“更多操作”里的事件记录）：读取当前 UID 的事件，批量归档后刷新库存列表
/// </summary>
public partial class AccountRecordsPageViewModel
{
    /// <summary>最近一次打开的事件记录（自测读取）</summary>
    public AccountRecordsEventsViewModel? LastEvents { get; private set; }

    /// <summary>打开当前 UID 的事件记录；name / eventId 用于从提醒跳转时定位（同 WPF OpenEventRecordsAsync）</summary>
    [RelayCommand]
    private async Task OpenEvents()
    {
        var uid = SelectedUid;
        if (uid == null) return;
        await OpenEventRecordsAsync(uid, null, null);
    }

    /// <summary>读取前固定账号、物品和定位事件，避免异步期间切换 UID 串到另一账号</summary>
    public async Task OpenEventRecordsAsync(string uid, string? name, string? eventId)
    {
        try
        {
            var workspace = await _store.ReadAsync(uid);
            var events = new AccountRecordsEventsViewModel(workspace.Inventory, eventId, async (kind, ids) =>
            {
                await _store.ArchiveCompletedEventsAsync(uid, kind, ids);
                if (SelectedUid == uid) await SelectUidAsync(uid);
                return (await _store.ReadAsync(uid)).Inventory;
            }, name);
            LastEvents = events;
            if (AvaloniaPickers.MainWindow is not { } owner) return;
            var dialog = new FAContentDialog { Title = "事件记录", Content = new AccountRecordsEventsView(events), CloseButtonText = "关闭" };
            await FluentMessageBox.ShowCustomAsync(dialog, owner);
        }
        catch (Exception ex)
        {
            StatusText = AccountRecordsErrorMessages.Describe(ex);
        }
    }
}
