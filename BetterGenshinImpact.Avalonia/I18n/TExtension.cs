using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using BetterGenshinImpact.Service.I18n;

namespace BetterGenshinImpact.Avalonia.I18n;

/// <summary>
/// Avalonia 版：与 WPF <c>{i18n:T 中文键}</c> 相同，中文原文即资源键。
/// 例：Header="{i18n:T '软件UI语言'}"。语言切换时随 <see cref="I18nService.Revision"/> 刷新。
/// </summary>
public sealed class TExtension : MarkupExtension
{
    private static readonly IValueConverter Converter = new RevisionToTranslationConverter();

    public TExtension()
    {
    }

    public TExtension(string key)
    {
        Key = key;
    }

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // 设计时 / 无 Key：直接返回原文，避免绑定空路径
        if (string.IsNullOrEmpty(Key))
        {
            return string.Empty;
        }

        return new Binding(nameof(I18nService.Revision))
        {
            Source = I18nService.Instance,
            Mode = BindingMode.OneWay,
            Converter = Converter,
            ConverterParameter = Key,
        };
    }

    private sealed class RevisionToTranslationConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => I18nService.Instance.Translate(parameter as string ?? string.Empty);

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
