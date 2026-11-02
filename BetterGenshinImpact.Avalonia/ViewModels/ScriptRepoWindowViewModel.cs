using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Pulonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// 脚本仓库窗口（对应 WPF ScriptRepoWindow）：更新渠道、仓库地址、Git 凭据、更新 / 重置仓库、从 zip 导入、打开仓库网页、一键更新订阅。
/// 提示文字与 Windows 版相同；Git 凭据保存在系统密钥环（Windows 版为凭据管理器）
/// </summary>
public sealed partial class ScriptRepoWindowViewModel : ObservableObject, IDisposable
{
    public const string CustomChannelName = "自定义";
    public const string CustomPlaceholderUrl = "https://example.com/custom-repo";

    private readonly IPuloniaCredentialVault? _vault;
    private string _savedUsername = "";
    private string _savedToken = "";
    private bool _disposed;

    public sealed record RepoChannel(string Name, string Url)
    {
        public override string ToString() => Name;
    }

    public ObservableCollection<RepoChannel> RepoChannels { get; }

    [ObservableProperty] private RepoChannel? _selectedRepoChannel;
    [ObservableProperty] private bool _isRepoUrlReadOnly = true;
    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private bool _isProgressIndeterminate;
    [ObservableProperty] private int _updateProgressValue;
    [ObservableProperty] private string _updateProgressText = "准备更新，请耐心等待...";
    [ObservableProperty] private string _gitUsername = "";
    [ObservableProperty] private string _gitToken = "";
    [ObservableProperty] private string _credentialMessage = "";

    public ScriptConfig Config { get; }

    /// <summary>打开仓库网页（由窗口提供：打开网页窗口并关闭本窗口）</summary>
    public Func<bool>? OpenRepoWebView { get; set; }

    /// <summary>仓库超过 30 天未更新时询问（立即更新 / 直接打开 / 关闭），参数为天数</summary>
    public Func<int, Task<RepoUpdatePromptResult>>? PromptRepoUpdate { get; set; }

    public enum RepoUpdatePromptResult { None, UpdateNow, OpenDirectly }

    public ScriptRepoWindowViewModel(ScriptConfig config, IPuloniaCredentialVault? vault)
    {
        Config = config;
        _vault = vault;
        RepoChannels = new ObservableCollection<RepoChannel>(ScriptRepoUpdater.RepoChannels.Select(kv => new RepoChannel(kv.Key, kv.Value)));
        RepoChannels.Add(new RepoChannel(CustomChannelName, CustomPlaceholderUrl));

        // 根据配置中保存的渠道名称恢复选择；找不到时选第一个并写回配置
        var saved = string.IsNullOrEmpty(Config.SelectedChannelName) ? null : RepoChannels.FirstOrDefault(c => c.Name == Config.SelectedChannelName);
        SelectedRepoChannel = saved ?? RepoChannels[0];
        if (saved is null)
        {
            Config.SelectedChannelName = SelectedRepoChannel.Name;
        }

        IsRepoUrlReadOnly = SelectedRepoChannel.Name != CustomChannelName;
        LoadCredentials();
        Config.PropertyChanged += OnConfigPropertyChanged;
        ScriptRepoUpdater.Instance.AutoUpdateStateChanged += OnAutoUpdateStateChanged;
        SyncAutoUpdateState();
    }

    /// <summary>当前仓库地址（自定义渠道为配置中的自定义地址）</summary>
    public string CurrentRepoUrl => SelectedRepoChannel is null ? "" : SelectedRepoChannel.Name == CustomChannelName ? Config.CustomRepoUrl : SelectedRepoChannel.Url;

    partial void OnSelectedRepoChannelChanged(RepoChannel? value)
    {
        if (value is null)
        {
            return;
        }

        Config.SelectedChannelName = value.Name;
        IsRepoUrlReadOnly = value.Name != CustomChannelName;
        OnPropertyChanged(nameof(CurrentRepoUrl));
    }

    private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScriptConfig.CustomRepoUrl))
        {
            OnPropertyChanged(nameof(CurrentRepoUrl));
        }

        if (e.PropertyName == nameof(ScriptConfig.SelectedChannelName))
        {
            // 其他窗口改了渠道时同步回选择器
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed) return;
                var channel = RepoChannels.FirstOrDefault(c => c.Name == Config.SelectedChannelName) ?? RepoChannels[0];
                if (!ReferenceEquals(channel, SelectedRepoChannel)) SelectedRepoChannel = channel;
            });
        }
    }

    private void OnAutoUpdateStateChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(SyncAutoUpdateState);

    private void SyncAutoUpdateState()
    {
        if (_disposed) return;
        if (ScriptRepoUpdater.Instance.IsAutoUpdating)
        {
            IsUpdating = true;
            IsProgressIndeterminate = true;
            UpdateProgressText = "后台正在自动更新订阅脚本...";
            UiHost.Dialogs.Toast(DialogKind.Information, "后台正在自动更新订阅脚本，请等待完成");
        }
        else
        {
            IsUpdating = false;
            IsProgressIndeterminate = false;
        }
    }

    private void LoadCredentials()
    {
        try
        {
            var credential = _vault?.Read(ScriptRepoUpdater.GitCredentialTarget);
            _savedUsername = GitUsername = credential?.UserName ?? "";
            _savedToken = GitToken = credential?.Password ?? "";
            CredentialMessage = "";
        }
        catch (Exception e)
        {
            CredentialMessage = "读取 Git 凭据失败：" + e.Message;
        }
    }

    /// <summary>保存 Git 凭据到系统密钥环（有改动时）；返回是否成功</summary>
    public bool SaveCredentials()
    {
        if (GitUsername == _savedUsername && GitToken == _savedToken)
        {
            return true;
        }

        try
        {
            if (_vault is null)
            {
                throw new InvalidOperationException("没有可用的系统密钥环");
            }

            if (GitUsername.Length == 0 && GitToken.Length == 0)
            {
                _vault.Delete(ScriptRepoUpdater.GitCredentialTarget);
            }
            else
            {
                _vault.Write(ScriptRepoUpdater.GitCredentialTarget, GitUsername, GitToken);
            }

            _savedUsername = GitUsername;
            _savedToken = GitToken;
            CredentialMessage = "";
            return true;
        }
        catch (Exception e)
        {
            CredentialMessage = "保存 Git 凭据失败：" + e.Message;
            return false;
        }
    }

    [RelayCommand]
    private async Task UpdateRepo()
    {
        if (SelectedRepoChannel is null)
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, "请选择一个脚本仓库更新渠道。");
            return;
        }

        var repoUrl = CurrentRepoUrl;
        if (string.IsNullOrWhiteSpace(repoUrl))
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, "请输入自定义仓库URL。");
            return;
        }

        if (repoUrl == CustomPlaceholderUrl)
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, "请修改默认的自定义URL为有效的仓库地址。");
            return;
        }

        if (!Uri.TryCreate(repoUrl, UriKind.Absolute, out _))
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, "请输入有效的URL地址。");
            return;
        }

        SaveCredentials();
        try
        {
            UiHost.Dialogs.Toast(DialogKind.Information, "正在更新脚本仓库，请耐心等待...");
            IsUpdating = true;
            IsProgressIndeterminate = true;
            UpdateProgressValue = 0;
            UpdateProgressText = "准备更新，请耐心等待...";

            var (_, updated) = await Task.Run(() => ScriptRepoUpdater.Instance.UpdateCenterRepoByGit(repoUrl,
                (path, steps, totalSteps) => Dispatcher.UIThread.Post(() =>
                {
                    IsProgressIndeterminate = false;
                    UpdateProgressValue = (int)(totalSteps > 0 ? Math.Min(100, (double)steps / totalSteps * 100) : 0);
                    UpdateProgressText = $"{path}";
                })));
            UiHost.Dialogs.Toast(DialogKind.Success, updated ? "脚本仓库更新成功，有新内容" : "脚本仓库已是最新");
            LastResult = updated ? "updated" : "latest";
        }
        catch (Exception ex)
        {
            LastResult = "error:" + ex.Message;
            await UiHost.Dialogs.ErrorAsync($"更新失败，可尝试重置仓库后重新更新。失败原因：{ex.Message}");
        }
        finally
        {
            IsUpdating = false;
            IsProgressIndeterminate = false;
        }
    }

    /// <summary>最近一次操作的结果（自测用）</summary>
    public string LastResult { get; private set; } = "";

    [RelayCommand]
    private async Task OpenLocalScriptRepo()
    {
        if (await CheckAndPromptRepoUpdate())
        {
            Config.ScriptRepoHintDotVisible = false;
            OpenRepoWebView?.Invoke();
        }
    }

    /// <summary>仓库超过 30 天未更新时提示；返回是否继续打开</summary>
    private async Task<bool> CheckAndPromptRepoUpdate()
    {
        TimeSpan timeSinceUpdate;
        try
        {
            if (!Directory.Exists(ScriptRepoUpdater.CenterRepoPath))
            {
                return true;
            }

            var repoJsonPath = Directory.GetFiles(ScriptRepoUpdater.CenterRepoPath, "repo.json", SearchOption.AllDirectories).FirstOrDefault();
            if (repoJsonPath == null || !File.Exists(repoJsonPath))
            {
                return true;
            }

            timeSinceUpdate = DateTime.Now - new FileInfo(repoJsonPath).LastWriteTime;
            if (timeSinceUpdate.TotalDays <= 30)
            {
                return true;
            }
        }
        catch
        {
            return true;
        }

        var result = PromptRepoUpdate is null ? RepoUpdatePromptResult.OpenDirectly : await PromptRepoUpdate((int)timeSinceUpdate.TotalDays);
        switch (result)
        {
            case RepoUpdatePromptResult.UpdateNow:
                await UpdateRepo();
                return false;
            case RepoUpdatePromptResult.OpenDirectly:
                return true;
            default:
                return false;
        }
    }

    [RelayCommand]
    private async Task ResetRepo()
    {
        if (IsUpdating)
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, "请等待当前更新完成后再进行重置操作。");
            return;
        }

        var result = await UiHost.Dialogs.ShowAsync("确定要重置脚本仓库吗？无法正常更新时候可以使用本功能，重置后请重新更新脚本仓库。",
            "确认重置", DialogKind.Warning, DialogButtons.YesNo);
        if (result != DialogResult.Yes)
        {
            return;
        }

        try
        {
            await ScriptRepoUpdater.Instance.ResetCurrentRepoAsync();
            LastResult = "reset";
            UiHost.Dialogs.Toast(DialogKind.Success, "脚本仓库已重置，请重新更新脚本仓库。");
        }
        catch (Exception ex)
        {
            LastResult = "error:" + ex.Message;
            UiHost.Dialogs.Toast(DialogKind.Error, $"重置失败: {ex.Message}");
        }
    }

    /// <summary>选择 zip 文件（由窗口提供文件选择器；自测替换）</summary>
    public Func<Task<string?>>? PickZip { get; set; }

    [RelayCommand]
    private async Task ImportLocalScriptsRepoZip()
    {
        if (IsUpdating)
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, "请等待当前操作完成后再进行导入。");
            return;
        }

        try
        {
            var file = PickZip is null ? null : await PickZip();
            if (file is null)
            {
                return;
            }

            IsUpdating = true;
            IsProgressIndeterminate = false;
            UpdateProgressValue = 0;
            UpdateProgressText = "正在导入脚本仓库，请耐心等待...";
            await ScriptRepoUpdater.Instance.ImportLocalRepoZip(file, (progress, text) => Dispatcher.UIThread.Post(() =>
            {
                UpdateProgressValue = progress;
                UpdateProgressText = text;
            }));
            LastResult = "imported";
            UiHost.Dialogs.Toast(DialogKind.Success, "脚本仓库导入成功！");
        }
        catch (Exception ex)
        {
            LastResult = "error:" + ex.Message;
            UiHost.Dialogs.Toast(DialogKind.Error, $"导入失败: {ex.Message}");
        }
        finally
        {
            IsUpdating = false;
        }
    }

    [RelayCommand]
    private async Task UpdateSubscribedScripts()
    {
        if (IsUpdating)
        {
            UiHost.Dialogs.Toast(DialogKind.Warning, "请等待当前操作完成后再进行更新。");
            return;
        }

        try
        {
            IsUpdating = true;
            IsProgressIndeterminate = true;
            UpdateProgressValue = 0;
            UpdateProgressText = "正在更新订阅脚本...";
            await Task.Run(() => ScriptRepoUpdater.Instance.ManualUpdateSubscribedScripts());
            LastResult = "subscribed-updated";
        }
        catch (Exception ex)
        {
            LastResult = "error:" + ex.Message;
            UiHost.Dialogs.Toast(DialogKind.Error, $"更新订阅脚本失败: {ex.Message}");
        }
        finally
        {
            IsUpdating = false;
            IsProgressIndeterminate = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Config.PropertyChanged -= OnConfigPropertyChanged;
        ScriptRepoUpdater.Instance.AutoUpdateStateChanged -= OnAutoUpdateStateChanged;
    }
}
