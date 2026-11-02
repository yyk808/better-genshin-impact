using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Host.Common;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Presentation;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using Serilog;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// “账户”页（对应 WPF PuloniaAccountsPage）：顶部是本实例当前账户（修改立即保存），左侧账户列表，右侧所选账户的“概况”“登录资料”。
/// 概况与登录资料是草稿，“保存”一次保存整个目录（带凭据草稿、检查修订）；删除账户确认后立即保存。
/// 账户目录、凭据、切换与 UID 读取都用 Core 的同一实现：凭据在 Linux 存入系统密钥环（freedesktop Secret Service），
/// 自动切换支持本机 X11 / Wayland（XTest / uinput 输入）下 Wine 运行的 YuanShen.exe。
/// 路线 CD 在“任务计划”页查看；首领领奖统计尚未迁移
/// </summary>
public sealed partial class AccountsPageViewModel : PageViewModel
{
    private const string SingleAccountHeader = "只有一个账号时不需要任何设置。有多个账号时，在这里添加账户，并在本页顶部选择每个实例当前运行的账户。";
    private const string MultipleAccountsHeader = "为每个账户填写 UID，并在本页顶部选择每个实例当前运行的账户；需要 BetterGI 自动切换时，再填写登录资料。";

    private readonly IPuloniaAccountService? _accounts;
    private readonly PuloniaTaskStore? _store;
    private readonly PuloniaGameTaskCoordinator? _coordinator;
    private readonly IPuloniaGenshinAccountSwitcher? _switcher;
    private readonly PuloniaRunOverview? _runOverview;
    private readonly Func<string, bool> _navigate;
    private readonly ILogger _log;
    private long _revision;
    private IReadOnlyList<string> _savedOrder = [];
    private bool _loaded;
    private bool _subscribed;
    private string? _currentAccountId;
    private string? _lastSaveWarning;
    private CancellationTokenSource? _operation;
    private int _overviewVersion;

    public AccountsPageViewModel(GameHost? host, Func<string, bool> navigate, ILogger log)
    {
        _navigate = navigate;
        _log = log;
        var services = host?.Services;
        _accounts = services?.GetService<IPuloniaAccountService>();
        _store = services?.GetService<PuloniaTaskStore>();
        _coordinator = services?.GetService<PuloniaGameTaskCoordinator>();
        _switcher = services?.GetService<IPuloniaGenshinAccountSwitcher>();
        if (services?.GetService<IPuloniaTaskService>() is { } tasks && _store is not null && _accounts is not null)
            _runOverview = new PuloniaRunOverview(tasks, _store, _accounts.CurrentInstanceKey);
        Accounts.CollectionChanged += (_, _) => OnAccountListChanged();
    }

    public override string Title => "账户";

    /// <summary>“打开狛荷屋”时要选中的 UID（狛荷屋页读取后清空）</summary>
    public static string? PendingAccountRecordsUid { get; set; }

    public ObservableCollection<PuloniaAccountItemViewModel> Accounts { get; } = [];
    public ObservableCollection<PuloniaAccountChoiceViewModel> CurrentAccountOptions { get; } = [];
    public ObservableCollection<PuloniaAccountPlanUsage> PlanUsages { get; } = [];

    [ObservableProperty] private PuloniaAccountItemViewModel? _selectedAccount;
    [ObservableProperty] private int _selectedDetailTabIndex;
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isGameOperationRunning;
    [ObservableProperty] private string _lastRunText = "";
    /// <summary>凭据存储状态：系统密钥环是否可用（打开页面时检查一次）</summary>
    [ObservableProperty] private string _credentialStoreText = "";
    [ObservableProperty] private bool _credentialStoreAvailable;

    public bool CanEdit => !IsBusy;
    public bool IsSingleAccount => Accounts.Count <= 1;
    public bool ShowAccountList => !IsSingleAccount;
    public string HeaderText => IsSingleAccount ? SingleAccountHeader : MultipleAccountsHeader;
    public bool HasPlanUsages => PlanUsages.Count > 0;
    public bool HasSelectedAccount => SelectedAccount is not null;

    public string CurrentInstanceName => _accounts is null ? "主实例"
        : PuloniaInstanceKey.DescribeDirectory(PuloniaInstanceKey.ToDirectoryName(_accounts.CurrentInstanceKey));

    public string CurrentInstanceText => CurrentInstanceName + " · 本实例的游戏现在登录的账户；修改立即保存，下一次运行开始时生效。";

    public bool IsCloudInstance => _accounts?.CurrentInstanceKey.StartsWith(PuloniaInstanceKey.WebViewPrefix, StringComparison.Ordinal) == true;

    /// <summary>本实例当前账户 ID；从界面写入时立即保存（下拉框重建选项时写回的 null 忽略）</summary>
    public PuloniaAccountChoiceViewModel? CurrentAccountChoice
    {
        get => CurrentAccountOptions.FirstOrDefault(o => o.Id == _currentAccountId);
        set
        {
            if (value is null || value.Id == _currentAccountId) return;
            _ = ChangeCurrentAccountAsync(value.Id);
        }
    }

    public string? CurrentAccountHint => _loaded && _currentAccountId is null
        ? "本实例当前账户未确定，跟随当前账户的运行会进入待处理。请选择游戏中现在登录的账户。"
        : null;

    public bool HasCurrentAccountHint => CurrentAccountHint is not null;

    public bool HasUnsavedChanges => !Accounts.Select(a => a.Id).SequenceEqual(_savedOrder) || Accounts.Any(a => a.IsDirty);

    public IReadOnlyList<KeyValuePair<PuloniaTaskServerTimeZone, string>> ServerTimeZones { get; } =
    [
        new(PuloniaTaskServerTimeZone.UtcPlus8, "国服 / 亚服 / 港澳台（UTC+8）"),
        new(PuloniaTaskServerTimeZone.Europe, "欧服（UTC+1）"),
        new(PuloniaTaskServerTimeZone.America, "美服（UTC-5）"),
    ];

    public IReadOnlyList<KeyValuePair<PuloniaGenshinAccountLoginMode, string>> LoginModes { get; } =
    [
        new(PuloniaGenshinAccountLoginMode.Automatic, "自动：图片 → 列表文字 → 可选密码"),
        new(PuloniaGenshinAccountLoginMode.Image, "账户图片"),
        new(PuloniaGenshinAccountLoginMode.ListText, "列表文字"),
        new(PuloniaGenshinAccountLoginMode.Password, "账号密码"),
    ];

    /// <summary>所选账户的服务器（ComboBox 绑定键值对）</summary>
    public KeyValuePair<PuloniaTaskServerTimeZone, string>? SelectedServerTimeZone
    {
        get => SelectedAccount is { } a ? ServerTimeZones.FirstOrDefault(p => p.Key == a.ServerTimeZone) : null;
        set { if (value is { } v && SelectedAccount is { } a) a.ServerTimeZone = v.Key; }
    }

    /// <summary>所选账户的登录方式</summary>
    public KeyValuePair<PuloniaGenshinAccountLoginMode, string>? SelectedLoginMode
    {
        get => SelectedAccount is { } a ? LoginModes.FirstOrDefault(p => p.Key == a.LoginMode) : null;
        set { if (value is { } v && SelectedAccount is { } a) a.LoginMode = v.Key; }
    }

    public string TestSwitchHint
    {
        get
        {
            if (IsCloudInstance) return "云原神实例不能切换账户。";
            if (SelectedAccount is not { } account) return "";
            if (string.IsNullOrEmpty(account.Saved?.Uid)) return "账户没有 UID，不能测试切换。请先填写 UID 并保存。";
            return "在本实例的游戏中切换到这个账户并核验 UID，成功后本实例当前账户改为这个账户。使用已保存的登录资料，修改后请先保存。"
                   + "Linux 版支持本机 X11 / Wayland 窗口（XTest / uinput 输入）中 Wine / Proton 运行的 YuanShen.exe。";
        }
    }

    public override void OnNavigatedTo()
    {
        if (!_subscribed && _accounts is not null)
        {
            _subscribed = true;
            _accounts.AccountsChanged += (_, _) => Dispatcher.UIThread.Post(() => { if (!IsBusy) _ = LoadAsync(false); });
            _accounts.CurrentAccountChanged += (_, _) => Dispatcher.UIThread.Post(() => _ = RefreshCurrentAccountsAsync());
        }
        _ = EnterAsync();
    }

    private async Task EnterAsync()
    {
        await LoadAsync(false);
        await RefreshOverviewAsync();
        await ProbeCredentialStoreAsync();
    }

    /// <summary>检查凭据存储：Linux 为系统密钥环（Secret Service）；不可用时说明原因，保存密码会失败而不会明文保存</summary>
    public async Task ProbeCredentialStoreAsync()
    {
        var reason = await Task.Run(() => OperatingSystem.IsLinux() ? BetterGenshinImpact.Platform.Linux.Accounts.SecretServiceCredentialVault.Probe() : null);
        CredentialStoreAvailable = reason is null;
        CredentialStoreText = reason is null
            ? "密码保存在系统密钥环（Secret Service：GNOME Keyring / KWallet / KeePassXC）中，不写入 BetterGI 的文件。"
            : reason;
    }

    public async Task LoadAsync(bool force)
    {
        if (IsBusy || _accounts is null) return;
        IsBusy = true;
        try { await LoadCoreAsync(force); }
        finally { IsBusy = false; }
    }

    private async Task LoadCoreAsync(bool force)
    {
        try
        {
            var settings = await _accounts!.LoadAccountsAsync();
            if (force || !_loaded || (!HasUnsavedChanges && settings.Revision != _revision))
                ApplyDirectory(settings);
            else if (HasUnsavedChanges && settings.Revision != _revision)
                Message = "账户目录已在其他地方修改。本页有未保存的修改，保存时会提示冲突；可以先“放弃修改”再查看最新内容。";
            _loaded = true;
            await RefreshCurrentAccountsCoreAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Message = "账户读取失败：" + ex.Message;
        }
    }

    private void ApplyDirectory(PuloniaGenshinAccountSettings settings)
    {
        var selectedId = SelectedAccount?.Id;
        foreach (var item in Accounts) { item.PropertyChanged -= OnAccountPropertyChanged; item.ClearCredentialDraft(); }
        Accounts.Clear();
        foreach (var account in settings.Accounts) Accounts.Add(Attach(new PuloniaAccountItemViewModel(account)));
        _revision = settings.Revision;
        _savedOrder = settings.Accounts.Select(a => a.Id).ToArray();
        RebuildCurrentAccountOptions(settings.Accounts);
        SelectedAccount = Accounts.FirstOrDefault(a => a.Id == selectedId) ?? Accounts.FirstOrDefault();
        NotifyDirtyChanged();
    }

    private PuloniaAccountItemViewModel Attach(PuloniaAccountItemViewModel item)
    {
        item.PropertyChanged += OnAccountPropertyChanged;
        return item;
    }

    private void RebuildCurrentAccountOptions(IEnumerable<PuloniaGenshinAccountProfile> saved)
    {
        CurrentAccountOptions.Clear();
        foreach (var account in saved) CurrentAccountOptions.Add(new PuloniaAccountChoiceViewModel(account, account.Id == _currentAccountId));
        OnPropertyChanged(nameof(CurrentAccountChoice));
    }

    private async Task RefreshCurrentAccountsAsync()
    {
        try { await RefreshCurrentAccountsCoreAsync(); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Message = "当前账户读取失败：" + ex.Message; }
    }

    private async Task RefreshCurrentAccountsCoreAsync()
    {
        SetCurrentAccountIdSilently(await _accounts!.GetCurrentAccountIdAsync(_accounts.CurrentInstanceKey));
        var instances = await _accounts.ListInstanceSettingsAsync();
        var currentDirectory = PuloniaInstanceKey.ToDirectoryName(_accounts.CurrentInstanceKey);
        var names = instances.Items.Where(e => e.Settings.CurrentAccountId is not null)
            .OrderBy(e => e.DirectoryName == PuloniaInstanceKey.Primary ? 0 : e.DirectoryName == PuloniaInstanceKey.ChildSession ? 1 : 2)
            .ThenBy(e => e.DirectoryName, StringComparer.Ordinal)
            .GroupBy(e => e.Settings.CurrentAccountId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => string.Join("、", g.Select(e =>
                PuloniaInstanceKey.DescribeDirectory(e.DirectoryName) + (e.DirectoryName == currentDirectory ? "（本实例）" : ""))), StringComparer.Ordinal);
        foreach (var item in Accounts)
        {
            item.InstancesText = names.GetValueOrDefault(item.Id) ?? "";
            item.RunningText = item.Saved is not null && _accounts.GetAccountOccupant(item.Id) is { } occupant
                ? "运行中 · " + (string.IsNullOrEmpty(occupant.InstanceKey) ? "另一个实例" : PuloniaInstanceKey.Describe(occupant.InstanceKey))
                : "";
        }
        if (instances.Failures.Count > 0)
            Message = "有实例设置文件无法读取，“作为哪些实例的当前账户”可能不完整：" + string.Join("；", instances.Failures);
    }

    private void SetCurrentAccountIdSilently(string? accountId)
    {
        _currentAccountId = accountId;
        foreach (var option in CurrentAccountOptions) option.IsCurrent = option.Id == accountId;
        OnPropertyChanged(nameof(CurrentAccountChoice));
        OnPropertyChanged(nameof(CurrentAccountHint));
        OnPropertyChanged(nameof(HasCurrentAccountHint));
    }

    public async Task ChangeCurrentAccountAsync(string accountId)
    {
        var previous = _currentAccountId;
        SetCurrentAccountIdSilently(accountId);
        try
        {
            await _accounts!.SetCurrentAccountAsync(_accounts.CurrentInstanceKey, accountId);
            var name = CurrentAccountOptions.FirstOrDefault(o => o.Id == accountId)?.Name ?? accountId;
            Message = $"本实例当前账户已改为“{name}”，下一次运行开始时生效。";
            await RefreshCurrentAccountsAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetCurrentAccountIdSilently(previous);
            Message = "未能修改本实例当前账户：" + ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddAccount()
    {
        var names = Accounts.Select(a => a.Name.Trim()).ToHashSet(StringComparer.Ordinal);
        var name = "新账户";
        for (var index = 2; names.Contains(name); index++) name = $"新账户 {index}";
        var account = new PuloniaGenshinAccountProfile { Name = name, ServerTimeZone = PuloniaServerTime.GetDefaultServerTimeZone() };
        var item = Attach(new PuloniaAccountItemViewModel(account, isSaved: false));
        Accounts.Add(item);
        SelectedAccount = item;
        SelectedDetailTabIndex = 0;
        Message = "已新增账户草稿：填写名称和完整 UID（可以从当前游戏读取），选择服务器后点“保存”。";
    }

    private bool CanMoveUp() => CanEdit && SelectedAccount is { } a && Accounts.IndexOf(a) > 0;
    private bool CanMoveDown() => CanEdit && SelectedAccount is { } a && Accounts.IndexOf(a) is var i && i >= 0 && i < Accounts.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => MoveSelected(-1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => MoveSelected(1);

    private void MoveSelected(int offset)
    {
        if (SelectedAccount is not { } account) return;
        var index = Accounts.IndexOf(account);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= Accounts.Count) return;
        Accounts.Move(index, target);
        SelectedAccount = account;
        Message = "已调整账户顺序，点“保存”后生效。";
    }

    private bool CanEditSelected() => CanEdit && SelectedAccount is not null;

    [RelayCommand(CanExecute = nameof(CanEditSelected))]
    private async Task RenameAccountAsync()
    {
        if (SelectedAccount is not { } account) return;
        var name = (await FluentMessageBox.PromptAsync("请输入新的账户名称，名称不能与其他账户重复。", "账户改名", account.Name))?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name == account.Name.Trim()) return;
        account.Name = name;
        Message = $"账户已改名为“{name}”，点“保存”后生效。";
    }

    private bool CanDeleteAccount() => CanEditSelected() && Accounts.Count > 1;

    [RelayCommand(CanExecute = nameof(CanDeleteAccount))]
    private async Task DeleteAccountAsync()
    {
        if (SelectedAccount is not { } account || Accounts.Count <= 1 || IsBusy || _accounts is null) return;
        if (account.Saved is null)
        {
            RemoveFromDraft(account);
            Message = $"已移除尚未保存的账户“{account.Name}”。";
            return;
        }
        IsBusy = true;
        try
        {
            if (await _accounts.CheckAccountRemovalAsync(account.Id) is { } blocker)
            {
                Message = "不能删除账户：" + blocker;
                await ShowAsync(blocker, "不能删除账户", DialogKind.Warning, DialogButtons.Ok);
                return;
            }
            var othersDirty = Accounts.Where(i => !ReferenceEquals(i, account)).Any(i => i.IsDirty)
                              || !Accounts.Where(i => !ReferenceEquals(i, account)).Select(i => i.Id).SequenceEqual(_savedOrder.Where(id => id != account.Id));
            string dataText;
            try
            {
                var items = (await _accounts.DescribeAccountRemovalAsync(account.Id)).Describe();
                dataText = items.Count == 0
                    ? "这个账户没有保存登录凭据、账户图片、路线 CD 完成记录、首领领奖次数或狛荷屋记录。"
                    : "将一并删除：\n" + string.Join("\n", items.Select(i => "• " + i));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                dataText = "将一并删除它保存的登录凭据和账户图片，以及该账户的路线 CD 完成记录、首领领奖次数和狛荷屋记录" + $"（未能读取具体数据：{ex.Message}）。";
            }
            var confirm = $"删除账户“{account.Name}”？\n\n{dataText}\n\n删除后不能撤销。" + (othersDirty ? "\n本页其他未保存的修改会一并保存。" : "");
            if (await ShowAsync(confirm, "删除账户", DialogKind.Warning, DialogButtons.YesNo) != DialogResult.Yes) return;
            var index = Accounts.IndexOf(account);
            RemoveFromDraft(account);
            if (await SaveCoreAsync())
            {
                Message = _lastSaveWarning is { } warning ? $"已删除账户“{account.Name}”，但{warning}" : $"已删除账户“{account.Name}”。";
                return;
            }
            if (!Accounts.Any(i => i.Id == account.Id))
            {
                Accounts.Insert(Math.Min(index, Accounts.Count), Attach(account));
                SelectedAccount = account;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Message = "未删除：" + ex.Message;
        }
        finally { IsBusy = false; }
    }

    private void RemoveFromDraft(PuloniaAccountItemViewModel account)
    {
        var index = Accounts.IndexOf(account);
        account.PropertyChanged -= OnAccountPropertyChanged;
        account.ClearCredentialDraft();
        Accounts.Remove(account);
        SelectedAccount = Accounts.Count == 0 ? null : Accounts[Math.Clamp(index, 0, Accounts.Count - 1)];
    }

    private bool CanSave() => CanEdit && HasUnsavedChanges;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await SaveCoreAsync(); }
        finally { IsBusy = false; }
    }

    /// <summary>保存整个目录；凭据写入系统密钥环，失败（如没有密钥环）时目录照常保存、保留密码草稿并说明原因</summary>
    private async Task<bool> SaveCoreAsync()
    {
        var settings = new PuloniaGenshinAccountSettings { Revision = _revision, Accounts = Accounts.Select(a => a.ToProfile()).ToList() };
        var credentials = Accounts.Where(a => a.HasCredentialDraft)
            .ToDictionary(a => a.Id, a => (a.LoginUsername, a.LoginPassword), StringComparer.Ordinal);
        _lastSaveWarning = null;
        try
        {
            // 写入密钥环可能等待解锁提示：放到后台线程，界面不卡住
            var result = await Task.Run(() => _accounts!.SaveAccountsAsync(settings, credentials));
            _revision = result.Settings.Revision;
            _savedOrder = result.Settings.Accounts.Select(a => a.Id).ToArray();
            var keepCredentials = result.CredentialError is not null;
            foreach (var item in Accounts) item.MarkSaved(result.Settings.Accounts.Single(s => s.Id == item.Id), keepCredentials);
            RebuildCurrentAccountOptions(result.Settings.Accounts);
            NotifyDirtyChanged();
            var warnings = new[] { result.CredentialError, result.DataWarning }.OfType<string>().ToArray();
            _lastSaveWarning = warnings.Length > 0 ? string.Join("；", warnings) : null;
            Message = _lastSaveWarning is { } warning ? "账户目录已保存，但" + warning : "账户目录已保存。";
            _ = RefreshOverviewAsync();
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (!await IsRevisionConflictAsync())
            {
                Message = "未保存：" + ex.Message;
                return false;
            }
            Message = "未保存：账户目录已被其他实例或页面修改。";
            if (await ShowAsync("账户目录已被其他实例或页面修改，本页的修改不能直接保存。\n\n要重新读取最新的账户目录吗？本页未保存的修改会丢失。",
                    "账户目录已变更", DialogKind.Warning, DialogButtons.YesNo) == DialogResult.Yes)
            {
                await LoadCoreAsync(force: true);
                Message = "已重新读取最新的账户目录，请重新修改后保存。";
            }
            return false;
        }
    }

    private async Task<bool> IsRevisionConflictAsync()
    {
        try { return (await _accounts!.LoadAccountsAsync()).Revision != _revision; }
        catch (Exception ex) when (ex is not OperationCanceledException) { return false; }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task DiscardChangesAsync()
    {
        if (await ShowAsync("放弃本页未保存的修改，并重新读取账户目录？", "放弃修改", DialogKind.Question, DialogButtons.YesNo) != DialogResult.Yes) return;
        await LoadAsync(force: true);
        Message = "已放弃未保存的修改。";
    }

    // —— 概况：最近一次运行与指定了该账户的计划 ——

    public async Task RefreshOverviewAsync()
    {
        var version = ++_overviewVersion;
        var account = SelectedAccount;
        if (account?.Saved is null || _accounts is null)
        {
            PlanUsages.Clear();
            OnPropertyChanged(nameof(HasPlanUsages));
            LastRunText = account is null ? "" : "保存后显示。";
            return;
        }
        PuloniaAccountPlanUsage[] usages;
        try { usages = (await _accounts.ListPlansSpecifyingAccountAsync(account.Id)).ToArray(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { _log.Debug(ex, "读取指定账户的计划失败"); usages = []; }
        string lastRun;
        try
        {
            lastRun = _runOverview is null ? ""
                : (await _runOverview.ListAsync()).Runs.Where(r => r.AccountId == account.Id).OrderByDescending(r => r.SubmittedAt).FirstOrDefault() is { } run
                    ? string.Join(" · ", run.PlanName, PuloniaTaskHistoryText.Status(run.Status),
                        PuloniaTaskHistoryText.Time(run.FinishedAt ?? run.StartedAt ?? run.SubmittedAt), PuloniaInstanceKey.Describe(run.Instance))
                    : "还没有运行记录。";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { lastRun = "读取执行记录失败：" + ex.Message; }
        if (version != _overviewVersion) return;
        PlanUsages.Clear();
        foreach (var usage in usages) PlanUsages.Add(usage);
        OnPropertyChanged(nameof(HasPlanUsages));
        LastRunText = lastRun;
    }

    /// <summary>打开指定了该账户的计划：转到任务计划页或新一条龙页并选中</summary>
    public Func<PuloniaAccountPlanUsage, bool>? OpenPlanHandler { get; set; }

    [RelayCommand]
    private void OpenPlan(PuloniaAccountPlanUsage? usage)
    {
        if (usage is not null) OpenPlanHandler?.Invoke(usage);
    }

    [RelayCommand]
    private void OpenAccountRecords()
    {
        if (SelectedAccount?.Saved?.Uid is not { Length: > 0 } uid)
        {
            Message = "账户保存了 UID 后才有狛荷屋记录。";
            return;
        }
        PendingAccountRecordsUid = uid;
        _navigate("狛荷屋(test)");
    }

    // —— 游戏操作：读取 UID、导入 / 采集账户图片、测试切换 ——

    private bool CanRunGameOperation() => CanEdit && SelectedAccount is not null && _coordinator is not null && _switcher is not null;
    private bool CanTestSwitch() => CanRunGameOperation() && !IsCloudInstance && !string.IsNullOrEmpty(SelectedAccount?.Saved?.Uid);

    [RelayCommand(CanExecute = nameof(CanRunGameOperation))]
    private async Task ReadUidAsync()
    {
        if (SelectedAccount is not { } account) return;
        await RunGameOperationAsync("正在读取游戏中的 UID…", "未能读取 UID：", async (lease, ct) =>
        {
            if (await _switcher!.TryReadUidAsync(lease.Runtime, ct) is not { } uid)
            {
                Message = "没有读到 UID，请让游戏停在主界面后重试。";
                return;
            }
            // 账户草稿绑定在界面上，必须在界面线程修改
            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                account.Uid = uid;
                Message = $"已读取 UID {uid}，点“保存”后生效。";
            });
        });
    }

    /// <summary>导入账户图片（测试与自动化可直接调用 ImportImageFileAsync）</summary>
    [RelayCommand(CanExecute = nameof(CanEditSelected))]
    private async Task ImportImageAsync()
    {
        var path = await AvaloniaPickers.PickFileAsync("选择 1080p 登录列表中的账户标识裁剪图（png / jpg）");
        if (path is not null) await ImportImageFileAsync(path);
    }

    public async Task ImportImageFileAsync(string path)
    {
        if (SelectedAccount is not { } account || IsBusy || _store is null) return;
        IsBusy = true;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            using var image = Cv2.ImDecode(bytes, ImreadModes.Color);
            if (image.Empty() || image.Width > 1920 || image.Height > 1080)
                throw new InvalidOperationException("请选择可解码的 1080p 账户标识裁剪图片，或使用“采集当前账户图片”。");
            account.ImagePath = await _store.CreateAccountImageAsync(account.Id, image.ImEncode(".png"));
            Message = "图片已导入，点“保存”后生效。";
        }
        catch (Exception ex) { Message = "图片未导入：" + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanRunGameOperation))]
    private async Task CaptureImageAsync()
    {
        if (SelectedAccount is not { } account) return;
        if (await ShowAsync("将从当前已登录的账户识别 UID，退出到登录界面采集账户标识，再返回游戏。请确认当前登录的是要保存的账户。",
                "采集账户图片", DialogKind.Question, DialogButtons.YesNo) != DialogResult.Yes) return;
        await RunGameOperationAsync("正在采集账户图片…", "采集未完成：", async (lease, ct) =>
        {
            var result = await _switcher!.CaptureCurrentAccountAsync(lease.Runtime, account.Id, ct);
            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                account.Uid = result.Uid;
                account.ImagePath = result.ImagePath;
                Message = "采集完成，已返回游戏。点“保存”后生效。";
            });
        });
    }

    [RelayCommand(CanExecute = nameof(CanTestSwitch))]
    private async Task TestSwitchAsync()
    {
        if (SelectedAccount is not { Saved: { Uid: { Length: > 0 } } saved } account || IsCloudInstance || _accounts is null) return;
        if (account.IsDirty)
        {
            Message = "这个账户有未保存的修改，请先保存，再测试切换。";
            return;
        }
        IReadOnlyList<string> displaced;
        try { displaced = await _accounts.ListOtherOnlineInstancesUsingAccountAsync(saved.Id); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { displaced = []; _log.Debug(ex, "读取在线实例失败"); }
        var displacedText = displaced.Count == 0 ? ""
            : $"\n\n{string.Join("、", displaced.Select(PuloniaInstanceKey.Describe))}的当前账户是“{saved.Name}”且正在线，切换会把它挤下线。";
        if (await ShowAsync($"将在本实例的游戏中切换到账户“{saved.Name}”（UID {saved.Uid}）并核验 UID，当前登录的账户会被登出。{displacedText}\n\n确定继续吗？",
                "测试切换", DialogKind.Question, DialogButtons.YesNo) != DialogResult.Yes) return;
        IDisposable? occupancy;
        PuloniaLockOwner? occupant;
        try { occupancy = _accounts.TryAcquireAccountOccupancy(saved.Id, null, out occupant); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PuloniaTaskValidationException)
        {
            Message = $"无法取得账户“{saved.Name}”的占用，未切换：{ex.Message}";
            return;
        }
        if (occupancy is null)
        {
            Message = $"账户“{saved.Name}”正在{(occupant is null ? "另一个实例" : DescribeOccupant(occupant))}运行，未切换。";
            return;
        }
        using var heldOccupancy = occupancy;
        await RunGameOperationAsync($"正在切换到“{saved.Name}”…", "测试切换失败：", async (lease, ct) =>
        {
            var switchStarted = false;
            string uid;
            try
            {
                uid = await _switcher!.PrepareAsync(lease.Runtime, saved, () => switchStarted = true, ct);
            }
            catch (Exception ex) when (switchStarted)
            {
                var undetermined = await TrySetCurrentAccountAsync(null);
                Message = (ex is OperationCanceledException ? "测试切换已取消。" : "测试切换失败：" + ex.Message)
                          + (undetermined
                              ? "切换已经开始，本实例当前账户已改为“未确定”，请确认游戏中登录的账户后在顶部重新选择。"
                              : "切换已经开始，但未能把本实例当前账户改为“未确定”，请确认游戏中登录的账户后在顶部重新选择。");
                return;
            }
            Message = await TrySetCurrentAccountAsync(saved.Id)
                ? $"已切换到“{saved.Name}”，UID {uid} 核验通过；本实例当前账户已改为“{saved.Name}”。"
                : $"已切换到“{saved.Name}”，UID {uid} 核验通过，但未能修改本实例当前账户，请在顶部手动选择。";
        });
    }

    private async Task<bool> TrySetCurrentAccountAsync(string? accountId)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await _accounts!.SetCurrentAccountAsync(_accounts.CurrentInstanceKey, accountId, cleanup.Token);
            SetCurrentAccountIdSilently(accountId);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { _log.Debug(ex, "修改当前账户失败"); return false; }
    }

    [RelayCommand(CanExecute = nameof(IsGameOperationRunning))]
    private void CancelOperation() => _operation?.Cancel();

    private async Task RunGameOperationAsync(string progress, string failurePrefix, Func<PuloniaGameTaskLease, CancellationToken, Task> operation)
    {
        if (IsBusy || _coordinator is null) return;
        using var cancellation = new CancellationTokenSource();
        _operation = cancellation;
        IsBusy = true;
        IsGameOperationRunning = true;
        Message = progress;
        try
        {
            // 游戏操作在后台线程执行（截图识别与键鼠等待），结果消息回到界面线程显示
            await Task.Run(async () =>
            {
                using var lease = await _coordinator.AcquireAsync(cancellation.Token);
                await operation(lease, cancellation.Token);
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { Message = "操作已取消。"; }
        catch (Exception ex) { Message = failurePrefix + ex.Message; }
        finally
        {
            _operation = null;
            IsGameOperationRunning = false;
            IsBusy = false;
        }
    }

    private static string DescribeOccupant(PuloniaLockOwner occupant)
        => string.IsNullOrEmpty(occupant.InstanceKey) ? "另一个实例"
            : PuloniaInstanceKey.Describe(occupant.InstanceKey) + (occupant.RunId is { } runId ? $"（运行 {runId}）" : "");

    private static Task<DialogResult> ShowAsync(string message, string title, DialogKind kind, DialogButtons buttons)
        => AvaloniaPickers.MainWindow is { } owner ? FluentMessageBox.ShowAsync(owner, message, title, kind, buttons) : Task.FromResult(DialogResult.None);

    // —— 状态通知 ——

    private void OnAccountPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PuloniaAccountItemViewModel.IsDirty) or null) NotifyDirtyChanged();
        if (ReferenceEquals(sender, SelectedAccount))
        {
            if (e.PropertyName is nameof(PuloniaAccountItemViewModel.Uid) or null) OnPropertyChanged(nameof(TestSwitchHint));
            if (e.PropertyName is nameof(PuloniaAccountItemViewModel.ServerTimeZone) or null) OnPropertyChanged(nameof(SelectedServerTimeZone));
            if (e.PropertyName is nameof(PuloniaAccountItemViewModel.LoginMode) or null) OnPropertyChanged(nameof(SelectedLoginMode));
        }
    }

    private void OnAccountListChanged()
    {
        OnPropertyChanged(nameof(IsSingleAccount));
        OnPropertyChanged(nameof(ShowAccountList));
        OnPropertyChanged(nameof(HeaderText));
        NotifyDirtyChanged();
    }

    private void NotifyDirtyChanged()
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        NotifyCommands();
    }

    partial void OnSelectedAccountChanged(PuloniaAccountItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedAccount));
        OnPropertyChanged(nameof(SelectedServerTimeZone));
        OnPropertyChanged(nameof(SelectedLoginMode));
        NotifyCommands();
        _ = RefreshOverviewAsync();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEdit));
        NotifyCommands();
    }

    partial void OnIsGameOperationRunningChanged(bool value) => CancelOperationCommand.NotifyCanExecuteChanged();

    private void NotifyCommands()
    {
        AddAccountCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        RenameAccountCommand.NotifyCanExecuteChanged();
        DeleteAccountCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        DiscardChangesCommand.NotifyCanExecuteChanged();
        ReadUidCommand.NotifyCanExecuteChanged();
        ImportImageCommand.NotifyCanExecuteChanged();
        CaptureImageCommand.NotifyCanExecuteChanged();
        TestSwitchCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(TestSwitchHint));
    }
}
