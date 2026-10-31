using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>将库存快照转换为展示行，不读写存储或管理界面选中状态。</summary>
internal sealed class AccountRecordsInventoryRowFactory(InventoryCatalog catalog, InventoryIconCache icons)
{
    /// <summary>在后台准备行快照，避免事件分组占用 UI 线程；行数据由 Core 的 InventoryRowBuilder 生成。</summary>
    public Task<AccountRecordsInventoryRow[]> BuildAsync(InventoryWorkspace workspace) => Task.Run(() =>
        InventoryRowBuilder.Build(workspace, catalog).Select(Create).ToArray());

    /// <summary>将 Core 行数据映射为 WPF 行模型，图像按需从缓存加载。</summary>
    private AccountRecordsInventoryRow Create(InventoryRowSnapshot row)
    {
        var name = row.Name;
        return new AccountRecordsInventoryRow
        {
            LastScanAttemptId = row.LastScanAttemptId, Event = row.Event, PendingDelta = row.PendingDelta, StatusDetails = row.StatusDetails,
            ErrorCount = row.ErrorCount, Name = name, ItemNameSortKey = row.ItemNameSortKey, Prior = row.Prior, Category = row.Category,
            Quantity = row.Quantity, RecordedAt = row.RecordedAt, Expirations = row.Expirations, ExpirationsEstimated = row.ExpirationsEstimated,
            SourceReference = row.SourceReference, SourceName = row.SourceName,
            IconLoader = row.UseImageCache ? () => icons.Load(name) : null, Error = row.Error, Recent = row.Recent,
            CanBatchArchive = row.CanBatchArchive
        };
    }
}
