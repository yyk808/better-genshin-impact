using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.GameTask.Common.Element.Assets;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 一条龙参数字段中秘境选择框的选项来源。选项对象由界面决定（WPF 为级联选择框的 ICascadingItem，其他宿主默认为秘境名字符串），
/// 字段模型只通过这里查找选中项、取回秘境名。
/// </summary>
public interface IPuloniaDomainPickerItems
{
    /// <summary>按选择框类型（见 TrainingGuideDomainNames 的 *Picker 常量）返回选项。</summary>
    IReadOnlyList<object> ForPicker(string? picker);

    /// <summary>非秘境字段使用的空选项（元素类型须与 <see cref="ForPicker"/> 一致，WPF 级联选择框按类型校验）。</summary>
    IReadOnlyList<object> Empty { get; }

    /// <summary>在选项中查找秘境名对应的选中项；空值或没有对应选项时为 null。</summary>
    object? Find(IReadOnlyList<object> items, string? name);

    /// <summary>选中项对应的秘境名；不是秘境选项时为 null。</summary>
    string? ValueOf(object? item);
}

/// <summary>秘境选择框选项来源的宿主设置点（WPF 在程序集加载时换成级联选项实现）。</summary>
public static class PuloniaDomainPickerItems
{
    public static IPuloniaDomainPickerItems Current { get; set; } = new NameItems();

    /// <summary>默认实现：选项为 tp.json 中的秘境名。</summary>
    private sealed class NameItems : IPuloniaDomainPickerItems
    {
        private IReadOnlyList<object>? _names;

        public IReadOnlyList<object> ForPicker(string? picker) => _names ??= Load();

        public IReadOnlyList<object> Empty => Array.Empty<string>();

        public object? Find(IReadOnlyList<object> items, string? name)
            => string.IsNullOrWhiteSpace(name) ? null : items.FirstOrDefault(item => item as string == name);

        public string? ValueOf(object? item) => item as string;

        private static IReadOnlyList<object> Load()
        {
            try
            {
                return MapLazyAssets.Get().DomainNameList.Cast<object>().ToList();
            }
            catch (Exception)
            {
                return [];
            }
        }
    }
}
