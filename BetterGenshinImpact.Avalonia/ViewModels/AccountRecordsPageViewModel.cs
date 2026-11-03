using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Platform;
using FluentAvalonia.UI.Controls;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// 狛荷屋(test)（对应 WPF AccountRecordsPage）：只读库存表与 UID 切换/新增。
/// 存储、UID 规则与行数据均来自 Core（InventoryStore、AccountRecordsAccounts、InventoryRowBuilder），与 Windows 版共用 User/AccountRecords。
/// </summary>
public partial class AccountRecordsPageViewModel : PageViewModel
{
    public override string Title => "狛荷屋(test)";

    /// <summary>与 WPF 相同：页面进入时同步 UID 列表并读取当前账号库存</summary>
    public override void OnNavigatedTo() => _ = Load();

    private readonly InventoryStore _store;
    private readonly InventoryCatalog _catalog;
    private InventoryRowSnapshot[] _all = [];
    private int _loadVersion;

    public AccountRecordsPageViewModel() : this(new InventoryCatalog()) { }

    public AccountRecordsPageViewModel(InventoryCatalog catalog) : this(catalog, new InventoryStore(catalog)) { }

    public AccountRecordsPageViewModel(InventoryCatalog catalog, InventoryStore store)
    {
        _catalog = catalog;
        _store = store;
    }

    public ObservableCollection<string> UidOptions { get; } = [];

    public ObservableCollection<InventoryRowSnapshot> Rows { get; } = [];

    [ObservableProperty] private string? _selectedUid;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _pendingUid = "";
    [ObservableProperty] private string _uidValidationMessage = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isAddingUid;

    public bool HasRows => Rows.Count > 0;

    partial void OnSelectedUidChanged(string? value) => _ = SelectUidAsync(value);

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    public async Task Load()
    {
        try
        {
            var uids = await _store.ListUidsAsync();
            foreach (var uid in UidOptions.Except(uids).ToArray()) UidOptions.Remove(uid);
            foreach (var uid in uids.Except(UidOptions)) UidOptions.Add(uid);
            // 账户页“打开狛荷屋”：选中该账户的 UID
            if (AccountsPageViewModel.PendingAccountRecordsUid is { } pending)
            {
                AccountsPageViewModel.PendingAccountRecordsUid = null;
                if (UidOptions.Contains(pending)) SelectedUid = pending;
            }
            if (SelectedUid == null || !UidOptions.Contains(SelectedUid)) SelectedUid = uids.FirstOrDefault();
            else await SelectUidAsync(SelectedUid);
            if (UidOptions.Count == 0) { _all = []; ApplyFilter(); StatusText = "尚未添加 UID。"; }
            await LoadMaintenanceSettingsAsync();
        }
        catch (Exception ex) { StatusText = AccountRecordsErrorMessages.Describe(ex); }
    }

    /// <summary>切换账号时读取库存；较早发起的读取结果会被丢弃，避免串号。</summary>
    public async Task SelectUidAsync(string? uid)
    {
        var version = ++_loadVersion;
        if (uid == null) { _all = []; ApplyFilter(); return; }
        try
        {
            var workspace = await _store.LoadAsync(uid, false);
            var rows = await Task.Run(() => InventoryRowBuilder.Build(workspace, _catalog));
            if (version != _loadVersion) return;
            _all = rows.OrderByDescending(r => r.Prior).ThenBy(r => r.ItemNameSortKey, _catalog.TextComparer).ToArray();
            ApplyFilter();
            StatusText = workspace.WarningRuleError != null ? $"提醒规则无效：{workspace.WarningRuleError}" : $"UID {uid}：{_all.Length} 项";
        }
        catch (Exception ex)
        {
            if (version != _loadVersion) return;
            _all = []; ApplyFilter();
            StatusText = AccountRecordsErrorMessages.Describe(ex);
        }
    }

    private void ApplyFilter()
    {
        var text = SearchText.Trim();
        Rows.Clear();
        foreach (var row in _all.Where(r => text.Length == 0 || r.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || r.Category.Contains(text, StringComparison.OrdinalIgnoreCase)))
            Rows.Add(row);
        OnPropertyChanged(nameof(HasRows));
    }

    [RelayCommand]
    private void BeginAddUid()
    {
        PendingUid = "";
        UidValidationMessage = "";
        IsAddingUid = true;
    }

    [RelayCommand]
    private void CancelAddUid()
    {
        IsAddingUid = false;
        PendingUid = "";
        UidValidationMessage = "";
    }

    /// <summary>保存空账号；重复输入时直接选择已有项（与 WPF 相同）。</summary>
    [RelayCommand]
    public async Task AddUid()
    {
        var uid = PendingUid.Trim();
        if (!AccountRecordsAccounts.IsValidUid(uid))
        {
            UidValidationMessage = AccountRecordsAccounts.UidValidationMessage;
            return;
        }
        try
        {
            await AccountRecordsInteractionGate.Instance.ExecuteManualAsync(() => _store.AddUidAsync(uid));
            if (!UidOptions.Contains(uid)) UidOptions.Add(uid);
            SelectedUid = uid;
            CancelAddUid();
        }
        catch (Exception ex) { UidValidationMessage = AccountRecordsErrorMessages.Describe(ex); }
    }

    // —— 第 3 步：导入/导出、清除与两次重置确认（FAContentDialog；确认文字来自 Core 的 AccountRecordsAccounts / AccountRecordsImport） ——

    /// <summary>选择导出路径（参数为建议文件名）；默认用系统保存对话框，自测时替换</summary>
    public Func<string, Task<string?>> PickExportPath { get; set; } = async suggested =>
    {
        if (AvaloniaPickers.MainWindow?.StorageProvider is not { CanSave: true } storage) return null;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出当前库存", SuggestedFileName = suggested, DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("账号记录 JSON") { Patterns = ["*.json"] }]
        });
        return file?.TryGetLocalPath();
    };

    /// <summary>选择导入文件；默认用系统打开对话框，自测时替换</summary>
    public Func<Task<string?>> PickImportPath { get; set; } = async () =>
    {
        if (AvaloniaPickers.MainWindow?.StorageProvider is not { CanOpen: true } storage) return null;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择账号记录", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("账号记录 JSON") { Patterns = ["*.json"] }]
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    };

    /// <summary>最近一次导入的完成摘要（自测读取）</summary>
    [ObservableProperty] private string _importMessage = "";

    private static async Task<bool> ConfirmAsync(string text, string title)
    {
        var owner = AvaloniaPickers.MainWindow;
        return owner is not null && await FluentMessageBox.ShowAsync(owner, text, title, DialogKind.Warning, DialogButtons.YesNo) == DialogResult.Yes;
    }

    private static async Task InfoAsync(string text, string title, DialogKind kind = DialogKind.Information)
    {
        var owner = AvaloniaPickers.MainWindow;
        if (owner is not null) await FluentMessageBox.ShowAsync(owner, text, title, kind, DialogButtons.Ok);
    }

    /// <summary>导出所选 UID 的当前库存（与 WPF 相同：先写临时文件再替换目标）</summary>
    [RelayCommand]
    public async Task ExportInventory()
    {
        var uid = SelectedUid;
        if (uid == null)
        {
            await InfoAsync(AccountRecordsAccounts.NoUidSelectedText, "导出库存");
            return;
        }
        var path = await PickExportPath(AccountRecordsAccounts.DefaultExportFileName(uid, DateTime.Now));
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            await AccountRecordsAccounts.ExportInventoryAsync(_store, uid, path);
            await InfoAsync(AccountRecordsAccounts.ExportDoneText, "导出完成");
        }
        catch (Exception ex) { await InfoAsync(AccountRecordsErrorMessages.Describe(ex), "导出失败", DialogKind.Error); }
    }

    /// <summary>导入：选择文件 → 预览对话框（目标 UID、是否补充字典）→ 开发增量确认 → 合并 → 刷新目标账号</summary>
    [RelayCommand]
    public async Task OpenImport()
    {
        var path = await PickImportPath();
        if (string.IsNullOrEmpty(path)) return;
        AccountRecordsImportPreview preview;
        try { preview = await AccountRecordsImport.PreviewAsync(path); }
        catch (Exception ex)
        {
            await InfoAsync("文件未载入。\n" + AccountRecordsErrorMessages.Describe(ex), "导入库存", DialogKind.Error);
            return;
        }

        var owner = AvaloniaPickers.MainWindow;
        if (owner is null) return;
        // 同 WPF AccountRecordsImportDialog：摘要、目标 UID、记录时间说明、补充字典勾选与条目列表
        var wrap = global::Avalonia.Media.TextWrapping.Wrap;
        var uidBox = new TextBox { Name = "ImportUidBox", Text = preview.FileUid, Width = 200, MaxLength = 20, HorizontalAlignment = HorizontalAlignment.Left };
        var definitions = new CheckBox { Name = "ImportDefinitionsBox", Content = "使用下列分类补充本地字典空缺（不覆盖已有分类）", IsEnabled = preview.Definitions.Count > 0 };
        var content = new StackPanel { Spacing = 8, MaxWidth = 480 };
        content.Children.Add(new TextBlock { Text = preview.Summary, TextWrapping = wrap });
        // Linux 版狛荷屋的 UID 列表来自数据目录，导入到新 UID 后直接出现在列表中（WPF 为 Pulonia 账户目录，不在目录中时先建账户）
        content.Children.Add(new TextBlock { Text = "导入到 UID（可修改；导入到新 UID 后出现在 UID 列表中）", TextWrapping = wrap });
        content.Children.Add(uidBox);
        content.Children.Add(new TextBlock { Name = "ImportNoteText", Text = ImportTimeNote, TextWrapping = wrap, Opacity = 0.7 });
        content.Children.Add(definitions);
        content.Children.Add(new ScrollViewer
        {
            MaxHeight = 160,
            Content = new ItemsControl { Name = "ImportDefinitionsList", ItemsSource = preview.Definitions },
        });
        var dialog = new FAContentDialog
        {
            Title = "导入库存", Content = content, PrimaryButtonText = "导入当前库存", CloseButtonText = "取消", DefaultButton = FAContentDialogButton.Primary
        };
        if (await FluentMessageBox.ShowCustomAsync(dialog, owner) != FAContentDialogResult.Primary) return;

        if (preview.HasDeltas && !await ConfirmAsync(AccountRecordsImport.DeltaConfirmText, AccountRecordsImport.DeltaConfirmTitle)) return;
        var uid = (uidBox.Text ?? "").Trim();
        string? importedUid = null;
        InventoryReportResult? result = null;
        string? message = null;
        try
        {
            if (!InventoryStore.IsValidUid(uid)) throw new ArgumentException(AccountRecordsImport.InvalidTargetUid);
            importedUid = uid;
            result = await AccountRecordsInteractionGate.Instance.ExecuteManualAsync(() =>
                _store.ImportInventoryAsync(preview.Package, uid, definitions.IsChecked == true, preview.HasDeltas));
            message = AccountRecordsImport.WarningMessage(result);
        }
        catch (Exception ex) { message = AccountRecordsImport.FailureMessage(ex); }
        var hasErrors = result != null && AccountRecordsImport.HasErrors(result);
        ImportMessage = string.Join("\n", new[] { hasErrors ? ImportErrorCounts(result!) : null, message }.Where(text => text != null));
        await RefreshAfterImportAsync(importedUid);
        // 同 WPF：无异常、无提醒时直接关闭并在底部提示行短暂显示完成摘要；否则在“导入库存”对话框中显示计数与说明
        if (result != null && !hasErrors && message == null)
        {
            var summary = AccountRecordsImport.CompletionSummary(result);
            ImportMessage = summary;
            _ = ShowImportSummaryAsync(summary);
            return;
        }
        await ShowImportResultAsync(hasErrors ? result : null, message);
    }

    /// <summary>导入说明（同 WPF）</summary>
    public const string ImportTimeNote = "记录时间应为库存实际采集或确认的时间，导入时不会更新为当前时间。仅合并提供的物品，旧记录、非法数量等会进入库存异常。";

    /// <summary>有异常时的计数文字（同 WPF 导入窗口）</summary>
    private static string ImportErrorCounts(InventoryReportResult result) =>
        $"导入完成：接受 {result.Accepted.Count} 项，忽略重复 {result.DuplicateCount} 项（已有异常 {result.DuplicateErrorCount} 项），新增异常 {result.Rejected.Count} 项。关闭窗口后可在目标 UID 的库存中查看，红灯异常需要手动处理。";

    /// <summary>导入结果对话框：有异常时计数（异常数红色加粗），提醒或失败说明另起一段</summary>
    private static async Task ShowImportResultAsync(InventoryReportResult? errors, string? message)
    {
        if (AvaloniaPickers.MainWindow is not { } owner) return;
        var wrap = global::Avalonia.Media.TextWrapping.Wrap;
        var panel = new StackPanel { Spacing = 12, MaxWidth = 480 };
        if (errors != null)
        {
            var counts = new TextBlock { Name = "ImportResultText", TextWrapping = wrap };
            global::Avalonia.Controls.Documents.Run Plain(string text) => new(text);
            global::Avalonia.Controls.Documents.Run Red(int value) => new(value.ToString())
            { Foreground = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#EA4335")), FontWeight = global::Avalonia.Media.FontWeight.SemiBold };
            counts.Inlines!.AddRange([
                Plain($"导入完成：接受 {errors.Accepted.Count} 项，忽略重复 {errors.DuplicateCount} 项（已有异常 "), Red(errors.DuplicateErrorCount),
                Plain(" 项），新增异常 "), Red(errors.Rejected.Count), Plain(" 项。关闭窗口后可在目标 UID 的库存中查看，红灯异常需要手动处理。")]);
            panel.Children.Add(counts);
        }
        if (message != null) panel.Children.Add(new TextBlock { Name = "ImportMessageText", Text = message, TextWrapping = wrap });
        await FluentMessageBox.ShowCustomAsync(new FAContentDialog { Title = "导入库存", Content = panel, CloseButtonText = "关闭" }, owner);
    }

    /// <summary>底部提示行显示导入摘要 8 秒；期间切换账号或提示已变化则不恢复（同 WPF ShowImportSummaryAsync）</summary>
    private async Task ShowImportSummaryAsync(string summary)
    {
        var previous = StatusText;
        var version = _loadVersion;
        StatusText = summary;
        await Task.Delay(TimeSpan.FromSeconds(8));
        if (version == _loadVersion && StatusText == summary) StatusText = previous;
    }

    /// <summary>导入可能部分成功；无论是否有异常，均完整读取目标账号（与 WPF 相同）</summary>
    private async Task RefreshAfterImportAsync(string? importedUid)
    {
        await Load();
        if (importedUid == null) return;
        if (UidOptions.Contains(importedUid)) SelectedUid = importedUid;
        await SelectUidAsync(SelectedUid);
    }

    /// <summary>清空库存（inventory）或连同 UID 一并删除（uid），先确认</summary>
    [RelayCommand]
    public async Task ClearAccount(string scope)
    {
        if (!AccountRecordsAccounts.IsClearScope(scope)) return;
        var uid = SelectedUid;
        if (uid == null) return;
        if (!await ConfirmAsync(AccountRecordsAccounts.ClearConfirmText(scope), "清除账号记录")) return;
        try
        {
            await AccountRecordsInteractionGate.Instance.ExecuteManualAsync(() => _store.ClearAsync(uid, false, scope == "uid"));
            await Load();
        }
        catch (Exception ex) { await InfoAsync(AccountRecordsErrorMessages.Describe(ex), "清除账号记录失败", DialogKind.Error); }
    }

    /// <summary>两次确认后重置全部账号数据和设置（原数据保留在同级备份目录）</summary>
    [RelayCommand]
    public async Task ResetToDefaults()
    {
        if (!await ConfirmAsync(AccountRecordsAccounts.ResetConfirm1.Text, AccountRecordsAccounts.ResetConfirm1.Title)) return;
        if (!await ConfirmAsync(AccountRecordsAccounts.ResetConfirm2.Text, AccountRecordsAccounts.ResetConfirm2.Title)) return;
        try
        {
            await AccountRecordsInteractionGate.Instance.ExecuteManualAsync(() => _store.ResetToDefaultsAsync());
            CancelAddUid();
            SelectedUid = null;
            UidOptions.Clear();
            SearchText = "";
            await Load();
            await InfoAsync(AccountRecordsAccounts.ResetDoneText, "重置完成");
        }
        catch (Exception ex) { await InfoAsync(AccountRecordsErrorMessages.Describe(ex), "重置失败", DialogKind.Error); }
    }
}
