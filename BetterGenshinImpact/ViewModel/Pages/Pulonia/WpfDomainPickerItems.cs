using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>WPF 的秘境选择框选项：级联选择框的 ICascadingItem（与字段模型迁入 Core 前相同）。</summary>
internal sealed class WpfDomainPickerItems : IPuloniaDomainPickerItems
{
    /// <summary>WPF 程序集加载时即替换默认实现，界面与单元测试都使用级联选项。</summary>
    [ModuleInitializer]
    internal static void Install() => PuloniaDomainPickerItems.Current = new WpfDomainPickerItems();

    public IReadOnlyList<object> ForPicker(string? picker) => TrainingGuideDomainOptions.ForPicker(picker);

    public IReadOnlyList<object> Empty => System.Array.Empty<ICascadingItem>();

    public object? Find(IReadOnlyList<object> items, string? name)
        => TrainingGuideDomainOptions.Find(items as IReadOnlyList<ICascadingItem> ?? items.OfType<ICascadingItem>().ToList(), name);

    public string? ValueOf(object? item) => (item as ICascadingItem)?.Tag as string;
}
