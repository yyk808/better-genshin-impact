using System;
using System.Collections.Generic;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Platform.Linux.Desktop;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterGenshinImpact.Core.UnitTest.Platform;

/// <summary>
/// Linux 独立任务勿扰：配置开关、GNOME gsettings / swaync / mako 选择与恢复（不真正跑外部命令）。
/// </summary>
public class LinuxNotificationSuppressionServiceTests
{
    private sealed class FakeConfig(AllConfig config) : IConfigService
    {
        public AllConfig Get() => config;
        public void Save() { }
        public void Flush() { }
        public AllConfig Read() => config;
        public void Write(AllConfig config) { }
    }

    private static AllConfig Config(bool suppress) => new()
    {
        OtherConfig = new OtherConfig { SuppressWindowsNotificationsDuringSoloTask = suppress },
    };

    [Fact]
    public void DisabledConfig_ReturnsNull_WithoutRunningCommands()
    {
        var calls = 0;
        var svc = new LinuxNotificationSuppressionService(
            new FakeConfig(Config(false)),
            NullLogger<LinuxNotificationSuppressionService>.Instance,
            (_, _) => { calls++; return (0, "true"); });

        Assert.Null(svc.TrySuppressForSoloTask());
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Gsettings_AlreadyFalse_ReturnsNull()
    {
        var svc = new LinuxNotificationSuppressionService(
            new FakeConfig(Config(true)),
            NullLogger<LinuxNotificationSuppressionService>.Instance,
            (file, args) =>
            {
                Assert.Equal("gsettings", file);
                Assert.Equal(["get", "org.gnome.desktop.notifications", "show-banners"], args);
                return (0, "false\n");
            });

        Assert.Null(svc.TrySuppressForSoloTask());
    }

    [Fact]
    public void Gsettings_True_SetsFalse_AndRestores()
    {
        var log = new List<string>();
        var svc = new LinuxNotificationSuppressionService(
            new FakeConfig(Config(true)),
            NullLogger<LinuxNotificationSuppressionService>.Instance,
            (file, args) =>
            {
                log.Add(file + " " + string.Join(' ', args));
                if (args[0] == "get") return (0, "true");
                if (args[0] == "set" && args[^1] == "false") return (0, "");
                if (args[0] == "set" && args[^1] == "true") return (0, "");
                return (1, "");
            });

        using (var scope = svc.TrySuppressForSoloTask())
        {
            Assert.NotNull(scope);
            Assert.Contains("gsettings set org.gnome.desktop.notifications show-banners false", log);
        }

        Assert.Contains("gsettings set org.gnome.desktop.notifications show-banners true", log);
    }

    [Fact]
    public void FallsBackToSwaync_WhenGsettingsMissing()
    {
        var log = new List<string>();
        var svc = new LinuxNotificationSuppressionService(
            new FakeConfig(Config(true)),
            NullLogger<LinuxNotificationSuppressionService>.Instance,
            (file, args) =>
            {
                log.Add(file + " " + string.Join(' ', args));
                if (file == "gsettings") return (127, "");
                if (file == "swaync-client" && args is ["-D"]) return (0, "false\n");
                if (file == "swaync-client" && args is ["-dn"]) return (0, "");
                if (file == "swaync-client" && args is ["-df"]) return (0, "");
                return (127, "");
            });

        using (var scope = svc.TrySuppressForSoloTask())
        {
            Assert.NotNull(scope);
        }

        Assert.Contains("swaync-client -dn", log);
        Assert.Contains("swaync-client -df", log);
    }

    [Fact]
    public void FallsBackToMako_WhenOthersMissing()
    {
        var log = new List<string>();
        var svc = new LinuxNotificationSuppressionService(
            new FakeConfig(Config(true)),
            NullLogger<LinuxNotificationSuppressionService>.Instance,
            (file, args) =>
            {
                log.Add(file + " " + string.Join(' ', args));
                if (file is "gsettings" or "swaync-client") return (127, "");
                if (file == "makoctl" && args is ["mode"]) return (0, "default\n");
                if (file == "makoctl" && args.Count >= 2 && args[1] == "-a") return (0, "");
                if (file == "makoctl" && args.Count >= 2 && args[1] == "-r") return (0, "");
                return (127, "");
            });

        using (var scope = svc.TrySuppressForSoloTask())
        {
            Assert.NotNull(scope);
        }

        Assert.Contains("makoctl mode -a dnd", log);
        Assert.Contains("makoctl mode -r dnd", log);
    }

    [Fact]
    public void NoBackend_ReturnsNull()
    {
        var svc = new LinuxNotificationSuppressionService(
            new FakeConfig(Config(true)),
            NullLogger<LinuxNotificationSuppressionService>.Instance,
            (_, _) => (127, ""));

        Assert.Null(svc.TrySuppressForSoloTask());
    }
}
