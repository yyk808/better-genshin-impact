using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using BetterGenshinImpact.Core.Plugins.Pluggables;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.Plugins;

/// <summary>
/// 一项可插拔能力的选择控件：可选实现、当前选择和回退提示。
/// 插件页“功能选择”的每一行使用它；功能自己的设置界面（如设置页的 OCR 配置）也可以使用，各处的选择通过注册表保持同步。
/// </summary>
public sealed partial class PluggableItemViewModel : ObservableObject
{
    /// <summary>
    /// 可插拔能力注册表。
    /// </summary>
    private readonly PluggableRegistry _registry;

    /// <summary>
    /// 正在按注册表的状态刷新界面，期间的选择变化不写回注册表。
    /// </summary>
    private bool _syncing;

    /// <summary>
    /// 可选实现列表。
    /// </summary>
    public ObservableCollection<PluggableImplOption> Options { get; } = [];

    /// <summary>
    /// 当前选中的实现。
    /// </summary>
    [ObservableProperty]
    private PluggableImplOption? _selectedOption;

    /// <summary>
    /// 回退提示：保存的实现不可用或加载失败时说明当前实际使用的实现。
    /// </summary>
    [ObservableProperty]
    private string? _fallbackText;

    /// <summary>
    /// 是否正在回退到默认实现。
    /// </summary>
    [ObservableProperty]
    private bool _hasFallback;

    /// <summary>
    /// 创建一行功能选择。
    /// </summary>
    /// <param name="registry">可插拔能力注册表。</param>
    /// <param name="descriptor">可插拔能力。</param>
    public PluggableItemViewModel(PluggableRegistry registry, PluggableDescriptor descriptor)
    {
        _registry = registry;
        Descriptor = descriptor;
        // 实现增减、选择变化（包括在其他界面改选）、创建失败时刷新；事件可能来自任意线程，切回界面线程处理。
        // 使用方都是单例 ViewModel，订阅随进程结束，不需要取消
        _registry.Changed += (_, e) =>
        {
            if (e.PluggableId == Descriptor.Id)
            {
                _ = Application.Current?.Dispatcher.InvokeAsync(Refresh);
            }
        };
        Refresh();
    }

    /// <summary>
    /// 可插拔能力。
    /// </summary>
    public PluggableDescriptor Descriptor { get; }

    /// <summary>
    /// 显示名称。
    /// </summary>
    public string DisplayName => Descriptor.DisplayName;

    /// <summary>
    /// 说明。
    /// </summary>
    public string Description => Descriptor.Description ?? string.Empty;

    /// <summary>
    /// 按注册表的当前状态重建选项和选择。在界面线程调用。
    /// </summary>
    public void Refresh()
    {
        // 选项、当前选择与回退提示由 Core 计算（Avalonia 插件页共用）
        var view = PluggableSelectionPresentation.Build(_registry, Descriptor.Id);
        _syncing = true;
        try
        {
            Options.Clear();
            foreach (var option in view.Options)
            {
                Options.Add(option);
            }

            SelectedOption = view.Selected;
            HasFallback = view.HasFallback;
            FallbackText = view.FallbackText;
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>
    /// 用户改选实现时保存选择，下一次调用即使用新实现。
    /// </summary>
    partial void OnSelectedOptionChanged(PluggableImplOption? value)
    {
        if (_syncing || value is null || value.IsUnavailable)
        {
            return;
        }

        // 注册表随后触发 Changed，各处的选择控件据此刷新回退提示
        _registry.Select(Descriptor.Id, value.Id);
    }
}
