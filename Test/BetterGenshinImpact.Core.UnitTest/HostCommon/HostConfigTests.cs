using System.Text.Json;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Host.Common;
using BetterGenshinImpact.Host.Common.Config;
using BetterGenshinImpact.Service;
using Serilog;

namespace BetterGenshinImpact.Core.UnitTest.HostCommon;

public sealed class HostConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bgi-host-config-" + Guid.NewGuid().ToString("N"));
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();

    public HostConfigTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // 清理失败不影响测试
        }
    }

    [Fact]
    public void ReadsWindowsFormatAndWritesSameJson()
    {
        // Windows 主程序用同一个 ConfigJson.Options 写出的配置
        var original = new AllConfig();
        original.AutoPickConfig.Enabled = false;
        original.AutoSkipConfig.ClickChatOption = "随机选择选项";
        original.TriggerInterval = 77;
        var path = Path.Combine(_dir, "config.json");
        var windowsJson = JsonSerializer.Serialize(original, ConfigJson.Options);
        File.WriteAllText(path, windowsJson);

        using var service = new FileConfigService(path, Log);
        var read = service.Read();
        Assert.False(service.CreatedDefault);
        Assert.False(read.AutoPickConfig.Enabled);
        Assert.Equal("随机选择选项", read.AutoSkipConfig.ClickChatOption);
        Assert.Equal(77, read.TriggerInterval);
        // 读取时做与 Windows 主程序相同的旧字段迁移（如体力用法），迁移后的内容与 Windows 读取再保存的结果一致
        original.AutoPickConfig.MigrateLegacyConfig();
        original.AutoDomainConfig.MigrateLegacyResinUsage();
        original.AutoStygianOnslaughtConfig.MigrateLegacyResinUsage();
        original.AutoBossConfig.MigrateLegacyResinUsage();
        original.AutoLeyLineOutcropConfig.MigrateLegacyResinUsage();
        Assert.Equal(JsonSerializer.Serialize(original, ConfigJson.Options), JsonSerializer.Serialize(read, ConfigJson.Options));
    }

    [Fact]
    public void MissingFileUsesDefaultsAndCorruptFileIsBackedUp()
    {
        var path = Path.Combine(_dir, "config.json");
        using (var service = new FileConfigService(path, Log))
        {
            Assert.NotNull(service.Read());
            Assert.True(service.CreatedDefault);
        }

        File.WriteAllText(path, "{ 这不是 JSON");
        using (var service = new FileConfigService(path, Log))
        {
            Assert.NotNull(service.Read());
        }

        Assert.Single(Directory.GetFiles(Path.Combine(_dir, "backup"), "config_*.json.bak"));
    }

    [Fact]
    public async Task StoreCoalescesChangesAndWritesAtomically()
    {
        var path = Path.Combine(_dir, "data.json");
        var value = 0;
        using var store = new JsonFileStore(path, () => JsonSerializer.Serialize(new { value }), Log, TimeSpan.FromMilliseconds(100));
        for (var i = 1; i <= 20; i++)
        {
            value = i;
            store.RequestSave();
        }

        await Task.Delay(600);
        Assert.Equal(1, store.WriteCount);
        Assert.False(store.HasPendingChanges);
        Assert.Contains("20", File.ReadAllText(path));
        // 没有残留的临时文件
        Assert.Empty(Directory.GetFiles(_dir, "data.json.*.tmp"));
    }

    [Fact]
    public void DisposeFlushesPendingChanges()
    {
        var path = Path.Combine(_dir, "data.json");
        var value = "a";
        var store = new JsonFileStore(path, () => JsonSerializer.Serialize(new { value }), Log, TimeSpan.FromHours(1));
        value = "b";
        store.RequestSave();
        Assert.False(File.Exists(path));
        store.Dispose();
        Assert.Contains("\"b\"", File.ReadAllText(path));
    }

    [Fact]
    public async Task LinuxHostConfigRoundTrips()
    {
        var path = Path.Combine(_dir, "linux-host.json");
        using (var store = new LinuxHostConfigStore(path, Log))
        {
            store.Config.CaptureMode = "GetImageRoot";
            store.Config.LaunchGameEnabled = true;
            store.Config.LaunchCommand = "umu-run {game}";
            store.Config.Environment = "PROTONPATH=GE-Proton\nGAMEID=0";
            await Task.Delay(50);
        }

        using var reloaded = new LinuxHostConfigStore(path, Log);
        Assert.Equal("GetImageRoot", reloaded.Config.CaptureMode);
        Assert.True(reloaded.Config.LaunchGameEnabled);
        Assert.Equal("umu-run {game}", reloaded.Config.LaunchCommand);
    }

    [Fact]
    public void LaunchCommandQuotesGamePathAndParsesEnvironment()
    {
        var config = new LinuxHostConfig { LaunchCommand = "wine {game}", GamePath = "/games/Genshin Impact/it's/YuanShen.exe" };
        Assert.Equal("wine '/games/Genshin Impact/it'\\''s/YuanShen.exe'", GameLauncher.BuildCommand(config));

        var env = GameLauncher.ParseEnvironment("# 注释\nWINEPREFIX=/pfx\n\nGAMEID = umu-0 ");
        Assert.Equal("/pfx", env["WINEPREFIX"]);
        Assert.Equal("umu-0", env["GAMEID"]);
        Assert.Throws<FormatException>(() => GameLauncher.ParseEnvironment("NOEQUALS"));
        Assert.Throws<InvalidOperationException>(() => GameLauncher.BuildCommand(new LinuxHostConfig { LaunchCommand = "wine {game}" }));
    }

    [Fact]
    public void RuntimeOverridesAreNotWrittenBack()
    {
        var config = new AllConfig();
        config.TriggerInterval = 77;
        var fileJson = JsonSerializer.Serialize(config, ConfigJson.Options);

        // 命令行覆盖：关闭拾取、改间隔
        var overrides = new RuntimeOverrides();
        config.AutoPickConfig.Enabled = false;
        config.TriggerInterval = 30;
        overrides.Record(fileJson, JsonSerializer.Serialize(config, ConfigJson.Options));
        Assert.Equal(2, overrides.Count);
        Assert.Contains("autoPickConfig.enabled", overrides.Paths);

        // 只有覆盖、没有其它改动：保存结果与文件原内容完全相同（格式也相同）
        Assert.Equal(fileJson, overrides.Apply(JsonSerializer.Serialize(config, ConfigJson.Options), ConfigJson.Options));

        // 之后在界面上改了别的设置和被覆盖的间隔：这些改动照常保存，仍等于覆盖值的拾取开关写回原值
        config.AutoSkipConfig.ClickChatOption = "随机选择选项";
        config.TriggerInterval = 60;
        var saved = JsonSerializer.Deserialize<AllConfig>(
            overrides.Apply(JsonSerializer.Serialize(config, ConfigJson.Options), ConfigJson.Options), ConfigJson.Options)!;
        Assert.True(saved.AutoPickConfig.Enabled);
        Assert.Equal(60, saved.TriggerInterval);
        Assert.Equal("随机选择选项", saved.AutoSkipConfig.ClickChatOption);
    }

    [Fact]
    public void LinuxHostRuntimeOverrideKeepsFileValue()
    {
        var path = Path.Combine(_dir, "linux-host.json");
        using (var store = new LinuxHostConfigStore(path, Log))
        {
            store.Config.CaptureMode = "GetImageRoot";
            store.Store.SaveNow();
        }

        using (var store = new LinuxHostConfigStore(path, Log))
        {
            store.ApplyRuntimeOverride(c =>
            {
                c.CaptureMode = "CompositeWindow";
                c.MaskWindowEnabled = true;
            });
            Assert.Equal("CompositeWindow", store.Config.CaptureMode);
            store.Config.LaunchTimeoutSeconds = 30;
            store.Store.SaveNow();
        }

        var saved = JsonSerializer.Deserialize<LinuxHostConfig>(File.ReadAllText(path), ConfigJson.Options)!;
        Assert.Equal("GetImageRoot", saved.CaptureMode);
        Assert.False(saved.MaskWindowEnabled);
        Assert.Equal(30, saved.LaunchTimeoutSeconds);
    }
}
