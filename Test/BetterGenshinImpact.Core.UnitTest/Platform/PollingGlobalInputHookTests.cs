namespace BetterGenshinImpact.Core.UnitTest.Platform;

public class PollingGlobalInputHookTests
{
    private sealed class FakeKeyState : IKeyStateProvider, IDisposable
    {
        public HashSet<GameKey> Pressed { get; } = [];
        public PixelPoint? Cursor { get; set; } = new(10, 20);
        public bool Disposed { get; private set; }
        public bool IsKeyDown(GameKey key) => Pressed.Contains(key);
        public IReadOnlySet<GameKey> GetPressedKeys() { lock (this) return new HashSet<GameKey>(Pressed); }
        public PixelPoint? GetCursorPosition() => Cursor;
        public void Dispose() => Disposed = true;
    }

    private static (FakeKeyState state, PollingGlobalInputHook hook, List<string> log) Create()
    {
        var state = new FakeKeyState();
        var hook = new PollingGlobalInputHook(state, autoStart: false, ownsState: true);
        var log = new List<string>();
        hook.KeyDown += (_, e) => log.Add($"down {e.Key} {e.Modifiers}");
        hook.KeyUp += (_, e) => log.Add($"up {e.Key} {e.Modifiers}");
        hook.MouseDown += (_, e) => log.Add($"mdown {e.Button} {e.Location.X},{e.Location.Y}");
        hook.MouseUp += (_, e) => log.Add($"mup {e.Button}");
        hook.MouseMove += (_, e) => log.Add($"move {e.Location.X},{e.Location.Y}");
        return (state, hook, log);
    }

    [Fact]
    public void FirstPollOnlyPrimes()
    {
        var (state, hook, log) = Create();
        state.Pressed.Add(GameKey.VK_W);
        hook.Poll();
        Assert.Empty(log);
        hook.Poll();
        Assert.Empty(log);
    }

    [Fact]
    public void ReportsKeyDownAndUpWithModifiers()
    {
        var (state, hook, log) = Create();
        hook.Poll();
        state.Pressed.UnionWith([GameKey.VK_LCONTROL, GameKey.VK_C]);
        hook.Poll();
        state.Pressed.Remove(GameKey.VK_C);
        hook.Poll();
        state.Pressed.Clear();
        hook.Poll();
        Assert.Equal(new[]
        {
            "down VK_LCONTROL Control",
            "down VK_C Control",
            "up VK_C Control",
            "up VK_LCONTROL None",
        }, log);
    }

    [Fact]
    public void GenericModifierIsShadowedBySidedKey()
    {
        // X11 / GetAsyncKeyState 同时报告 VK_SHIFT 与 VK_LSHIFT，事件只报左右键
        var (state, hook, log) = Create();
        hook.Poll();
        state.Pressed.UnionWith([GameKey.VK_SHIFT, GameKey.VK_LSHIFT]);
        hook.Poll();
        state.Pressed.Clear();
        hook.Poll();
        Assert.Equal(new[] { "down VK_LSHIFT Shift", "up VK_LSHIFT None" }, log);
    }

    [Fact]
    public void ReportsMouseButtonsAndMoves()
    {
        var (state, hook, log) = Create();
        hook.Poll();
        state.Cursor = new PixelPoint(30, 40);
        state.Pressed.Add(GameKey.VK_RBUTTON);
        hook.Poll();
        state.Pressed.Clear();
        hook.Poll();
        hook.Poll();
        Assert.Equal(new[] { "move 30,40", "mdown VK_RBUTTON 30,40", "mup VK_RBUTTON" }, log);
    }

    [Fact]
    public void DisposeStopsEventsAndReleasesOwnedState()
    {
        var (state, hook, log) = Create();
        hook.Poll();
        hook.Dispose();
        state.Pressed.Add(GameKey.VK_A);
        hook.Poll();
        Assert.Empty(log);
        Assert.True(state.Disposed);
        Assert.False(hook.SupportsWheel);
    }

    [Fact]
    public void BackgroundThreadPolls()
    {
        var state = new FakeKeyState();
        using var hook = new PollingGlobalInputHook(state, TimeSpan.FromMilliseconds(2));
        using var got = new ManualResetEventSlim();
        hook.KeyDown += (_, e) => { if (e.Key == GameKey.VK_F) got.Set(); };
        Thread.Sleep(30);
        lock (state) state.Pressed.Add(GameKey.VK_F);
        Assert.True(got.Wait(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(GameKey.VK_LBUTTON, "Left")]
    [InlineData(GameKey.VK_RBUTTON, "Right")]
    [InlineData(GameKey.VK_MBUTTON, "Middle")]
    [InlineData(GameKey.VK_XBUTTON1, "XButton1")]
    [InlineData(GameKey.VK_XBUTTON2, "XButton2")]
    [InlineData(GameKey.None, "None")]
    public void MouseButtonNameMatchesWinForms(GameKey button, string expected)
    {
        Assert.Equal(expected, KeyModifierHelper.MouseButtonName(button));
    }

    [Fact]
    public void ModifiersFromPressedKeys()
    {
        Assert.Equal(KeyModifiers.Shift | KeyModifiers.Alt,
            KeyModifierHelper.FromPressedKeys(new HashSet<GameKey> { GameKey.VK_RSHIFT, GameKey.VK_LMENU, GameKey.VK_A }));
        Assert.Equal(KeyModifiers.None, KeyModifierHelper.FromPressedKeys(new HashSet<GameKey> { GameKey.VK_LWIN }));
    }
}
