using System;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 运行预览中的一行：序号、名称、类型、预测结果与原因（WPF 预览窗口与 Avalonia 预览对话框共用）。
/// </summary>
public sealed class PuloniaTaskRunPreviewRow
{
    /// <summary>序号（从 1 开始）。</summary>
    public int Index { get; }

    /// <summary>节点名称。</summary>
    public string Name { get; }

    /// <summary>任务类型显示名。</summary>
    public string TypeText { get; }

    /// <summary>预测结果，如“执行”“跳过 · CD 中”。</summary>
    public string OutcomeText { get; }

    /// <summary>结果说明。</summary>
    public string DetailText { get; }

    /// <summary>会因 CD、一次性完成或本次重复而跳过。</summary>
    public bool IsSkipped { get; }

    /// <summary>会执行但未识别刷新规则。</summary>
    public bool IsCaution { get; }

    /// <summary>节点自身或上级已关闭。</summary>
    public bool IsDisabled { get; }

    /// <summary>可清除的完成记录（预览账户与路线身份）；没有记录时为空。</summary>
    public (string AccountId, string Route)? CooldownKey { get; }

    /// <param name="item">预测结果。</param>
    /// <param name="index">序号。</param>
    /// <param name="typeText">任务类型显示名。</param>
    /// <param name="accountId">预览读取完成记录的账户 ID。</param>
    /// <param name="now">当前时间。</param>
    public PuloniaTaskRunPreviewRow(PuloniaTaskRunPreviewItem item, int index, string typeText, string accountId, DateTimeOffset now)
    {
        Index = index;
        Name = item.Name;
        TypeText = typeText;
        IsDisabled = item.Outcome == PuloniaTaskPreviewOutcome.Disabled;
        IsSkipped = item.Outcome is PuloniaTaskPreviewOutcome.CoolingDown or PuloniaTaskPreviewOutcome.Completed
            or PuloniaTaskPreviewOutcome.RepeatedInRun;
        IsCaution = item.Outcome == PuloniaTaskPreviewOutcome.Execute
                    && item.Cooldown?.State == PuloniaRouteCooldownState.Unmatched;
        if (item.Rule is { } refreshRule && item.Cooldown?.Completion is not null)
            CooldownKey = (accountId, refreshRule.FileIdentity);
        var rule = item.Rule is { } p ? $"{p.MaterialName ?? "未识别"} · {p.Description}" : string.Empty;
        (OutcomeText, DetailText) = item.Outcome switch
        {
            PuloniaTaskPreviewOutcome.Disabled => ("已关闭", "节点自身或上级分组已关闭。"),
            PuloniaTaskPreviewOutcome.CoolingDown when item.Cooldown?.NextEligibleAt is { } next =>
                ("跳过 · CD 中", $"下次可执行：{PuloniaCooldownDisplay.Next(next, now)} · {rule}"),
            PuloniaTaskPreviewOutcome.Completed => ("跳过 · 已完成", "一次性路线已成功执行，不再自动执行。"),
            PuloniaTaskPreviewOutcome.RepeatedInRun =>
                ("跳过 · 本次重复", $"与“{item.RepeatedTaskName}”是同一路线文件，前一项成功后跳过。"),
            _ when item.Rule is null => ("执行", string.Empty),
            _ when IsCaution => ("执行", "未识别刷新规则：成功后不会跳过，可在共享设置中选择自定义周期。"),
            _ when item.Cooldown?.LastSuccessAt is { } last => ("执行", $"上次成功 {PuloniaCooldownDisplay.Time(last)}，已可执行 · {rule}"),
            _ => ("执行", rule)
        };
    }

    /// <summary>预览汇总文字（与 WPF 预览窗口相同）。</summary>
    /// <param name="preview">预测结果。</param>
    /// <param name="scopeText">作用域显示名。</param>
    public static string Summary(PuloniaTaskRunPreview preview, string scopeText)
        => $"预计执行 {preview.ExecuteCount} 项 · 因路线 CD 跳过 {preview.CooldownSkipCount} 项"
           + (preview.UnmatchedCount > 0 ? $" · {preview.UnmatchedCount} 项路线未识别刷新规则（会执行）" : string.Empty)
           + $"（{scopeText}）";
}
