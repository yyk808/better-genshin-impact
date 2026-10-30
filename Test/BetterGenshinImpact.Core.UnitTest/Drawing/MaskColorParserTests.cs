using BetterGenshinImpact.Core.Mask;

namespace BetterGenshinImpact.Core.UnitTest.Drawing;

public class MaskColorParserTests
{
    [Theory]
    [InlineData("#5DCC17", 255, 93, 204, 23)]
    [InlineData("5dcc17", 255, 93, 204, 23)]
    [InlineData(" #FF000080 ", 128, 255, 0, 0)]
    public void ParseRgbaHex_AlphaLast(string text, byte a, byte r, byte g, byte b)
    {
        Assert.Equal(MaskColor.FromArgb(a, r, g, b), MaskColorParser.ParseRgbaHex(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#FFF")]
    [InlineData("#GGGGGG")]
    [InlineData("Red")]
    public void ParseRgbaHex_Invalid_ReturnsNull(string? text)
    {
        Assert.Null(MaskColorParser.ParseRgbaHex(text));
    }
}
