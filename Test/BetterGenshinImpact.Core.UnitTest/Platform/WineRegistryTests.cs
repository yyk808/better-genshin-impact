using System.Text;

namespace BetterGenshinImpact.Core.UnitTest.Platform;

/// <summary>
/// TestData/wine-user.reg 由 Wine 11 的 <c>reg add</c> 实际写出（键名从测试用路径改成了 miHoYo）
/// </summary>
public class WineRegistryTests
{
    private static string DataFile => Path.Combine(AppContext.BaseDirectory, "TestData", "wine-user.reg");

    private static IRegistryKey OpenCn()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bgi-wine-reg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.Copy(DataFile, Path.Combine(dir, "user.reg"));
        var reg = new WinePrefixUserRegistry(dir);
        return reg.OpenSubKey(@"Software\miHoYo\原神") ?? throw new Xunit.Sdk.XunitException("key not found");
    }

    [Fact]
    public void ReadsChineseKeyNameAndValueOrder()
    {
        using var key = OpenCn();
        Assert.Equal(new[] { "", "E", "GENERAL_DATA_h2389025596", "M", "Neg", "Q", "S", "Screenmanager Resolution Width_h182942802" },
            key.GetValueNames());
    }

    [Fact]
    public void ReadsValueTypesLikeMicrosoftWin32Registry()
    {
        using var key = OpenCn();
        Assert.Equal("default", key.GetValue(""));
        Assert.Equal(1920, key.GetValue("Screenmanager Resolution Width_h182942802"));
        Assert.Equal(-1, key.GetValue("Neg"));
        Assert.Equal(0x123456789L, key.GetValue("q")); // 名称不区分大小写
        Assert.Equal("a \"quoted\" \\back 中文 tab\tend", key.GetValue("S"));
        Assert.Equal("%SystemRoot%\\x", key.GetValue("E"));
        Assert.Equal(new[] { "one", "two", "三" }, key.GetValue("M"));
        Assert.Null(key.GetValue("missing"));
    }

    [Fact]
    public void ReadsMultiLineBinaryGeneralData()
    {
        using var key = OpenCn();
        var bytes = Assert.IsType<byte[]>(key.GetValue("GENERAL_DATA_h2389025596"));
        Assert.Equal(0, bytes[^1]);
        var json = Encoding.UTF8.GetString(bytes, 0, bytes.Length - 1);
        Assert.StartsWith("{\"deviceUUID\":\"x\"", json);
        Assert.EndsWith(new string('z', 60) + "\"}", json);
    }

    [Fact]
    public void MissingKeyOrPrefix()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bgi-wine-reg-none-" + Guid.NewGuid().ToString("N"));
        Assert.Null(new WinePrefixUserRegistry(dir).OpenSubKey(@"Software\miHoYo\原神"));
        var file = WineRegistryFile.Load(DataFile);
        Assert.True(file.ContainsKey(@"Software\miHoYo\Genshin Impact"));
        Assert.True(file.ContainsKey(@"software/mihoyo/genshin impact"));
        Assert.False(file.ContainsKey(@"Software\miHoYo"));
        Assert.Equal(1, file.GetValue(@"Software\miHoYo\Genshin Impact", "WINDOWS_HDR_ON_h3132281285"));
    }

    [Theory]
    [InlineData(@"a\\b", @"a\b")]
    [InlineData(@"\x539f\x795e", "原神")]
    [InlineData(@"\x4e2dA", "中A")]
    [InlineData(@"tab\tnl\n", "tab\tnl\n")]
    [InlineData(@"\0\101", "\0A")]
    [InlineData(@"q\""", "q\"")]
    public void Unescape(string raw, string expected)
    {
        Assert.Equal(expected, WineRegistryFile.Unescape(raw));
    }

    [Fact]
    public void ParsesHexTypedStringsAndReloadsAfterChange()
    {
        const string reg = "WINE REGISTRY Version 2\n\n[Software\\\\T] 1\n" +
                           "\"sz\"=hex(2):25,00,41,00,25,00,00,00\n" +
                           "\"dw\"=hex(4):02,01,00,00\n" +
                           "\"bin\"=hex:01,02,\\\n  03\n";
        var file = WineRegistryFile.Parse(new StringReader(reg));
        Assert.Equal("%A%", file.GetValue(@"Software\T", "sz"));
        Assert.Equal(0x102, file.GetValue(@"Software\T", "dw"));
        Assert.Equal(new byte[] { 1, 2, 3 }, file.GetValue(@"Software\T", "bin"));

        var dir = Path.Combine(Path.GetTempPath(), "bgi-wine-reg-reload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "user.reg");
        File.WriteAllText(path, reg);
        var registry = new WinePrefixUserRegistry(dir);
        Assert.NotNull(registry.OpenSubKey(@"Software\T"));
        File.WriteAllText(path, reg.Replace("Software\\\\T", "Software\\\\U"));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));
        Assert.Null(registry.OpenSubKey(@"Software\T"));
        Assert.NotNull(registry.OpenSubKey(@"Software\U"));
    }
}
