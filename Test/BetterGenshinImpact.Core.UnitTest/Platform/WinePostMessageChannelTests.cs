using System;
using System.Collections.Generic;
using System.IO;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Platform.Linux.Wine;

namespace BetterGenshinImpact.Core.UnitTest.Platform;

/// <summary>
/// Linux 上 Wine 游戏的后台通道（PostMessage 经辅助程序）：消息序列与 Windows 版 PostMessageSimulator 相同、坐标换算、
/// WM_ACTIVATE 开关、辅助程序启动失败时的回退。不启动 wine，用假的投递目标
/// </summary>
public class WinePostMessageChannelTests
{
    private sealed class FakeSink : IWinePostSink
    {
        public List<(uint Msg, ulong W, ulong L, uint Flags)> Posts { get; } = [];
        public bool IsAlive { get; set; } = true;
        public bool Disposed { get; private set; }

        public bool Post(uint msg, ulong wParam, ulong lParam, uint flags = 0)
        {
            Posts.Add((msg, wParam, lParam, flags));
            return IsAlive;
        }

        public void Dispose() => Disposed = true;
    }

    private static (WinePostMessageChannel Channel, FakeSink Sink, Func<int> Starts) Create(bool activate = false, bool fail = false)
    {
        var sink = new FakeSink();
        var starts = 0;
        var channel = new WinePostMessageChannel(
            () =>
            {
                starts++;
                return fail ? (null, "测试：启动失败") : (sink, null);
            },
            () => new PixelRect(100, 50, 1920, 1080),
            () => (2560, 1440),
            () => activate);
        return (channel, sink, () => starts);
    }

    [Fact]
    public void KeyPress_PostsDownCharUp_WithScanCodeFill()
    {
        var (channel, sink, _) = Create();

        channel.Keyboard.KeyPress(GameKey.VK_F);

        Assert.Equal(
        [
            (WinePostMessageChannel.WM_KEYDOWN, (ulong)GameKey.VK_F, 1ul, WinePostMessageClient.FillScanCode),
            (WinePostMessageChannel.WM_CHAR, (ulong)GameKey.VK_F, 1ul, WinePostMessageClient.FillScanCode),
            (WinePostMessageChannel.WM_KEYUP, (ulong)GameKey.VK_F, 0xC0000001ul, WinePostMessageClient.FillScanCode),
        ], sink.Posts);
    }

    [Fact]
    public void ActivateOff_NoWmActivate()
    {
        var (channel, sink, _) = Create(activate: false);

        channel.Keyboard.KeyDown(GameKey.VK_W);
        channel.Keyboard.KeyUp(GameKey.VK_W);
        channel.Mouse.LeftButtonClick();

        Assert.DoesNotContain(sink.Posts, p => p.Msg == WinePostMessageChannel.WM_ACTIVATE);
    }

    [Fact]
    public void ActivateOn_PostsWmActivateOnlyIfBackground_BeforeEachOperation()
    {
        var (channel, sink, _) = Create(activate: true);

        channel.Keyboard.KeyDown(GameKey.VK_W);
        channel.Mouse.LeftButtonDown();

        var activate = (WinePostMessageChannel.WM_ACTIVATE, 1ul, 0ul, WinePostMessageClient.OnlyIfBackground);
        Assert.Equal(activate, sink.Posts[0]);
        Assert.Equal(WinePostMessageChannel.WM_KEYDOWN, sink.Posts[1].Msg);
        Assert.Equal(activate, sink.Posts[2]);
        Assert.Equal(WinePostMessageChannel.WM_LBUTTONDOWN, sink.Posts[3].Msg);
    }

    [Fact]
    public void Click_UsesClientCoordinates_DefaultIs16x16()
    {
        var (channel, sink, _) = Create();

        channel.Mouse.RightButtonClick();
        Assert.Equal((WinePostMessageChannel.WM_RBUTTONDOWN, 0ul, (ulong)((16 << 16) | 16), 0u), sink.Posts[0]);
        Assert.Equal(WinePostMessageChannel.WM_RBUTTONUP, sink.Posts[1].Msg);

        // 归一化 32767.5 ≈ 屏幕中心 (1280,720)，客户区左上角 (100,50) → (1180,670)
        channel.Mouse.MoveMouseTo(65535 / 2.0, 65535 / 2.0);
        Assert.Equal((1180, 670), channel.Pointer);
        sink.Posts.Clear();
        channel.Mouse.LeftButtonClick();
        Assert.Equal(WinePostMessageChannel.PointLParam(1180, 670), sink.Posts[0].L);
        Assert.Equal([WinePostMessageChannel.WM_LBUTTONDOWN, WinePostMessageChannel.WM_LBUTTONUP], sink.Posts.ConvertAll(p => p.Msg));
    }

    [Fact]
    public void Unsupported_Ignored()
    {
        var (channel, sink, _) = Create();

        channel.Mouse.MoveMouseBy(5, 5);
        channel.Mouse.MiddleButtonClick();
        channel.Mouse.VerticalScroll(1);

        Assert.Empty(sink.Posts);
    }

    [Fact]
    public void StartFailure_NotAvailable_NoRetryWithinInterval()
    {
        var (channel, sink, starts) = Create(fail: true);

        Assert.False(channel.EnsureStarted());
        channel.Keyboard.KeyPress(GameKey.VK_F);
        Assert.False(channel.EnsureStarted());

        Assert.Equal(1, starts());
        Assert.Empty(sink.Posts);
    }

    [Fact]
    public void Backend_Background_NullWhenHelperDied()
    {
        var (channel, sink, starts) = Create();
        Assert.True(channel.EnsureStarted());
        sink.IsAlive = false;

        Assert.False(channel.EnsureStarted());
        Assert.True(sink.Disposed);
        Assert.Equal(1, starts());
    }

    [Fact]
    public void ReleaseAll_LiftsPressedKeys_OnlyWhenStarted()
    {
        var (channel, sink, starts) = Create();
        channel.ReleaseAll();
        Assert.Equal(0, starts());

        channel.Keyboard.KeyDown(GameKey.VK_W);
        sink.Posts.Clear();
        channel.ReleaseAll();

        Assert.Equal([(WinePostMessageChannel.WM_KEYUP, (ulong)GameKey.VK_W, 0xC0000001ul, WinePostMessageClient.FillScanCode)], sink.Posts);
    }

    [Fact]
    public void Dispose_DisposesSink_AndStopsStarting()
    {
        var (channel, sink, starts) = Create();
        Assert.True(channel.EnsureStarted());
        channel.Dispose();

        Assert.True(sink.Disposed);
        Assert.False(channel.EnsureStarted());
        Assert.Equal(1, starts());
    }

    [Fact]
    public void HelperImage_IsPe32Plus_AndStable()
    {
        var a = WinePostHelperImage.Build();
        var b = WinePostHelperImage.Build();

        Assert.Equal(a, b);
        Assert.Equal((byte)'M', a[0]);
        Assert.Equal((byte)'Z', a[1]);
        var pe = BitConverter.ToInt32(a, 0x3C);
        Assert.Equal(0x00004550u, BitConverter.ToUInt32(a, pe));
        Assert.Equal((ushort)0x8664, BitConverter.ToUInt16(a, pe + 4));
        Assert.Equal((ushort)0x20B, BitConverter.ToUInt16(a, pe + 24));
    }

    [Fact]
    public void TryStart_NoWineLoader_ReturnsReason_WithoutStartingAnything()
    {
        var root = Directory.CreateTempSubdirectory("bgi-wine-proc-");
        try
        {
            var pidDir = Directory.CreateDirectory(Path.Combine(root.FullName, "4242"));
            File.WriteAllText(Path.Combine(pidDir.FullName, "environ"), "WINEPREFIX=/tmp/none\0HOME=/tmp\0");
            var exeDir = Directory.CreateDirectory(Path.Combine(root.FullName, "nowine"));
            File.WriteAllText(Path.Combine(exeDir.FullName, "wine-preloader"), "");
            File.CreateSymbolicLink(Path.Combine(pidDir.FullName, "exe"), Path.Combine(exeDir.FullName, "wine-preloader"));

            var client = WinePostMessageClient.TryStart(4242, "原神", null, out var error, helperDir: root.FullName, procRoot: root.FullName);

            Assert.Null(client);
            Assert.Contains("找不到游戏进程 4242 使用的 wine", error);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public void FindLoader_PrefersWineLoader_ThenExeDir()
    {
        var root = Directory.CreateTempSubdirectory("bgi-wine-loader-");
        try
        {
            var bin = Directory.CreateDirectory(Path.Combine(root.FullName, "bin"));
            var wine64 = Path.Combine(bin.FullName, "wine64");
            File.WriteAllText(wine64, "");
            var custom = Path.Combine(root.FullName, "custom-wine");
            File.WriteAllText(custom, "");

            Assert.Equal(wine64, WinePostMessageClient.FindLoader(new Dictionary<string, string>(), bin.FullName));
            Assert.Equal(custom, WinePostMessageClient.FindLoader(new Dictionary<string, string> { ["WINELOADER"] = custom }, bin.FullName));
            Assert.Null(WinePostMessageClient.FindLoader(new Dictionary<string, string>(), null));
        }
        finally
        {
            root.Delete(true);
        }
    }
}
