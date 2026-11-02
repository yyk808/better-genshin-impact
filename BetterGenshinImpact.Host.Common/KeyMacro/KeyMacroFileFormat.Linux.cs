using BetterGenshinImpact.Platform;

namespace BetterGenshinImpact.GameTask.KeyMacro;

/// <summary>
/// KeyMacroFileFormat 的 Linux 部分：快捷键按 Core 的 <see cref="HotkeyText"/>（与 WPF Model.HotKey 相同的文本格式）规范化
/// </summary>
public static partial class KeyMacroFileFormat
{
    private static partial string NormalizeHotkey(string value)
    {
        try
        {
            var hotKey = HotkeyText.Parse(value);
            return hotKey.IsEmpty ? string.Empty : hotKey.ToString();
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }
}
