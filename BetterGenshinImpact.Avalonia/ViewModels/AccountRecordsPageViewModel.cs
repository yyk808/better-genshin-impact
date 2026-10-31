using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.AccountRecords;
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
            if (SelectedUid == null || !UidOptions.Contains(SelectedUid)) SelectedUid = uids.FirstOrDefault();
            else await SelectUidAsync(SelectedUid);
            if (UidOptions.Count == 0) { _all = []; ApplyFilter(); StatusText = "尚未添加 UID。"; }
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
            StatusText = $"UID {uid}：{_all.Length} 项";
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
}
