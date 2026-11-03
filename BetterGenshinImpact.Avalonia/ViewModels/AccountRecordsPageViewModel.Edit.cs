using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Avalonia.Views;
using BetterGenshinImpact.Host.Common.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// 狛荷屋“新增库存记录…”（“更多”菜单）与行右键“修改数量 / 修订字典”（同 WPF AccountRecordsInventoryViewModel.Add / Edit）：
/// 打开前固定 UID，保存成功后只在仍是该 UID 时刷新列表
/// </summary>
public partial class AccountRecordsPageViewModel
{
    /// <summary>最近一次打开的编辑模型（自测读取）</summary>
    public AccountRecordsInventoryEditModel? LastEdit { get; private set; }

    /// <summary>无上报数据时也可由用户显式新增，不创建演示数据</summary>
    [RelayCommand]
    private Task AddInventory() => OpenEditAsync(null);

    /// <summary>修改数量；已收录名称与内置非空分类只读，用户补充分类允许修订</summary>
    [RelayCommand]
    private Task EditInventory(InventoryRowSnapshot? row) => row == null ? Task.CompletedTask : OpenEditAsync(row);

    private async Task OpenEditAsync(InventoryRowSnapshot? row)
    {
        var uid = SelectedUid;
        if (uid == null) return;
        try
        {
            var workspace = await _store.LoadAsync(uid, false);
            var editor = new AccountRecordsInventoryEditModel(_store, _catalog, uid, row, workspace.Definitions);
            LastEdit = editor;
            if (AvaloniaPickers.MainWindow is not { } owner) return;
            var dialog = new FAContentDialog
            {
                Title = row == null ? "新增库存记录" : "编辑库存记录", Content = new AccountRecordsInventoryEditView(editor),
                PrimaryButtonText = "确认当前库存", CloseButtonText = "取消", DefaultButton = FAContentDialogButton.Primary,
            };
            // 校验或保存失败时保留对话框并显示错误（同 WPF 窗口只在 Saved 后关闭）
            dialog.PrimaryButtonClick += async (_, args) =>
            {
                var deferral = args.GetDeferral();
                try { args.Cancel = !await editor.SaveAsync(); }
                finally { deferral.Complete(); }
            };
            if (await FluentMessageBox.ShowCustomAsync(dialog, owner) == FAContentDialogResult.Primary && SelectedUid == uid)
                await SelectUidAsync(uid);
        }
        catch (Exception ex)
        {
            StatusText = AccountRecordsErrorMessages.Describe(ex);
        }
    }
}
