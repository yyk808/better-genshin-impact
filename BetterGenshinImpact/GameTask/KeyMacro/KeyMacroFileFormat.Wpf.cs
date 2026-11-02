using BetterGenshinImpact.Model;
using System;

namespace BetterGenshinImpact.GameTask.KeyMacro;

/// <summary>
/// KeyMacroFileFormat 的 WPF 部分：快捷键按 WPF 的 <see cref="HotKey"/> 规范化
/// </summary>
public static partial class KeyMacroFileFormat
{
    /// <summary>
    /// 校验快捷键字符串：能解析时返回规范写法，不能解析时视为未设置
    /// </summary>
    private static partial string NormalizeHotkey(string value)
    {
        try
        {
            var hotKey = HotKey.FromString(value);
            return hotKey.IsEmpty ? string.Empty : hotKey.ToString();
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }
}
