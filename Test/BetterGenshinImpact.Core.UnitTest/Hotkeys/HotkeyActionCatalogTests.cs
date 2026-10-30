using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common.Hotkeys;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterGenshinImpact.Core.UnitTest.Hotkeys;

public class HotkeyActionCatalogTests
{
    [Fact]
    public void RealtimeToggles_FlipConfigValue()
    {
        var config = new AllConfig();
        foreach (var toggle in HotkeyActionCatalog.RealtimeToggles)
        {
            var before = toggle.Get(config);
            Assert.Equal(!before, HotkeyActionCatalog.Toggle(toggle, config, NullLogger.Instance));
            Assert.Equal(!before, toggle.Get(config));
            HotkeyActionCatalog.Toggle(toggle, config, NullLogger.Instance);
            Assert.Equal(before, toggle.Get(config));
        }
    }

    [Fact]
    public void HotkeyPropertyNames_ExistOnHotKeyConfig()
    {
        var names = HotkeyActionCatalog.RealtimeToggles.Select(t => t.HotkeyProperty)
            .Concat(HotkeyActionCatalog.SoloTaskHotkeys.Keys)
            .Concat(HotkeyActionCatalog.HoldHotkeys);
        foreach (var name in names)
        {
            Assert.NotNull(typeof(HotKeyConfig).GetProperty(name));
        }

        Assert.Equal(7, HotkeyActionCatalog.RealtimeToggles.Count);
        Assert.Equal(8, HotkeyActionCatalog.SoloTaskHotkeys.Count);
    }

    [Fact]
    public void ToggleLogBox_SyncsStatusWindow()
    {
        var mask = new MaskWindowConfig { ShowLogBox = false, ShowStatus = true };
        HotkeyActionCatalog.ToggleLogBox(mask);
        Assert.True(mask.ShowLogBox);
        Assert.True(mask.ShowStatus);
        HotkeyActionCatalog.ToggleLogBox(mask);
        Assert.False(mask.ShowLogBox);
        Assert.False(mask.ShowStatus);
    }

    [Fact]
    public async Task HoldRunner_RepeatsWhilePressed_AndStopsOnRelease()
    {
        var count = 0;
        var downs = 0;
        var ups = 0;
        var runner = new HoldHotkeyRunner
        {
            OnKeyPress = () =>
            {
                Interlocked.Increment(ref count);
                Thread.Sleep(5);
            },
            OnKeyDown = () => downs++,
            OnKeyUp = () => ups++,
        };

        runner.KeyDown();
        runner.KeyDown(); // 自动重复：忽略
        await Task.Delay(100);
        Assert.True(runner.IsPressed);
        runner.KeyUp();
        await Task.Delay(50);
        var stopped = Volatile.Read(ref count);
        await Task.Delay(100);
        Assert.True(stopped > 2);
        Assert.Equal(stopped, Volatile.Read(ref count));
        Assert.Equal(1, downs);
        Assert.Equal(1, ups);
        Assert.False(runner.IsPressed);
    }

    [Fact]
    public void HoldRunner_IgnoresReleaseWithoutPress_AndPressWhenNotAllowed()
    {
        var ups = 0;
        var downs = 0;
        var runner = new HoldHotkeyRunner { CanRun = () => false, OnKeyDown = () => downs++, OnKeyUp = () => ups++ };
        runner.KeyUp();
        runner.KeyDown();
        runner.KeyUp();
        Assert.Equal(0, downs);
        Assert.Equal(0, ups);
        Assert.False(runner.IsPressed);
    }
}
