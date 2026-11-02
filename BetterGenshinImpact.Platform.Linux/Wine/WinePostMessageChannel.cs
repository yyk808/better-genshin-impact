using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Platform;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Platform.Linux.Wine;

/// <summary>投递消息的目标（<see cref="WinePostMessageClient"/>；测试可替换）</summary>
public interface IWinePostSink : IDisposable
{
    bool IsAlive { get; }

    bool Post(uint msg, ulong wParam, ulong lParam, uint flags = 0);
}

/// <summary>
/// Linux 上 Wine 游戏窗口的后台通道：经 <see cref="WinePostMessageClient"/> 在游戏的 Wine 前缀里调用 PostMessageW，
/// 消息与 Windows 版 PostMessageChannel / PostMessageSimulator 相同（按键 WM_KEYDOWN / WM_CHAR / WM_KEYUP，左右键点击按下与抬起间隔 100 ms，
/// 点击位置为最近一次 MoveMouseTo 换算出的客户区坐标，默认 (16,16)）。不支持相对移动、中键、侧键、滚轮（warn 后忽略）。
/// <para>
/// 与 Windows 版的差别：默认不投递 WM_ACTIVATE。Wine 的 DefWindowProc 收到 WM_ACTIVATE 会调用 SetFocus 并向窗口管理器请求激活，
/// 游戏会被切到前台、紧随其后的按键可能丢失（实测）。<paramref name="activateBeforeInput"/> 返回 true 时照 Windows 的方式，
/// 游戏不在前台时每次操作前先投递 WM_ACTIVATE（会把游戏切到前台）。
/// </para>
/// <para>辅助程序在首次使用时启动（<see cref="EnsureStarted"/>）；启动失败或中途退出后 30 秒内不重试，此时 <see cref="XTestInputBackend"/> 返回告警通道</para>
/// </summary>
public sealed class WinePostMessageChannel : InputChannelBase, IDisposable
{
    public const uint WM_ACTIVATE = 0x0006;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_CHAR = 0x0102;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;

    /// <summary>与 PostMessageSimulator.LeftButtonClick 相同的按下时长</summary>
    public const int ClickHoldMs = 100;

    /// <summary>启动失败后多久再试</summary>
    public const int RetryIntervalMs = 30000;

    private readonly Func<(IWinePostSink? Sink, string? Error)> _start;
    private readonly Func<PixelRect> _clientRect;
    private readonly Func<(int Width, int Height)> _screenSize;
    private readonly Func<bool> _activateBeforeInput;
    private readonly object _startLock = new();
    private IWinePostSink? _sink;
    private long _lastFailure = long.MinValue;
    private bool _disposed;
    private (int X, int Y) _pointer = (16, 16);

    /// <param name="start">启动辅助程序，失败时返回原因</param>
    /// <param name="clientRect">游戏客户区在屏幕上的位置（像素）</param>
    /// <param name="screenSize">屏幕尺寸，用于把 0~65535 的归一化坐标换算成像素</param>
    /// <param name="activateBeforeInput">是否在游戏不在前台时先投递 WM_ACTIVATE（Linux 宿主设置，默认关闭）</param>
    public WinePostMessageChannel(Func<(IWinePostSink? Sink, string? Error)> start, Func<PixelRect> clientRect,
        Func<(int Width, int Height)> screenSize, Func<bool>? activateBeforeInput = null)
    {
        _start = start;
        _clientRect = clientRect;
        _screenSize = screenSize;
        _activateBeforeInput = activateBeforeInput ?? (() => false);
    }

    /// <summary>用 <see cref="WinePostMessageClient"/> 连接游戏窗口（按 _NET_WM_PID 找到游戏进程的 Wine 环境，按标题找窗口）</summary>
    public static WinePostMessageChannel ForWindow(IPlatformWindow window, Func<(int Width, int Height)> screenSize, Func<bool>? activateBeforeInput = null) =>
        new(() =>
            {
                var client = WinePostMessageClient.TryStart(window.ProcessId, window.Title, null, out var error, timeoutMs: 15000);
                return (client, error);
            },
            () => window.Viewport.ScreenRect, screenSize, activateBeforeInput);

    public override InputCapabilities Capabilities { get; } = new(
        RequiresForeground: false,
        SupportsRelativeMotion: false,
        SupportsKeyStateQuery: false);

    /// <summary>最近一次换算出的客户区坐标（后续点击的位置）</summary>
    public (int X, int Y) Pointer => _pointer;

    /// <summary>辅助程序在运行；没有运行时按间隔重新启动。返回 false 表示当前不可用</summary>
    public bool EnsureStarted()
    {
        lock (_startLock)
        {
            if (_disposed) return false;
            if (_sink is { IsAlive: true }) return true;
            if (_sink is not null)
            {
                Logger.LogWarning("后台输入辅助程序已退出，改用前台回退通道");
                _sink.Dispose();
                _sink = null;
                _lastFailure = Environment.TickCount64;
                return false;
            }

            if (_lastFailure != long.MinValue && Environment.TickCount64 - _lastFailure < RetryIntervalMs) return false;
            (IWinePostSink? Sink, string? Error) result;
            try
            {
                result = _start();
            }
            catch (Exception e)
            {
                result = (null, e.Message);
            }

            if (result.Sink is null)
            {
                _lastFailure = Environment.TickCount64;
                Logger.LogWarning("Wine 后台输入不可用，后台操作只在游戏位于前台时生效：{Error}", result.Error ?? "未知原因");
                return false;
            }

            _sink = result.Sink;
            Logger.LogInformation("Wine 后台输入已就绪（PostMessage，{Mode}）", _activateBeforeInput() ? "操作前投递 WM_ACTIVATE" : "不投递 WM_ACTIVATE");
            return true;
        }
    }

    /// <summary>只在辅助程序已启动时抬起按下的键，不为此启动它</summary>
    public override void ReleaseAll()
    {
        if (Volatile.Read(ref _sink) is { IsAlive: true }) base.ReleaseAll();
    }

    private bool Post(uint msg, ulong wParam, ulong lParam, uint flags = 0)
    {
        var sink = Volatile.Read(ref _sink);
        return sink is not null && sink.Post(msg, wParam, lParam, flags);
    }

    /// <summary>可用时（按设置）先投递 WM_ACTIVATE，与 PostMessageChannel.TryPrepare 相同</summary>
    private bool TryPrepare(string operation)
    {
        if (!EnsureStarted())
        {
            WarnUnsupported($"Wine 后台{operation}（辅助程序不可用）");
            return false;
        }

        if (_activateBeforeInput())
        {
            Post(WM_ACTIVATE, 1, 0, WinePostMessageClient.OnlyIfBackground);
        }

        return true;
    }

    /// <summary>按键 lParam：重复次数 1，抬起带第 30、31 位；扫描码与扩展键位由辅助程序按 Wine 的键盘布局填入</summary>
    public static ulong KeyLParam(bool keyUp) => keyUp ? 0xC0000001u : 1u;

    public static ulong PointLParam(int x, int y) => (uint)((y << 16) | (x & 0xFFFF));

    private void Key(uint msg, GameKey key, bool keyUp) => Post(msg, (ulong)key, KeyLParam(keyUp), WinePostMessageClient.FillScanCode);

    protected override void OnKeyDown(GameKey key)
    {
        if (TryPrepare("按键")) Key(WM_KEYDOWN, key, false);
    }

    protected override void OnKeyUp(GameKey key)
    {
        if (TryPrepare("按键")) Key(WM_KEYUP, key, true);
    }

    /// <summary>与 PostMessageSimulator.KeyPress 相同：WM_KEYDOWN、WM_CHAR、WM_KEYUP</summary>
    protected override void OnKeyPress(GameKey key)
    {
        if (!TryPrepare("按键")) return;
        Key(WM_KEYDOWN, key, false);
        Key(WM_CHAR, key, false);
        Key(WM_KEYUP, key, true);
    }

    private bool IsSupported(InputMouseButton button)
    {
        if (button is InputMouseButton.Left or InputMouseButton.Right) return true;
        WarnUnsupported($"后台鼠标键 {button}");
        return false;
    }

    protected override void OnMouseButton(InputMouseButton button, bool down)
    {
        if (!IsSupported(button) || !TryPrepare("鼠标")) return;
        var msg = (button, down) switch
        {
            (InputMouseButton.Left, true) => WM_LBUTTONDOWN,
            (InputMouseButton.Left, false) => WM_LBUTTONUP,
            (InputMouseButton.Right, true) => WM_RBUTTONDOWN,
            _ => WM_RBUTTONUP,
        };
        Post(msg, 0, PointLParam(_pointer.X, _pointer.Y));
    }

    protected override void OnMouseClick(InputMouseButton button)
    {
        if (!IsSupported(button) || !TryPrepare("鼠标")) return;
        var p = PointLParam(_pointer.X, _pointer.Y);
        var left = button == InputMouseButton.Left;
        Post(left ? WM_LBUTTONDOWN : WM_RBUTTONDOWN, 0, p);
        Thread.Sleep(ClickHoldMs);
        Post(left ? WM_LBUTTONUP : WM_RBUTTONUP, 0, p);
    }

    protected override void OnMoveBy(int dx, int dy) => WarnUnsupported("后台相对移动 MoveMouseBy");

    /// <summary>0~65535 归一化坐标按屏幕尺寸换算成像素，再换算成客户区坐标，记为后续点击的位置，不移动真实光标</summary>
    protected override void OnMoveTo(double absX, double absY)
    {
        var (w, h) = _screenSize();
        OnMoveToPixel((int)Math.Round(absX * w / 65535d), (int)Math.Round(absY * h / 65535d));
    }

    protected override void OnMoveToPixel(int screenX, int screenY)
    {
        var rect = _clientRect();
        _pointer = (screenX - rect.X, screenY - rect.Y);
    }

    protected override void OnScroll(int clicks) => WarnUnsupported("后台滚轮 VerticalScroll");

    public void Dispose()
    {
        lock (_startLock)
        {
            _disposed = true;
            _sink?.Dispose();
            _sink = null;
        }
    }
}
