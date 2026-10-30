namespace BetterGenshinImpact.Core.UnitTest.Platform;

public class HotkeyTextTests
{
    [Theory]
    [InlineData("F11", GameKey.VK_F11, KeyModifiers.None)]
    [InlineData("Ctrl + Shift + F11", GameKey.VK_F11, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData("Alt + D1", GameKey.VK_1, KeyModifiers.Alt)]
    [InlineData("Win + Ctrl + Oem3", GameKey.VK_OEM_3, KeyModifiers.Super | KeyModifiers.Control)]
    [InlineData("OemTilde", GameKey.VK_OEM_3, KeyModifiers.None)]
    [InlineData("Enter", GameKey.VK_RETURN, KeyModifiers.None)]
    [InlineData("NumPad5", GameKey.VK_NUMPAD5, KeyModifiers.None)]
    [InlineData("OemPlus", GameKey.VK_OEM_PLUS, KeyModifiers.None)]
    public void ParsesConfigText(string text, GameKey key, KeyModifiers mods)
    {
        var h = HotkeyText.Parse(text);
        Assert.Equal(key, h.Key);
        Assert.Equal(mods, h.Modifiers);
        Assert.False(h.IsMouseButton);
        Assert.Equal(new HotkeyGesture(key, mods), h.ToGesture());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("< None >")]
    public void EmptyTexts(string? text)
    {
        var h = HotkeyText.Parse(text);
        Assert.True(h.IsEmpty);
        Assert.Equal("< None >", h.ToString());
        Assert.Null(h.ToGesture());
    }

    [Theory]
    [InlineData("XButton1", GameKey.VK_XBUTTON1)]
    [InlineData("XButton2", GameKey.VK_XBUTTON2)]
    public void MouseSideButtons(string text, GameKey button)
    {
        var h = HotkeyText.Parse(text);
        Assert.True(h.IsMouseButton);
        Assert.Equal(button, h.MouseButton);
        Assert.Equal(text, h.ToString());
        Assert.Null(h.ToGesture());
    }

    [Fact]
    public void InvalidNameThrows()
    {
        Assert.Throws<FormatException>(() => HotkeyText.Parse("Ctrl + NotAKey"));
        // 与 Enum.Parse 一致：区分大小写
        Assert.Throws<FormatException>(() => HotkeyText.Parse("f11"));
    }

    [Theory]
    [InlineData("Ctrl + Shift + Alt + Win + F1")]
    [InlineData("Return")]
    [InlineData("Oem3")]
    [InlineData("Capital")]
    [InlineData("Next")]
    [InlineData("D0")]
    public void FormatsLikeWpfHotKey(string text)
    {
        Assert.Equal(text, HotkeyText.Parse(text).ToString());
    }

    [Fact]
    public void EveryWpfNameRoundTripsThroughVk()
    {
        foreach (var name in WpfKeyNames.AllNames)
        {
            Assert.True(WpfKeyNames.TryParse(name, out var vk), name);
            if (vk == GameKey.None) continue;
            // VK → 规范名 → VK 不变
            Assert.True(WpfKeyNames.TryParse(WpfKeyNames.ToName(vk), out var again), name);
            Assert.Equal(vk, again);
        }
    }

    [Fact]
    public void NumericEnumValueParsesLikeEnumParse()
    {
        // WPF Key.A == 44
        Assert.True(WpfKeyNames.TryParse("44", out var vk));
        Assert.Equal(GameKey.VK_A, vk);
    }
}
