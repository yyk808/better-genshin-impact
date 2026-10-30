namespace BetterGenshinImpact.Core.UnitTest.Platform;

/// <summary>测试用窗口</summary>
internal sealed class FakeWindow(nint handle, string processName, string className, int width, int height, string title = "")
    : IPlatformWindow
{
    public bool Disposed { get; private set; }
    public nint Handle { get; } = handle;
    public int ProcessId => (int)Handle;
    public string Title { get; } = title;
    public string ProcessName { get; } = processName;
    public string ClassName { get; } = className;
    public GameViewport Viewport => new(new PixelRect(0, 0, width, height), 1f);
    public bool IsAlive => !Disposed;
    public bool IsForeground => false;
    public bool IsMinimized => false;
    public bool RequiresForeground => true;
#pragma warning disable CS0067
    public event EventHandler? ViewportChanged;
#pragma warning restore CS0067
    public void Activate() { }
    public void RequestClose() { }
    public void Dispose() => Disposed = true;
}

/// <summary>测试用窗口管理器：每次枚举返回同一组窗口</summary>
internal sealed class FakeWindowManager(params FakeWindow[] windows) : IWindowManager
{
    public IReadOnlyList<FakeWindow> All => windows;
    public IReadOnlyList<IPlatformWindow> EnumerateWindows() => windows;
    public IReadOnlyList<IPlatformWindow> FindWindows(WindowQuery query) =>
        windows.Where(w => query.Matches(w.Title, w.ClassName, w.ProcessId, w.ProcessName)).ToList();
    public IPlatformWindow? GetForegroundWindow() => null;
    public IPlatformWindow? FromHandle(nint handle) => windows.FirstOrDefault(w => w.Handle == handle);
    public void Dispose() { }
}
