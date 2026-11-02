using Avalonia.Controls;
using BetterGenshinImpact.Platform;
using Avalonia.Media;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.AccountRecords;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// 狛荷屋“更多”菜单（对应 WPF 库存页签的更多操作）：刷新、快照设置（自动日期快照、保留数量）、
/// 来源记录 / 事件保留数量、查看物品 / 补充 / 来源字典、图像缓存开关。提示、校验与字典位置在 Core AccountRecordsMaintenance
/// </summary>
public partial class AccountRecordsPageViewModel
{
    [ObservableProperty] private bool _snapshotsEnabled = true;
    [ObservableProperty] private bool _useImageCache = true;

    /// <summary>最近一次查看的字典标题与内容（自测读取）</summary>
    public string? LastDictionaryTitle { get; private set; }

    /// <summary>读取公共设置到菜单勾选状态</summary>
    [RelayCommand]
    public async Task LoadMaintenanceSettingsAsync()
    {
        try
        {
            var settings = await _store.GetSettingsAsync();
            SnapshotsEnabled = settings.SnapshotsEnabled;
            UseImageCache = settings.UseImageCache;
        }
        catch (Exception ex)
        {
            StatusText = AccountRecordsErrorMessages.Describe(ex);
        }
    }

    /// <summary>刷新当前 UID 的库存</summary>
    [RelayCommand]
    private Task RefreshInventory() => SelectUidAsync(SelectedUid);

    /// <summary>开关日期快照，不删除已有快照（与 WPF 相同）</summary>
    [RelayCommand]
    private async Task ToggleSnapshots()
    {
        try
        {
            var settings = await AccountRecordsInteractionGate.Instance.ExecuteManualAsync(() =>
                _store.UpdateSettingsAsync(s => s.SnapshotsEnabled = !s.SnapshotsEnabled));
            SnapshotsEnabled = settings.SnapshotsEnabled;
            StatusText = SnapshotsEnabled ? "已开启自动日期快照。" : "已关闭自动日期快照（已有快照保留）。";
        }
        catch (Exception ex) { await InfoAsync(AccountRecordsErrorMessages.Describe(ex), "快照设置", DialogKind.Error); }
    }

    /// <summary>图像缓存开关（所有子表共用；关闭只停用缓存，不删除文件）。Linux 版库存列表暂不显示物品图像，设置与 Windows 版共用</summary>
    [RelayCommand]
    private async Task ToggleImageCache()
    {
        try
        {
            var settings = await AccountRecordsInteractionGate.Instance.ExecuteManualAsync(() =>
                _store.UpdateSettingsAsync(s => s.UseImageCache = !s.UseImageCache));
            UseImageCache = settings.UseImageCache;
            StatusText = UseImageCache ? "已启用图像缓存。" : "已停用图像缓存（不删除文件）。";
        }
        catch (Exception ex) { await InfoAsync(AccountRecordsErrorMessages.Describe(ex), "图像缓存", DialogKind.Error); }
    }

    /// <summary>编辑有明确上限的保留数量：snapshots / recent / events（调小时确认并立即清理超出部分）</summary>
    [RelayCommand]
    private async Task SetRetention(string kind)
    {
        int previous;
        try { previous = AccountRecordsMaintenance.RetentionLimit(await _store.GetSettingsAsync(), kind); }
        catch (Exception ex)
        {
            await InfoAsync(AccountRecordsErrorMessages.Describe(ex), "读取保存设置", DialogKind.Error);
            return;
        }
        var value = await FluentMessageBox.PromptAsync(AccountRecordsMaintenance.RetentionPrompt(kind), "保存设置",
            previous.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (string.IsNullOrEmpty(value)) return;
        if (!AccountRecordsMaintenance.TryParseRetention(kind, value, out var count))
        {
            await InfoAsync(AccountRecordsMaintenance.RetentionInvalidMessage(kind), "保存设置");
            return;
        }
        if (count < previous && !await ConfirmAsync(AccountRecordsMaintenance.RetentionShrinkWarning(kind, previous, count), "减少保留数量")) return;
        try
        {
            await AccountRecordsInteractionGate.Instance.ExecuteManualAsync(() => _store.SetRetentionAsync(kind, count, previous));
            StatusText = $"保留数量已设为 {count}。";
            await SelectUidAsync(SelectedUid);
        }
        catch (Exception ex) { await InfoAsync(AccountRecordsErrorMessages.Describe(ex), "保存设置", DialogKind.Error); }
    }

    /// <summary>只读查看字典（物品字典 items、补充字典 user、来源字典 sources）</summary>
    [RelayCommand]
    private async Task ViewDictionary(string kind)
    {
        if (AccountRecordsMaintenance.DictionaryRelativePath(kind) is not { } relative) return;
        var path = Global.Absolute(relative);
        var title = AccountRecordsMaintenance.DictionaryTitle(kind);
        string text;
        try
        {
            text = File.Exists(path) ? await File.ReadAllTextAsync(path) : "（文件不存在）";
            if (text.Length > 200_000) text = text[..200_000] + "\n…（内容较长，只显示前 200000 个字符）";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            text = "读取失败：" + ex.Message;
        }
        LastDictionaryTitle = title;
        var owner = AvaloniaPickers.MainWindow;
        if (owner is null) return;
        var content = new StackPanel { Spacing = 6, MinWidth = 560 };
        content.Children.Add(new TextBlock { Text = path, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 12 });
        content.Children.Add(new TextBox
        {
            Name = "DictionaryTextBox", Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            MaxHeight = 420, FontFamily = new FontFamily("monospace"),
        });
        var dialog = new FAContentDialog { Title = title, Content = content, CloseButtonText = "关闭" };
        await FluentMessageBox.ShowCustomAsync(dialog, owner);
        await SelectUidAsync(SelectedUid);
    }
}
