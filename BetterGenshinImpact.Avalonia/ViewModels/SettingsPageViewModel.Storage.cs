using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Host.Common.Storage;
using BetterGenshinImpact.Infrastructure.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Avalonia.ViewModels;

// —— 存储空间（同 WPF 常规设置页的“存储空间”卡片与 StorageManagementWindow）——
public sealed partial class SettingsPageViewModel
{
    /// <summary>计入摘要的分组：程序管理的缓存、下载、网页数据和临时文件（同 WPF）</summary>
    private static readonly StorageGroup[] StorageSummaryGroups =
        [StorageGroup.Cache, StorageGroup.Download, StorageGroup.WebData, StorageGroup.Temp];

    /// <summary>摘要上次统计的时间，短时间内反复进入页面时不重复统计</summary>
    private DateTime _storageSummaryUpdatedAt = DateTime.MinValue;

    private bool _isStorageSummaryRefreshing;

    /// <summary>存储空间卡片的摘要</summary>
    [ObservableProperty]
    private string _storageSummaryText = "查看和清理缓存、下载、网页数据与临时文件";

    private IStorageService? StorageService => _host?.Services.GetService<IStorageService>();

    /// <summary>进入设置页时统计摘要；一分钟内重复进入沿用上次结果（同 WPF）</summary>
    public Task RefreshStorageSummaryAsync(bool force) => RefreshStorageSummaryCoreAsync(force);

    private async Task RefreshStorageSummaryCoreAsync(bool force)
    {
        if (StorageService is not { } storage || _isStorageSummaryRefreshing ||
            (!force && DateTime.UtcNow - _storageSummaryUpdatedAt < TimeSpan.FromMinutes(1)))
        {
            return;
        }

        _isStorageSummaryRefreshing = true;
        try
        {
            var areas = storage.GetAreas().Where(area => StorageSummaryGroups.Contains(area.Group)).ToList();
            var sizes = await Task.WhenAll(areas.Select(area => storage.MeasureAsync(area, CancellationToken.None)));
            StorageSummaryText = $"缓存、下载、网页数据和临时文件共占用 {ByteSizeFormatter.Format(sizes.Sum())}";
            _storageSummaryUpdatedAt = DateTime.UtcNow;
        }
        catch (Exception)
        {
            StorageSummaryText = "查看和清理缓存、下载、网页数据与临时文件";
        }
        finally
        {
            _isStorageSummaryRefreshing = false;
        }
    }

    /// <summary>创建存储空间窗口的 VM（悬浮小窗设置页也用）</summary>
    public static StorageManagementViewModel? CreateStorageManagement(BetterGenshinImpact.Host.Common.GameHost? host) =>
        host?.Services.GetService<IStorageService>() is { } storage
            ? new StorageManagementViewModel(storage, host.Config.StorageConfig,
                host.Services.GetRequiredService<ILoggerFactory>().CreateLogger<StorageManagementViewModel>())
            : null;

    /// <summary>打开存储空间窗口（模态），关闭后刷新摘要</summary>
    [RelayCommand]
    private async Task OpenStorageManagement()
    {
        await ShowStorageManagementAsync(_host);
        await RefreshStorageSummaryAsync(force: true);
    }

    /// <summary>打开存储空间窗口并等待关闭</summary>
    public static async Task ShowStorageManagementAsync(BetterGenshinImpact.Host.Common.GameHost? host)
    {
        if (CreateStorageManagement(host) is not { } vm) return;
        var window = new Views.StorageManagementWindow(vm);
        if (Services.AvaloniaPickers.MainWindow is { } owner) await window.ShowDialog(owner);
        else window.Show();
    }
}
