using BetterGenshinImpact.GameTask;

namespace BetterGenshinImpact.Core.UnitTest.GameTask;

public class GenshinGameProcessTests
{
    [Fact]
    public void CandidateNames_DefaultsWhenNoInstallPath()
    {
        Assert.Equal(GenshinGameProcess.DefaultProcessNames, GenshinGameProcess.CandidateProcessNames(null));
        Assert.Equal(GenshinGameProcess.DefaultProcessNames, GenshinGameProcess.CandidateProcessNames(""));
    }

    [Theory]
    [InlineData(@"D:\Games\Genshin Impact\Genshin Impact Game\YuanShen.exe", 4)]
    [InlineData(@"D:\Games\Custom\MyGenshin.exe", 5)]
    [InlineData("/home/u/.wine/drive_c/Genshin/MyGenshin.exe", 5)]
    public void CandidateNames_AddsCustomExeName(string installPath, int expectedCount)
    {
        var names = GenshinGameProcess.CandidateProcessNames(installPath);
        Assert.Equal(expectedCount, names.Count);
        if (expectedCount == 5)
        {
            Assert.Equal("MyGenshin", names[^1]);
        }
    }

    [Theory]
    [InlineData(null, false, "")]
    [InlineData("  -foo  ", false, "-foo")]
    [InlineData("-popupwindow -screen-width 1280", false, "-popupwindow -screen-width 1280")]
    [InlineData(null, true, "-popupwindow -screen-width 1920 -screen-height 1080")]
    [InlineData("-popupwindow -screen-width 1280 -screen-height=720 -foo", true, "-foo -popupwindow -screen-width 1920 -screen-height 1080")]
    [InlineData("-screen-width \"1280\" -bar", true, "-bar -popupwindow -screen-width 1920 -screen-height 1080")]
    public void BuildStartArguments(string? configured, bool childSession, string expected)
    {
        Assert.Equal(expected, GenshinGameProcess.BuildStartArguments(configured, childSession));
    }
}
