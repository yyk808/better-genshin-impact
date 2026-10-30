using System;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.GameTask.Common.Reward;
using BetterGenshinImpact.Platform;

namespace BetterGenshinImpact.Core.UnitTest.Input;

/// <summary>
/// WinFormsKeys / WinFormsMouseButtons 是 WinForms 枚举的逐项副本，脚本与键鼠宏依赖其名称格式。
/// 期望值为 Windows 上 System.Windows.Forms.Keys / MouseButtons 的 ToString / Enum.Parse 结果。
/// 同值多名的项（如 Return / Enter）取哪个名称由运行时决定，副本与原枚举元数据顺序相同，因此结果一致，这里不对其断言
/// </summary>
public class WinFormsKeysTests
{
    [Theory]
    [InlineData(GameKey.VK_A, KeyModifiers.Shift, "A, Shift")]
    [InlineData(GameKey.VK_A, KeyModifiers.None, "A")]
    [InlineData(GameKey.VK_F1, KeyModifiers.Control | KeyModifiers.Alt, "F1, Control, Alt")]
    [InlineData(GameKey.VK_LCONTROL, KeyModifiers.Control, "LControlKey, Control")]
    [InlineData(GameKey.VK_SPACE, KeyModifiers.None, "Space")]
    [InlineData(GameKey.VK_0, KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Alt, "D0, Shift, Control, Alt")]
    public void KeyData_FormatsLikeWinForms(GameKey key, KeyModifiers modifiers, string expected)
    {
        Assert.Equal(expected, WinFormsKeyNames.ToKeyData(key, modifiers).ToString());
    }

    [Theory]
    [InlineData((ushort)0x10, "ShiftKey")]
    [InlineData((ushort)0x12, "Menu")]
    [InlineData((ushort)0x5B, "LWin")]
    [InlineData((ushort)0xC0, "Oemtilde")]
    public void KeyCode_FormatsLikeWinForms(ushort vk, string expected)
    {
        Assert.Equal(expected, ((WinFormsKeys)vk).ToString());
    }

    [Theory]
    [InlineData("Left", 1048576)]
    [InlineData("Right", 2097152)]
    [InlineData("Middle", 4194304)]
    [InlineData("XButton1", 8388608)]
    [InlineData("XButton2", 16777216)]
    [InlineData("None", 0)]
    public void MouseButtons_ParseLikeWinForms(string name, int value)
    {
        Assert.Equal(value, (int)Enum.Parse<WinFormsMouseButtons>(name));
    }

    [Fact]
    public void TargetItemCatalog_ResourceEmbeddedInCore()
    {
        using var stream = typeof(TargetItemCatalog).Assembly.GetManifestResourceStream("BetterGenshinImpact.Resources.TargetItem.target-item-data.json");
        Assert.NotNull(stream);
        Assert.True(stream!.Length > 0);
    }
}
