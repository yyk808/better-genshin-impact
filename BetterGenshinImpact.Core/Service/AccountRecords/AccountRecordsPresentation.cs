using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.AccountRecords.Model;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>账号记录页与界面框架无关的 UID 规则、清除范围说明与导出写入；WPF 与 Avalonia 共用。</summary>
public static class AccountRecordsAccounts
{
    /// <summary>新增 UID 不合法时的提示。</summary>
    public const string UidValidationMessage = "UID 必须为 9～10 位数字且首位非零。";

    /// <summary>UID 必须为 9～10 位 ASCII 数字且首位非零（与 InventoryStore 存储校验同一规则）；调用方负责去除首尾空白。</summary>
    public static bool IsValidUid(string uid) => InventoryStore.IsValidUid(uid);

    /// <summary>清除范围只接受 inventory 与 uid。</summary>
    public static bool IsClearScope(string? scope) => scope is "inventory" or "uid";

    /// <summary>清除确认框中的操作说明。</summary>
    public static string DescribeClearScope(string scope) => scope switch
    {
        "uid" => "删除该 UID 及其全部账号记录和快照",
        _ => "清空该 UID 的库存、异常和库存快照，但保留 UID"
    };

    /// <summary>清除确认框正文。</summary>
    public static string ClearConfirmText(string scope) => $"确定{DescribeClearScope(scope)}？此操作不可撤销。";

    /// <summary>重置为预设值的两步确认文本（标题，正文）。</summary>
    public static readonly (string Title, string Text) ResetConfirm1 = ("重置为预设值（1/2）",
        "将重置所有 UID 及其库存、异常、事件和快照，恢复补充字典、来源字典、Warning 规则及账号记录设置为预设值。预设 UID 列表为空。是否继续？");

    /// <summary>第二次确认。</summary>
    public static readonly (string Title, string Text) ResetConfirm2 = ("最终确认（2/2）",
        "再次确认：此操作影响所有账号，覆盖全部自定义字典和账号记录配置。原数据会保留在 AccountRecords 同级备份目录，但界面不提供撤销。确定执行重置？");

    /// <summary>导出完成提示。</summary>
    public const string ExportDoneText = "当前库存已导出。文件保留原记录时间与来源，不包含异常、快照或图标。";

    /// <summary>导出文件的默认文件名。</summary>
    public static string DefaultExportFileName(string uid, DateTime now) => $"AccountRecords-{uid}-{now:yyyyMMdd-HHmmss}.json";

    /// <summary>导出所选 UID 的当前库存：完整写入临时文件后替换目标，失败时删除临时文件。</summary>
    public static async Task ExportInventoryAsync(InventoryStore store, string uid, string path)
    {
        string? temporary = null;
        try
        {
            var package = await store.ExportInventoryAsync(uid);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(temporary, package.ToString(Newtonsoft.Json.Formatting.Indented), new UTF8Encoding(false));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (temporary != null && File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

/// <summary>库存表一行的展示数据，不含图像与界面状态；由 <see cref="InventoryRowBuilder"/> 生成。</summary>
public sealed class InventoryRowSnapshot
{
    /// <summary>最近一次可追溯上报批次。</summary>
    public string? LastScanAttemptId { get; init; }
    /// <summary>最高优先级的未归档事件。</summary>
    public InventoryEvent? Event { get; init; }
    /// <summary>累计待核实增量。</summary>
    public decimal? PendingDelta { get; init; }
    /// <summary>来源及变动摘要。</summary>
    public string StatusDetails { get; init; } = "";
    /// <summary>未归档错误事件数量。</summary>
    public int ErrorCount { get; init; }
    /// <summary>名称。</summary>
    public string Name { get; init; } = "";
    /// <summary>名称排序键。</summary>
    public string ItemNameSortKey { get; init; } = "";
    /// <summary>是否置顶。</summary>
    public bool Prior { get; init; }
    /// <summary>展示分类。</summary>
    public string Category { get; init; } = "";
    /// <summary>当前有效数量。</summary>
    public long? Quantity { get; init; }
    /// <summary>记录时间（UTC）。</summary>
    public DateTimeOffset? RecordedAt { get; init; }
    /// <summary>到期批次。</summary>
    public IReadOnlyList<InventoryExpiration>? Expirations { get; init; }
    /// <summary>到期批次是否为推算。</summary>
    public bool ExpirationsEstimated { get; init; }
    /// <summary>完整来源引用。</summary>
    public string SourceReference { get; init; } = "";
    /// <summary>展示来源。</summary>
    public string SourceName { get; init; } = "";
    /// <summary>是否需要加载图像缓存。</summary>
    public bool UseImageCache { get; init; }
    /// <summary>当前异常。</summary>
    public InventoryError? Error { get; init; }
    /// <summary>最近来源记录。</summary>
    public IReadOnlyList<InventoryRecord> Recent { get; init; } = [];
    /// <summary>可批量归档。</summary>
    public bool CanBatchArchive { get; set; }
    /// <summary>状态优先级。</summary>
    public int StateRank => InventoryRowFormatting.StateRank(Error != null, Event);
    /// <summary>数量显示。</summary>
    public string QuantityText => InventoryRowFormatting.QuantityText(Quantity, PendingDelta);
    /// <summary>时间显示（本地时区）。</summary>
    public string TimeText => InventoryRowFormatting.TimeText(RecordedAt);
    /// <summary>到期显示。</summary>
    public string ExpirationText => InventoryRowFormatting.ExpirationText(Expirations, ExpirationsEstimated);
    /// <summary>状态提示。</summary>
    public string StatusText => InventoryRowFormatting.StatusText(Event, Error, StatusDetails);
}

/// <summary>库存行的展示文本规则；WPF 行模型与 Avalonia 表格共用。</summary>
public static class InventoryRowFormatting
{
    /// <summary>状态优先级：红、橘、黄、绿、灰、无灯。</summary>
    public static int StateRank(bool hasError, InventoryEvent? @event) => hasError ? 0 : @event?.State switch { "Error" => 0, "Warning" => 1, "Pending" => 2, "Completed" => 3, "Planned" => 4, _ => 5 };

    /// <summary>增减量文本。</summary>
    public static string DeltaText(decimal? pendingDelta) => pendingDelta is { } value ? $"（{(value > 0 ? "+" : value == 0 ? "±" : "")}{value}）" : "";

    /// <summary>无确认库存但有变动时只显示增减量，不将未知库存视为零。</summary>
    public static string QuantityText(long? quantity, decimal? pendingDelta) => quantity?.ToString() ?? (pendingDelta.HasValue ? DeltaText(pendingDelta) : "未知");

    /// <summary>按当前系统时区展示时间。</summary>
    public static string TimeText(DateTimeOffset? recordedAt) => recordedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "未记录";

    /// <summary>状态两行提示。</summary>
    public static string StatusText(InventoryEvent? @event, InventoryError? error, string details) =>
        AccountRecordsErrorMessages.ForDisplay(@event?.Message ?? error?.Message ?? "") + "\n" + details;

    /// <summary>表格显示剩余天数，多批同时标注数量。</summary>
    public static string ExpirationText(IReadOnlyList<InventoryExpiration>? expirations, bool estimated) => expirations is not { Count: > 0 }
        ? estimated && expirations != null ? "无剩余批次（推算）" : "—"
        : string.Join("；", expirations.Select(item => $"{item.Quantity} 个：{Remaining(item.Time)}")) + (estimated ? "（推算）" : "");

    /// <summary>悬停逐行显示各批次数量和剩余天数。</summary>
    public static string ExpirationDetails(IReadOnlyList<InventoryExpiration>? expirations, bool estimated) => expirations is not { Count: > 0 }
        ? estimated && expirations != null ? "已知到期批次推算已消耗完，等待完整库存核实。" : "未提供过期信息"
        : string.Join("\n", expirations.Select(item => $"{item.Quantity} 个：{Remaining(item.Time)}")) + (estimated ? "\n包含待核实增量，按最早到期优先推算消耗。" : "");

    /// <summary>按实际时长显示完整天数，不足一天独立提示。</summary>
    public static string Remaining(DateTimeOffset time)
    {
        var remaining = time - DateTimeOffset.UtcNow;
        return remaining <= TimeSpan.Zero ? "已过期" : remaining.TotalDays < 1 ? "不足 1 天" : $"{Math.Floor(remaining.TotalDays)} 天";
    }
}

/// <summary>将库存快照转换为展示行数据，不读写存储；原 WPF AccountRecordsInventoryRowFactory 的逻辑。</summary>
public static class InventoryRowBuilder
{
    /// <summary>同步构建全部行；调用方可放到后台线程。</summary>
    public static InventoryRowSnapshot[] Build(InventoryWorkspace workspace, InventoryCatalog catalog)
    {
        // 每轮只扫描一次事件，建行时直接访问当前材料的分组。
        var eventsByName = workspace.Inventory.Events.Where(item => !item.Archived).ToLookup(item => item.Name, StringComparer.Ordinal);
        var rows = workspace.Inventory.Items.Keys.Union(workspace.Inventory.Errors.Keys, StringComparer.Ordinal)
        .Union(eventsByName.Select(group => group.Key), StringComparer.Ordinal).Select(name =>
        {
            workspace.Inventory.Items.TryGetValue(name, out var recent);
            workspace.Inventory.Errors.TryGetValue(name, out var error);
            var current = recent?.FirstOrDefault();
            var reference = current?.Source ?? error?.Source ?? "";
            workspace.Sources.TryGetValue(reference, out var source);
            workspace.Definitions.TryGetValue(name, out var category);
            var events = eventsByName[name].ToArray();
            var active = InventoryEventPolicy.SelectActive(events);
            var pending = events.Where(item => item.Kind == "Delta" && item.State == "Pending").ToArray();
            var changes = active?.Kind == "Delta" ? events.Where(item => item.Kind == "Delta" && item.State == active.State &&
                (active.State != "Completed" || item.VerificationId == active.VerificationId)).ToArray() : [];
            // 历史文件可能显式保存空来源；展示时按缺失来源处理，不让单个事件阻断整表。
            string Display(string? key) => key != null && workspace.Sources.TryGetValue(key, out var descriptor) && workspace.Settings.PreferReadableSource ? descriptor.DisplayName : key ?? "";
            var origins = changes.Select(item => Display(item.Source)).Distinct().ToArray();
            var firstOrigin = origins.FirstOrDefault() ?? Display(active?.Source ?? reference);
            if (firstOrigin.Length > 24) firstOrigin = firstOrigin[..24] + "…";
            var details = changes.Length > 0 ? $"变动来源：{firstOrigin}{(origins.Length > 1 ? $"，共 {origins.Length} 处" : "")}，累计变动：{changes.Sum(item => (decimal)item.Quantity):+0;-0;±0}" : $"核实来源：{firstOrigin}";
            if (active is { State: "Completed", Kind: "Delta" }) details += $"，累计推算与确认库存：{(active.VerifiedCorrect == true ? "一致" : active.VerifiedCorrect == false ? "不一致" : "未记录比对结果")}，核实来源：{Display(active.VerificationSource)}";
            if (active?.Kind == "CategoryWarning")
                details = $"上报来源：{Display(active.Source)}，分类采用本地值";
            if (active?.Kind == "ExpirationCheck")
                details = "已过期库存待检查；请重新上报到期批次或手动修订数量。";
            if (active?.Kind == "ThresholdWarning")
                details = "满足自定义规则；归档后本轮条件解除前不再提醒。";
            return new InventoryRowSnapshot
            {
                LastScanAttemptId = recent?.FirstOrDefault(record => !string.IsNullOrEmpty(record.AttemptId))?.AttemptId
                    ?? events.LastOrDefault(item => !string.IsNullOrEmpty(item.AttemptId))?.AttemptId,
                Event = active, PendingDelta = pending.Length > 0 ? pending.Sum(item => (decimal)item.Quantity) : null, StatusDetails = details,
                ErrorCount = events.Count(item => item.State == "Error"),
                Name = name, ItemNameSortKey = catalog.GetItemNameSortKey(name), Prior = workspace.Inventory.Prior.Contains(name),
                Category = string.IsNullOrEmpty(category) ? "未分类" : category,
                Quantity = current?.Quantity, RecordedAt = current?.RecordedAt,
                // 无总库存时仍展示增量中已知的到期批次；空记录仅用于批次重放，不作为确认数量。
                Expirations = InventoryStore.EstimateExpirations(current ?? new InventoryRecord(), pending),
                ExpirationsEstimated = pending.Length > 0,
                SourceReference = reference,
                SourceName = workspace.Settings.PreferReadableSource && !string.IsNullOrWhiteSpace(source?.DisplayName) ? source.DisplayName : reference,
                UseImageCache = workspace.Settings.UseImageCache, Error = error, Recent = recent ?? []
            };
        }).ToArray();
        // 批量入口与服务端使用同一资格规则；需求等未来类型不会与核实绿灯混合。
        var counts = rows.Where(row => row.Event?.State == "Completed").Select(row => row.Event!.Kind).Distinct()
            .ToDictionary(kind => kind, kind => InventoryEventPolicy.GetCompletedArchiveCandidates(workspace.Inventory, kind).Length);
        foreach (var row in rows)
            row.CanBatchArchive = row.StateRank == 3 && row.Event != null && counts.GetValueOrDefault(row.Event.Kind) >= 2;
        return rows;
    }
}
