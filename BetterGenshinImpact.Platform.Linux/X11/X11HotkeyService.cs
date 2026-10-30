using System.Collections.Concurrent;
using BetterGenshinImpact.Platform.Linux.X11.Native;
using static BetterGenshinImpact.Platform.Linux.X11.Native.Xlib;

namespace BetterGenshinImpact.Platform.Linux.X11;

/// <summary>
/// XGrabKey 全局热键：独立 Display 连接 + 事件线程（Xlib 连接只在该线程使用）。
/// 每个热键同时抓取 NumLock/CapsLock 的组合，避免锁定键开启时失效。
/// 被动抓取会“吞掉”按键，前台窗口（游戏）收不到该热键，这与 Windows RegisterHotKey 一致；
/// 抓取期间游戏会收到 mode=NotifyGrab 的 FocusOut，不应当成失焦处理。回调在线程池上触发
/// </summary>
public sealed unsafe class X11HotkeyService : IHotkeyService
{
    private static readonly uint[] LockVariants = [0, LockMask, Mod2Mask, LockMask | Mod2Mask];

    private readonly X11Connection _c;
    private readonly Thread _thread;
    private readonly ConcurrentQueue<Action> _ops = new();
    private readonly Dictionary<(int code, uint mods), (HotkeyGesture hk, Action<HotkeyGesture> cb)> _map = new();
    private volatile bool _stop;

    public X11HotkeyService(string? display = null)
    {
        _c = new X11Connection(display);
        _thread = new Thread(Loop) { IsBackground = true, Name = "X11Hotkey" };
        _thread.Start();
    }

    private static uint ToX(KeyModifiers m) =>
        (m.HasFlag(KeyModifiers.Shift) ? ShiftMask : 0) | (m.HasFlag(KeyModifiers.Control) ? ControlMask : 0) |
        (m.HasFlag(KeyModifiers.Alt) ? Mod1Mask : 0) | (m.HasFlag(KeyModifiers.Super) ? Mod4Mask : 0);

    /// <summary>在事件线程上执行并等待结果</summary>
    private T Invoke<T>(Func<T> f)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ops.Enqueue(() =>
        {
            try
            {
                tcs.SetResult(f());
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        });
        return tcs.Task.GetAwaiter().GetResult();
    }

    public IDisposable Register(HotkeyGesture hotkey, Action<HotkeyGesture> onPressed)
    {
        ArgumentNullException.ThrowIfNull(onPressed);
        var key = Invoke(() =>
        {
            var code = KeyMap.ToKeyCode(_c.Display, hotkey.Key);
            if (code == 0) throw new InvalidOperationException($"键 {hotkey.Key} 没有 keycode");
            var mods = ToX(hotkey.Modifiers);
            LastErrorCode = 0;
            foreach (var v in LockVariants) XGrabKey(_c.Display, code, mods | v, _c.Root, true, GrabModeAsync, GrabModeAsync);
            XSync(_c.Display, false);
            if (LastErrorCode != 0)
            {
                foreach (var v in LockVariants) XUngrabKey(_c.Display, code, mods | v, _c.Root);
                XSync(_c.Display, false);
                throw new InvalidOperationException($"XGrabKey {hotkey} 失败（X 错误 {LastErrorCode}，10=BadAccess 表示已被其它程序占用）");
            }

            _map[(code, mods)] = (hotkey, onPressed);
            return (code, mods);
        });
        return new Registration(this, key);
    }

    private sealed class Registration(X11HotkeyService owner, (int code, uint mods) key) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0 || owner._stop) return;
            owner.Invoke(() =>
            {
                foreach (var v in LockVariants) XUngrabKey(owner._c.Display, key.code, key.mods | v, owner._c.Root);
                XSync(owner._c.Display, false);
                owner._map.Remove(key);
                return 0;
            });
        }
    }

    private void Loop()
    {
        var ev = stackalloc byte[192];
        var fd = new PollFd { fd = XConnectionNumber(_c.Display), events = 1 };
        while (!_stop)
        {
            while (_ops.TryDequeue(out var op)) op();
            if (XPending(_c.Display) == 0)
            {
                fd.revents = 0;
                poll(&fd, 1, 20);
                if (XPending(_c.Display) == 0) continue;
            }

            XNextEvent(_c.Display, ev);
            if (*(int*)ev == KeyPress)
            {
                // XKeyEvent（x86_64）：state 在偏移 80，keycode 在 84
                uint state = *(uint*)(ev + 80) & ~(LockMask | Mod2Mask);
                int code = *(int*)(ev + 84);
                if (_map.TryGetValue((code, state), out var e))
                {
                    var (hk, cb) = e;
                    ThreadPool.QueueUserWorkItem(_ => cb(hk));
                }
            }
        }
    }

    public void Dispose()
    {
        _stop = true;
        _thread.Join(500);
        _c.Dispose();
    }
}
