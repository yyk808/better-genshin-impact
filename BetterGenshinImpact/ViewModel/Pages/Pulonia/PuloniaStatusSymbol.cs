using System;
using BetterGenshinImpact.Pulonia.Presentation;
using BetterGenshinImpact.Pulonia.Models;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// Core <see cref="PuloniaTaskHistoryText.CategoryIcon"/> 按名称给出的状态图标转为 WPF-UI 图标（Core 不引用 WPF-UI）。
/// </summary>
internal static class PuloniaStatusSymbol
{
    public static SymbolRegular Parse(string name) =>
        Enum.TryParse<SymbolRegular>(name, out var symbol) ? symbol : SymbolRegular.SubtractCircle24;

    /// <summary>着色分类对应的状态图标；中性状态使用 <paramref name="neutral"/>。</summary>
    public static SymbolRegular CategoryIcon(PuloniaTaskRunStatusCategory category, SymbolRegular neutral)
        => Parse(PuloniaTaskHistoryText.CategoryIcon(category, neutral.ToString()));
}
