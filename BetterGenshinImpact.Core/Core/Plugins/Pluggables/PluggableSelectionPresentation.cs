using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.Core.Plugins.Pluggables;

/// <summary>“功能选择”下拉框的一项（WPF 与 Avalonia 插件页共用）</summary>
/// <param name="Id">实现标识</param>
/// <param name="Text">显示文本：实现名 · 来源插件（内置）</param>
/// <param name="IsUnavailable">保存的实现来自未启用的插件，仅作占位</param>
public sealed record PluggableImplOption(string Id, string Text, bool IsUnavailable);

/// <summary>一个可插拔能力的下拉选项、当前选择与回退提示</summary>
public sealed record PluggableSelectionView(IReadOnlyList<PluggableImplOption> Options, PluggableImplOption? Selected, bool HasFallback, string? FallbackText);

/// <summary>由注册表状态计算“功能选择”显示内容（原 WPF PluggableItemViewModel.Refresh 的逻辑）</summary>
public static class PluggableSelectionPresentation
{
    public static PluggableSelectionView Build(PluggableRegistry registry, string pluggableId)
    {
        var state = registry.GetSelection(pluggableId);
        var options = registry.GetImplementations(pluggableId)
            .Select(i => new PluggableImplOption(i.Id, $"{i.DisplayName} · {i.PluginName ?? "内置"}", false))
            .ToList();
        PluggableImplOption? selected;
        // 保存的实现来自未启用的插件：保留一项占位并选中，用户主动改选之前原来的选择不会丢失
        if (state is { SavedImplementationId: { } savedId, Saved: null })
        {
            selected = new PluggableImplOption(savedId, $"{savedId}（插件未启用）", true);
            options.Add(selected);
        }
        else
        {
            var selectedId = state.Saved?.Id ?? state.Effective.Id;
            selected = options.FirstOrDefault(o => o.Id == selectedId);
        }

        var fallbackText = state switch
        {
            { IsFallback: true, SavedFailed: true } => $"{state.Saved!.DisplayName} 加载失败，当前使用 {state.Effective.DisplayName}，原因见日志。",
            { IsFallback: true } => $"所选实现来自未启用的插件，当前使用 {state.Effective.DisplayName}。",
            _ => null,
        };
        return new PluggableSelectionView(options, selected, state.IsFallback, fallbackText);
    }
}
