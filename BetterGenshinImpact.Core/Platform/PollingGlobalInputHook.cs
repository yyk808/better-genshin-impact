using System;
using System.Collections.Generic;
using System.Threading;

namespace BetterGenshinImpact.Platform;

/// <summary>
/// 轮询 <see cref="IKeyStateProvider"/> 实现的全局键鼠监听：比较前后两次按键集合产生按下 / 抬起事件，
/// 光标坐标变化产生移动事件。没有系统钩子的平台（X11 无 XInput2 时、XWayland）使用。
/// 局限：两次轮询之间按下又抬起的键会丢失；观察不到滚轮
/// </summary>
public sealed class PollingGlobalInputHook : IGlobalInputHook
{
    private readonly IKeyStateProvider _state;
    private readonly TimeSpan _interval;
    private readonly object _gate = new();
    private readonly Thread? _thread;
    private volatile bool _disposed;
    private HashSet<GameKey> _pressed = [];
    private PixelPoint? _cursor;
    private bool _primed;
    private readonly bool _ownsState;

    /// <param name="state">键状态来源</param>
    /// <param name="interval">轮询间隔，默认 8ms</param>
    /// <param name="autoStart">为 false 时不启动后台线程，由调用方调用 <see cref="Poll"/>（测试用）</param>
    /// <param name="ownsState">Dispose 时一并释放实现了 IDisposable 的 <paramref name="state"/></param>
    public PollingGlobalInputHook(IKeyStateProvider state, TimeSpan? interval = null, bool autoStart = true, bool ownsState = false)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _ownsState = ownsState;
        _interval = interval ?? TimeSpan.FromMilliseconds(8);
        if (autoStart)
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "PollingGlobalInputHook" };
            _thread.Start();
        }
    }

    public event EventHandler<GlobalKeyEventArgs>? KeyDown;
    public event EventHandler<GlobalKeyEventArgs>? KeyUp;
    public event EventHandler<GlobalMouseEventArgs>? MouseDown;
    public event EventHandler<GlobalMouseEventArgs>? MouseUp;
    public event EventHandler<GlobalMouseEventArgs>? MouseMove;
#pragma warning disable CS0067 // 轮询观察不到滚轮
    public event EventHandler<GlobalMouseEventArgs>? MouseWheel;
#pragma warning restore CS0067

    public bool SupportsWheel => false;

    /// <summary>
    /// 轮询一次并触发事件。第一次调用只记录初始状态（启动时已经按住的键不产生按下事件）
    /// </summary>
    public void Poll()
    {
        lock (_gate)
        {
            if (_disposed) return;
            var now = new HashSet<GameKey>(_state.GetPressedKeys());
            var cursor = _state.GetCursorPosition();
            if (!_primed)
            {
                _pressed = now;
                _cursor = cursor;
                _primed = true;
                return;
            }

            var ts = Environment.TickCount;
            var location = cursor ?? _cursor ?? default;

            if (cursor is { } c && _cursor != c)
            {
                MouseMove?.Invoke(this, new GlobalMouseEventArgs(GameKey.None, c, 0, ts));
            }

            // 先抬起后按下：同一轮里换键时顺序与真实操作一致的可能更大
            foreach (var key in _pressed)
            {
                if (now.Contains(key) || IsShadowedGenericModifier(key, _pressed)) continue;
                if (KeyModifierHelper.IsMouseButton(key))
                    MouseUp?.Invoke(this, new GlobalMouseEventArgs(key, location, 0, ts));
                else
                    KeyUp?.Invoke(this, new GlobalKeyEventArgs(key, KeyModifierHelper.FromPressedKeys(now), ts));
            }

            foreach (var key in Sorted(now))
            {
                if (_pressed.Contains(key) || IsShadowedGenericModifier(key, now)) continue;
                if (KeyModifierHelper.IsMouseButton(key))
                    MouseDown?.Invoke(this, new GlobalMouseEventArgs(key, location, 0, ts));
                else
                    KeyDown?.Invoke(this, new GlobalKeyEventArgs(key, KeyModifierHelper.FromPressedKeys(now), ts));
            }

            _pressed = now;
            if (cursor is not null) _cursor = cursor;
        }
    }

    /// <summary>修饰键先于普通键，使 "Ctrl+C" 同一轮按下时先报 Ctrl</summary>
    private static List<GameKey> Sorted(HashSet<GameKey> keys)
    {
        var list = new List<GameKey>(keys);
        list.Sort((a, b) =>
        {
            var ma = IsModifier(a) ? 0 : 1;
            var mb = IsModifier(b) ? 0 : 1;
            return ma != mb ? ma.CompareTo(mb) : a.CompareTo(b);
        });
        return list;
    }

    /// <summary>
    /// 状态来源同时报告通用修饰键（VK_SHIFT）与左右键（VK_LSHIFT）时，只为左右键产生事件，与 Windows 低级钩子一致
    /// </summary>
    private static bool IsShadowedGenericModifier(GameKey key, HashSet<GameKey> set) => key switch
    {
        GameKey.VK_SHIFT => set.Contains(GameKey.VK_LSHIFT) || set.Contains(GameKey.VK_RSHIFT),
        GameKey.VK_CONTROL => set.Contains(GameKey.VK_LCONTROL) || set.Contains(GameKey.VK_RCONTROL),
        GameKey.VK_MENU => set.Contains(GameKey.VK_LMENU) || set.Contains(GameKey.VK_RMENU),
        _ => false,
    };

    private static bool IsModifier(GameKey k) => k is GameKey.VK_SHIFT or GameKey.VK_LSHIFT or GameKey.VK_RSHIFT
        or GameKey.VK_CONTROL or GameKey.VK_LCONTROL or GameKey.VK_RCONTROL
        or GameKey.VK_MENU or GameKey.VK_LMENU or GameKey.VK_RMENU or GameKey.VK_LWIN or GameKey.VK_RWIN;

    private void Run()
    {
        while (!_disposed)
        {
            try
            {
                Poll();
            }
            catch (Exception)
            {
                // 订阅方或状态来源出错时不让后台线程退出
            }

            Thread.Sleep(_interval);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_ownsState && _state is IDisposable d)
            {
                d.Dispose();
            }
        }
    }
}
