using System.Collections.ObjectModel;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// 运行账户（对应 WPF 任务计划页与新一条龙的“运行账户”）：跟随当前账户（不切换），或指定填写了 UID 的账户——
/// 运行前由 Core 切换到该账户并核验 UID（Linux 版支持本机 X11 / Wayland 下 Wine 运行的 YuanShen.exe）。
/// 账户目录只有一个账户且计划跟随当前账户时不显示，规则与 WPF 相同（PuloniaRunAccountText）
/// </summary>
public partial class TaskPlanPageViewModel
{
    private List<PuloniaGenshinAccountProfile> _runAccounts = [];

    /// <summary>运行账户选项：首项“跟随当前账户（不切换）”，其余为填写了 UID 的账户</summary>
    public ObservableCollection<KeyValuePair<string, string>> RunAccountOptions { get; } = [];

    /// <summary>当前计划的运行账户选项（ComboBox 绑定）；修改立即保存计划</summary>
    public KeyValuePair<string, string>? SelectedRunAccount
    {
        get
        {
            var key = SelectedPlan?.Plan.AccountId ?? PuloniaRunAccountText.FollowCurrentKey;
            return RunAccountOptions.FirstOrDefault(o => o.Key == key) is { Value: not null } option ? option : null;
        }
        set
        {
            // 下拉框重建选项时会写回空值，不能据此把指定账户改成跟随当前账户
            if (value is not { } option || SelectedPlan is not { } item) return;
            var accountId = option.Key.Length == 0 ? null : option.Key;
            if (accountId == item.Plan.AccountId) return;
            if (accountId is not null && !PuloniaRunAccountText.CanSpecify(accountId, _runAccounts)) return;
            item.Plan.AccountId = accountId;
            _ = SaveRunAccountAsync(item, accountId is null ? "已改为跟随当前账户。" : $"运行账户已改为“{_runAccounts.First(a => a.Id == accountId).Name}”：运行前切换到该账户并核验 UID。");
        }
    }

    public bool ShowRunAccountEntry => PuloniaRunAccountText.ShouldShow(SelectedPlan?.Plan.AccountId, _runAccounts.Count);

    public string RunAccountText => PuloniaRunAccountText.Describe(SelectedPlan?.Plan.AccountId, _runAccounts);

    public string? RunAccountProblem => PuloniaRunAccountText.GetProblem(SelectedPlan?.Plan.AccountId, _runAccounts);

    public bool HasRunAccountProblem => RunAccountProblem is not null;

    /// <summary>计划指定了运行账户（新一条龙“切换账号”步骤显示为启用）</summary>
    public bool HasRunAccount => SelectedPlan?.Plan.AccountId is not null;

    private async Task SaveRunAccountAsync(TaskPlanItem item, string message)
    {
        await SaveAsync(item, message);
        NotifyRunAccountChanged();
    }

    /// <summary>读取账户目录，重建运行账户选项</summary>
    public async Task RefreshRunAccountsAsync()
    {
        if (AccountService is not { } accounts) return;
        try
        {
            var settings = await accounts.LoadAccountsAsync();
            _runAccounts = settings.Accounts.ToList();
            RunAccountOptions.Clear();
            foreach (var option in PuloniaRunAccountText.BuildOptions(_runAccounts)) RunAccountOptions.Add(option);
        }
        catch (Exception e)
        {
            _log.Warning(e, "读取账户目录失败");
        }
        NotifyRunAccountChanged();
    }

    private void NotifyRunAccountChanged()
    {
        OnPropertyChanged(nameof(SelectedRunAccount));
        OnPropertyChanged(nameof(ShowRunAccountEntry));
        OnPropertyChanged(nameof(RunAccountText));
        OnPropertyChanged(nameof(RunAccountProblem));
        OnPropertyChanged(nameof(HasRunAccountProblem));
        OnPropertyChanged(nameof(HasRunAccount));
    }

    /// <summary>选中指定计划（账户页“指定了该账户的计划”跳转）；页面尚未读取时先读取</summary>
    public async Task SelectPlanAsync(string planId)
    {
        if (!_loaded) await ReloadAsync();
        if (Plans.FirstOrDefault(p => p.Plan.Id == planId) is { } item) SelectedPlan = item;
    }
}
