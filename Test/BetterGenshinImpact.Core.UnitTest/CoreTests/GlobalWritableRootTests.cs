using System;
using System.IO;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Host.Common;
using BetterGenshinImpact.Infrastructure.Storage;
using Xunit;

namespace BetterGenshinImpact.Core.UnitTest.CoreTests;

/// <summary>
/// Linux 可写数据根：安装到只读前缀后 User/Data/log/Repos 必须与程序目录分离。
/// 不访问网络，也不改动本机已有用户数据。
/// </summary>
[Collection("GlobalPathIsolation")]
public sealed class GlobalWritableRootTests : IDisposable
{
    private readonly string _startup;
    private readonly string? _writable;
    private readonly string? _dataDirEnv;
    private readonly string? _xdgDataHome;

    public GlobalWritableRootTests()
    {
        _startup = Global.StartUpPath;
        _writable = Global.WritableRoot;
        _dataDirEnv = Environment.GetEnvironmentVariable(Global.DataDirectoryVariable);
        _xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
    }

    public void Dispose()
    {
        Global.StartUpPath = _startup;
        Global.WritableRoot = _writable;
        SetEnv(Global.DataDirectoryVariable, _dataDirEnv);
        SetEnv("XDG_DATA_HOME", _xdgDataHome);
    }

    [Theory]
    [InlineData("User", true)]
    [InlineData("User/config.json", true)]
    [InlineData(@"User\config.json", true)]
    [InlineData("Data", true)]
    [InlineData("Data/WebKit/Guide", true)]
    [InlineData("log", true)]
    [InlineData("log/auto/screenshot", true)]
    [InlineData("Repos", true)]
    [InlineData("Repos/foo", true)]
    [InlineData("UserSecrets", false)]
    [InlineData("login", false)]
    [InlineData("Assets", false)]
    [InlineData("Assets/Map", false)]
    [InlineData("", false)]
    public void UsesWritableRoot_OnlyUserDataPrefixes(string relative, bool expected)
    {
        Assert.Equal(expected, Global.UsesWritableRoot(relative));
    }

    [Fact]
    public void Absolute_WithoutWritableRoot_StaysUnderStartup()
    {
        var root = CreateTempDir();
        Global.StartUpPath = root + Path.DirectorySeparatorChar;
        Global.WritableRoot = null;
        Assert.Equal(Path.Combine(root, "User", "config.json"), Global.Absolute("User/config.json"));
        Assert.Equal(Path.Combine(root, "Assets", "Map"), Global.Absolute("Assets/Map"));
    }

    [Fact]
    public void Absolute_WithWritableRoot_SplitsUserDataFromAssets()
    {
        var assets = CreateTempDir();
        var data = CreateTempDir();
        Global.StartUpPath = assets + Path.DirectorySeparatorChar;
        Global.WritableRoot = data + Path.DirectorySeparatorChar;
        Assert.Equal(Path.Combine(data, "User", "config.json"), Global.Absolute("User/config.json"));
        Assert.Equal(Path.Combine(data, "Data", "WebKit"), Global.Absolute("Data/WebKit"));
        Assert.Equal(Path.Combine(data, "log"), Global.Absolute("log"));
        Assert.Equal(Path.Combine(data, "Repos", "x"), Global.Absolute("Repos/x"));
        Assert.Equal(Path.Combine(assets, "Assets", "Map"), Global.Absolute("Assets/Map"));
        Assert.False(SafePath.IsUnder(assets, Global.Absolute("User/config.json")));
        Assert.True(SafePath.IsUnderManagedRoot(Global.Absolute("User/config.json")));
        Assert.True(SafePath.IsUnderManagedRoot(Global.Absolute("Assets/Map")));
        Assert.Equal("User/config.json", SafePath.GetManagedRelativePath(Global.Absolute("User/config.json")).Replace('\\', '/'));
        Assert.Equal(Path.GetFullPath(Global.Absolute("User/config.json")), SafePath.ResolveManagedPath("User/config.json"));
    }

    [Fact]
    public void ApplyWritableRoot_WhenStartupWritableAndUnspecified_KeepsPortableLayout()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var assets = CreateTempDir();
        Global.StartUpPath = assets + Path.DirectorySeparatorChar;
        SetEnv(Global.DataDirectoryVariable, null);
        Global.ApplyWritableRoot(null);
        Assert.Null(Global.WritableRoot);
        Assert.Equal(Path.Combine(assets, "User", "config.json"), Global.Absolute("User/config.json"));
    }

    [Fact]
    public void ApplyWritableRoot_UsesExplicitDirectoryAndSeedsI18n()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var assets = CreateTempDir();
        var data = CreateTempDir();
        Directory.CreateDirectory(Path.Combine(assets, "User", "I18n"));
        File.WriteAllText(Path.Combine(assets, "User", "I18n", "zh-Hans.json"), "{}");
        Global.StartUpPath = assets + Path.DirectorySeparatorChar;
        SetEnv(Global.DataDirectoryVariable, null);
        Global.ApplyWritableRoot(data);
        Assert.Equal(Path.GetFullPath(data) + Path.DirectorySeparatorChar, Global.WritableRoot);
        Assert.True(File.Exists(Path.Combine(data, "User", "I18n", "zh-Hans.json")));
        Assert.True(Directory.Exists(Path.Combine(data, "log")));
        Assert.Equal(Path.Combine(Path.GetFullPath(data), "User", "config.json"), Global.Absolute("User/config.json"));
        // 再跑一次不得覆盖已有用户语言文件
        File.WriteAllText(Path.Combine(data, "User", "I18n", "zh-Hans.json"), "keep");
        Global.ApplyWritableRoot(data);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(data, "User", "I18n", "zh-Hans.json")));
    }

    [Fact]
    public void ApplyWritableRoot_UsesEnvironmentVariable()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var assets = CreateTempDir();
        var data = CreateTempDir();
        Global.StartUpPath = assets + Path.DirectorySeparatorChar;
        SetEnv(Global.DataDirectoryVariable, data);
        Global.ApplyWritableRoot(null);
        Assert.StartsWith(Path.GetFullPath(data), Global.WritableRoot);
        Assert.Equal(Path.Combine(Path.GetFullPath(data), "Data"), Global.Absolute("Data"));
    }

    [Fact]
    public void ApplyWritableRoot_UnwritableStartupFallsBackToXdg()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var assets = CreateTempDir();
        var xdg = CreateTempDir();
        // 去掉写权限以模拟 /usr/lib 安装前缀
        File.SetAttributes(assets, FileAttributes.ReadOnly);
        try
        {
            new DirectoryInfo(assets).UnixFileMode = UnixFileMode.UserRead | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        }
        catch (PlatformNotSupportedException)
        {
            return;
        }

        try
        {
            Global.StartUpPath = assets + Path.DirectorySeparatorChar;
            SetEnv(Global.DataDirectoryVariable, null);
            SetEnv("XDG_DATA_HOME", xdg);
            Global.ApplyWritableRoot(null);
            var expected = Path.GetFullPath(Path.Combine(xdg, "better-genshin-impact"));
            Assert.StartsWith(expected, Global.WritableRoot);
            Assert.True(Directory.Exists(Path.Combine(expected, "User")));
        }
        finally
        {
            try
            {
                new DirectoryInfo(assets).UnixFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute;
            }
            catch
            {
                // 仅用于测试清理
            }
        }
    }

    [Fact]
    public void GameHostOptions_ParsesDataDir()
    {
        var options = new GameHostOptions().Apply("--data-dir", () => "/tmp/bgi-data");
        Assert.NotNull(options);
        Assert.Equal("/tmp/bgi-data", options!.DataDirectory);
        var alias = new GameHostOptions().Apply("--data-directory", () => "/tmp/bgi-data-2");
        Assert.Equal("/tmp/bgi-data-2", alias!.DataDirectory);
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bgi-writable-root-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void SetEnv(string name, string? value)
    {
        Environment.SetEnvironmentVariable(name, value);
    }
}
