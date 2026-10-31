using System;
using System.Collections.Generic;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Presentation;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 运行条目：展示逻辑在 Core 的 <see cref="PuloniaTaskRunItem"/>（与 Linux 版共用），这里只把状态图标名称转为 WPF-UI 的图标。
/// </summary>
public sealed class PuloniaTaskRunItemViewModel(PuloniaTaskRunView run, IReadOnlyDictionary<string, string>? typeNames = null)
    : PuloniaTaskRunItem(run, typeNames)
{
    /// <summary>
    /// 状态图标。
    /// </summary>
    public SymbolRegular StatusIcon => PuloniaStatusSymbol.Parse(StatusIconName);

    /// <inheritdoc />
    protected override PuloniaTaskNodeResultItem CreateNodeItem(PuloniaTaskNodeResult result, PuloniaTaskPreparedTask? task,
        string typeText, IEnumerable<PuloniaTaskConfirmedEffect> confirmedEffects)
        => new PuloniaTaskNodeResultViewModel(result, task, typeText, confirmedEffects);
}

/// <summary>
/// 节点尝试详情：展示逻辑在 Core 的 <see cref="PuloniaTaskNodeResultItem"/>，这里只提供 WPF-UI 图标。
/// </summary>
public sealed class PuloniaTaskNodeResultViewModel(PuloniaTaskNodeResult result, PuloniaTaskPreparedTask? task,
    string typeText, IEnumerable<PuloniaTaskConfirmedEffect> confirmedEffects)
    : PuloniaTaskNodeResultItem(result, task, typeText, confirmedEffects)
{
    /// <summary>
    /// 状态图标。
    /// </summary>
    public SymbolRegular StatusIcon => PuloniaStatusSymbol.Parse(StatusIconName);
}

/// <summary>
/// Core 中按名称给出的状态图标转为 WPF-UI 图标。
/// </summary>
internal static class PuloniaStatusSymbol
{
    public static SymbolRegular Parse(string name) =>
        Enum.TryParse<SymbolRegular>(name, out var symbol) ? symbol : SymbolRegular.SubtractCircle24;
}
