using BetterGenshinImpact.Genshin.Settings;

namespace BetterGenshinImpact.Core.UnitTest.Genshin;

public class GenshinSettingsStoreTests
{
    private sealed class FakeRegistry(params string[] keys) : IUserRegistry
    {
        public IRegistryKey? OpenSubKey(string path) =>
            keys.Contains(path, StringComparer.OrdinalIgnoreCase) ? new Key(path) : null;

        private sealed class Key(string path) : IRegistryKey
        {
            public string Path => path;
            public IReadOnlyList<string> GetValueNames() => ["Screenmanager Resolution Width_h182942802", "GENERAL_DATA_h2389025596"];
            public object? GetValue(string name) => null;
            public void Dispose() { }
        }
    }

    private static GenshinSettingsStore Store(IUserRegistry reg, params string[] running) =>
        new(reg, name => running.Contains(name));

    [Fact]
    public void RunningProcessDecidesRegion()
    {
        var reg = new FakeRegistry(GenshinSettingsStore.ChineseRegistryPath, GenshinSettingsStore.GlobalRegistryPath);
        Assert.Equal(GenshinRegistryType.Global, Store(reg, "GenshinImpact").ResolveRegistryType(@"D:\x\YuanShen.exe"));
        Assert.Equal(GenshinRegistryType.Chinese, Store(reg, "YuanShen").ResolveRegistryType(null));
    }

    [Theory]
    [InlineData(@"D:\Genshin Impact Game\GenshinImpact.exe", GenshinRegistryType.Global)]
    [InlineData("/home/u/Games/genshin/drive_c/Genshin Impact Game/GenshinImpact.exe", GenshinRegistryType.Global)]
    [InlineData(@"D:\Genshin Impact Game\YuanShen.exe", GenshinRegistryType.Chinese)]
    [InlineData(null, GenshinRegistryType.Chinese)]
    public void ExecutablePathDecidesRegionWhenBothOrNoneRunning(string? path, GenshinRegistryType expected)
    {
        var reg = new FakeRegistry(GenshinSettingsStore.ChineseRegistryPath, GenshinSettingsStore.GlobalRegistryPath);
        Assert.Equal(expected, Store(reg).ResolveRegistryType(path));
        Assert.Equal(expected, Store(reg, "YuanShen", "GenshinImpact").ResolveRegistryType(path));
    }

    [Fact]
    public void OnlyGlobalKeyExists()
    {
        var reg = new FakeRegistry(GenshinSettingsStore.GlobalRegistryPath);
        var store = Store(reg);
        Assert.Equal(GenshinRegistryType.Global, store.ResolveRegistryType(null));
        using var key = store.OpenGameKey();
        Assert.Equal(GenshinSettingsStore.GlobalRegistryPath, key?.Path);
        Assert.Null(store.OpenGameKey(GenshinRegistryType.Chinese));
        Assert.Null(store.OpenGameKey(GenshinRegistryType.Cloud));
    }

    [Fact]
    public void FindValueNameByFragment()
    {
        using var key = new FakeRegistry(GenshinSettingsStore.ChineseRegistryPath).OpenSubKey(GenshinSettingsStore.ChineseRegistryPath)!;
        Assert.Equal("GENERAL_DATA_h2389025596", GenshinSettingsStore.FindValueName(key, "GENERAL_DATA"));
        Assert.Equal("", GenshinSettingsStore.FindValueName(key, "Height"));
    }

    [Fact]
    public void ReadsGeneralDataFromWinePrefix()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bgi-wine-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", "wine-user.reg"), Path.Combine(dir, "user.reg"));
        var store = new GenshinSettingsStore(new WinePrefixUserRegistry(dir), _ => false);
        using var key = store.OpenGameKey(GenshinRegistryType.Auto, null);
        Assert.NotNull(key);
        var name = GenshinSettingsStore.FindValueName(key!, "GENERAL_DATA");
        Assert.IsType<byte[]>(key!.GetValue(name));
        Assert.Equal(1920, key.GetValue(GenshinSettingsStore.FindValueName(key, "Width")));
    }
}
