using System;
using System.Globalization;
using BetterGenshinImpact.Service.AccountRecords.Model;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>
/// 狛荷屋“更多”菜单的界面无关规则：保留数量（来源记录 / 日期快照 / 事件）的提示、范围校验与减少确认文字，
/// 以及物品字典 / 补充字典 / 来源字典的位置与标题。WPF 与 Avalonia 共用
/// </summary>
public static class AccountRecordsMaintenance
{
    /// <summary>当前保留上限</summary>
    public static int RetentionLimit(AccountRecordsSettings settings, string kind) => InventoryStore.GetRetentionLimit(settings, kind);

    /// <summary>输入提示</summary>
    public static string RetentionPrompt(string kind) => kind switch
    {
        "events" => "每个 UID 保留多少条事件？（1000～50000，默认 5000）超限清理最旧的已结束记录；未解决事件保留，实际数量可能超过上限。",
        "snapshots" => "保留最近几个有记录的日期？（1～30）",
        _ => "每个物品保留几个最近来源？（1～10，含当前记录）",
    };

    /// <summary>解析输入；不在范围内的整数返回 false</summary>
    public static bool TryParseRetention(string kind, string? text, out int count)
    {
        var events = kind == "events";
        return int.TryParse(text, out count) && count >= (events ? 1000 : 1) && count <= (events ? 50000 : kind == "snapshots" ? 30 : 10);
    }

    /// <summary>输入无效时的说明</summary>
    public static string RetentionInvalidMessage(string kind) =>
        $"保留数量须为 {(kind == "events" ? "1000～50000" : kind == "snapshots" ? "1～30" : "1～10")} 范围内的整数，设置尚未保存。";

    /// <summary>调小保留数量前的确认文字</summary>
    public static string RetentionShrinkWarning(string kind, int previous, int count)
    {
        var records = kind == "events" ? "已结束事件" : kind == "snapshots" ? "日期快照" : "每个物品的来源记录";
        return $"将{records}保留数量从 {previous} 调小至 {count}，会立即清理所有 UID 中超出上限的旧记录。\n调大数量不会恢复已清理的记录。请先备份运行目录下的 User/AccountRecords 文件夹，再继续操作。\n\n是否继续？";
    }

    /// <summary>字典文件（相对运行目录，Windows 分隔符，由 Global.Absolute 转换）；未知类型为 null</summary>
    public static string? DictionaryRelativePath(string kind) => kind switch
    {
        "items" => @"Assets\Model\ItemV2\item.csv",
        "user" => @"User\AccountRecords\item-overrides.json",
        "sources" => @"User\AccountRecords\sources.json",
        _ => null,
    };

    /// <summary>字典标题</summary>
    public static string DictionaryTitle(string kind) => kind switch { "items" => "物品字典", "user" => "补充字典", _ => "来源字典" };

    /// <summary>清理全部图像缓存的确认文字</summary>
    public const string ClearIconCacheConfirm = "清理所有 UID 共用的物品图标？删除后需重新采集或手动上传，当前尚未接入图标自动重建。库存和事件记录不受影响。";
}
