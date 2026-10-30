using System;
using System.Globalization;
using System.Windows.Data;

namespace BetterGenshinImpact.View.Converters;

/// <summary>
/// 按数值在两个枚举类型之间转换（例如配置中的平台无关枚举 ImageStretchMode 与 WPF 的 Stretch，两者数值相同）
/// </summary>
public sealed class EnumValueConverter : IValueConverter
{
    public static EnumValueConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => ConvertEnum(value, targetType);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => ConvertEnum(value, targetType);

    private static object? ConvertEnum(object? value, Type targetType)
    {
        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (value is Enum && type.IsEnum)
        {
            return Enum.ToObject(type, System.Convert.ToInt64(value, CultureInfo.InvariantCulture));
        }

        return value;
    }
}
