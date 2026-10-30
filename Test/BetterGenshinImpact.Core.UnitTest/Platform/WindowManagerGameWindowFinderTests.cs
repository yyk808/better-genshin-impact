namespace BetterGenshinImpact.Core.UnitTest.Platform;

public class WindowManagerGameWindowFinderTests
{
    private static readonly string[] Names = ["YuanShen", "GenshinImpact"];

    [Fact]
    public void FindsByProcessName_IgnoringExeSuffixAndCase()
    {
        var wm = new FakeWindowManager(
            new FakeWindow(1, "xfce4-terminal", "xfce4-terminal/Xfce4-terminal", 800, 600),
            new FakeWindow(2, "yuanshen.EXE", "", 1920, 1080));
        var finder = new WindowManagerGameWindowFinder(wm);

        var found = finder.FindGameWindow(new GameWindowSearch(Names));

        Assert.NotNull(found);
        Assert.Equal(2, found!.Handle);
        Assert.True(wm.All[0].Disposed);
        Assert.False(wm.All[1].Disposed);
    }

    [Fact]
    public void FindsWineWindowByWmClass()
    {
        // Wine 窗口 WM_CLASS 为 "yuanshen.exe/YuanShen.exe"，进程名可能是 wine 预加载器
        var wm = new FakeWindowManager(new FakeWindow(7, "wine64-preloader", "genshinimpact.exe/GenshinImpact.exe", 1280, 720));
        var found = new WindowManagerGameWindowFinder(wm).FindGameWindow(new GameWindowSearch(Names));
        Assert.Equal(7, found?.Handle);
    }

    [Fact]
    public void PrefersLargestMatchingWindow()
    {
        var wm = new FakeWindowManager(
            new FakeWindow(1, "YuanShen.exe", "yuanshen.exe/YuanShen.exe", 400, 300),
            new FakeWindow(2, "YuanShen.exe", "yuanshen.exe/YuanShen.exe", 1920, 1080),
            new FakeWindow(3, "YuanShen.exe", "yuanshen.exe/YuanShen.exe", 1, 1));
        var found = new WindowManagerGameWindowFinder(wm).FindGameWindow(new GameWindowSearch(Names));
        Assert.Equal(2, found?.Handle);
        Assert.True(wm.All[0].Disposed);
        Assert.True(wm.All[2].Disposed);
    }

    [Fact]
    public void ReturnsNull_WhenNothingMatches()
    {
        var wm = new FakeWindowManager(new FakeWindow(1, "firefox", "Navigator/firefox", 1920, 1080, "原神 - 攻略"));
        Assert.Null(new WindowManagerGameWindowFinder(wm).FindGameWindow(new GameWindowSearch(Names)));
        Assert.True(wm.All[0].Disposed);
    }

    [Fact]
    public void ReturnsNull_WhenNoProcessNames()
    {
        var wm = new FakeWindowManager(new FakeWindow(1, "YuanShen", "", 10, 10));
        Assert.Null(new WindowManagerGameWindowFinder(wm).FindGameWindow(new GameWindowSearch(["", " "])));
    }

    [Fact]
    public void ProcessNameMustMatchExactly()
    {
        // "YuanShenLauncher" 不应被当成 "YuanShen"
        var w = new FakeWindow(1, "YuanShenLauncher", "", 10, 10);
        Assert.False(WindowManagerGameWindowFinder.IsGameWindow(w, Names));
    }

    [Theory]
    [InlineData("记事本", "Notepad", 1, "notepad", null, null, null, null, true)]
    [InlineData("记事本", "Notepad", 1, "notepad", "记事", "note", 1, "NOTE", true)]
    [InlineData("记事本", "Notepad", 1, "notepad", null, null, 2, null, false)]
    [InlineData("记事本", "Notepad", 1, null, null, null, null, "note", false)]
    public void WindowQueryMatches(string title, string cls, int pid, string? proc,
        string? qTitle, string? qClass, int? qPid, string? qProc, bool expected)
    {
        Assert.Equal(expected, new WindowQuery(qTitle, qClass, qPid, qProc).Matches(title, cls, pid, proc));
    }
}
