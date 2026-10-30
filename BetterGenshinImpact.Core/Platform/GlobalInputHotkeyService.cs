using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace BetterGenshinImpact.Platform;

/// <summary>
/// 基于全局键鼠监听（<see cref="IGlobalInputHook"/>，不拦截按键）的热键：相当于 Windows 快捷键的“键盘监听”方式，
/// 前台程序（游戏）照常收到按键。按键与修饰键完全一致时触发；按住不放只触发一次。
/// 回调按事件顺序在同一个后台线程上执行（不阻塞监听线程，长按的按下 / 抬起不会乱序）
/// </summary>
public sealed class GlobalInputHotkeyService : IHotkeyService, IHotkeyHoldService
{
    private readonly IGlobalInputHook _hook;
    private readonly object _gate = new();
    private readonly List<Entry> _entries = [];
    private readonly HashSet<GameKey> _down = [];
    private readonly Dictionary<GameKey, List<Entry>> _holding = [];
    private readonly BlockingCollection<Action> _callbacks = new();
    private readonly Thread _worker;

    private sealed record Entry(HotkeyGesture Gesture, Action<HotkeyGesture> OnDown, Action<HotkeyGesture>? OnUp, bool NoRepeat);

    public GlobalInputHotkeyService(IGlobalInputHook hook)
    {
        _hook = hook ?? throw new ArgumentNullException(nameof(hook));
        _hook.KeyDown += OnKeyDown;
        _hook.KeyUp += OnKeyUp;
        _worker = new Thread(() =>
        {
            foreach (var a in _callbacks.GetConsumingEnumerable())
            {
                try
                {
                    a();
                }
                catch (Exception)
                {
                    // 回调自身负责记录异常
                }
            }
        }) { IsBackground = true, Name = "GlobalInputHotkey" };
        _worker.Start();
    }

    public IDisposable Register(HotkeyGesture hotkey, Action<HotkeyGesture> onPressed, bool noRepeat = false) =>
        Add(new Entry(hotkey, onPressed ?? throw new ArgumentNullException(nameof(onPressed)), null, noRepeat));

    public IDisposable RegisterHold(HotkeyGesture hotkey, Action<HotkeyGesture> onDown, Action<HotkeyGesture> onUp) =>
        Add(new Entry(hotkey, onDown ?? throw new ArgumentNullException(nameof(onDown)), onUp ?? throw new ArgumentNullException(nameof(onUp)), true));

    private IDisposable Add(Entry entry)
    {
        lock (_gate)
        {
            _entries.Add(entry);
        }

        return new Registration(this, entry);
    }

    private void Remove(Entry entry)
    {
        lock (_gate)
        {
            _entries.Remove(entry);
            foreach (var (key, list) in _holding)
            {
                if (list.Remove(entry) && entry.OnUp is { } up) Post(() => up(entry.Gesture));
            }
        }
    }

    private void OnKeyDown(object? sender, GlobalKeyEventArgs e)
    {
        // 本进程自己注入的按键（键鼠回放、宏）不触发快捷键，避免回环
        if (e.IsSelfInjected) return;
        lock (_gate)
        {
            var repeat = !_down.Add(e.Key);
            foreach (var entry in _entries)
            {
                if (entry.Gesture.Key != e.Key || entry.Gesture.Modifiers != e.Modifiers || (repeat && entry.NoRepeat)) continue;
                if (entry.OnUp != null)
                {
                    if (!_holding.TryGetValue(e.Key, out var list)) _holding[e.Key] = list = [];
                    list.Add(entry);
                }

                var cb = entry.OnDown;
                var g = entry.Gesture;
                Post(() => cb(g));
            }
        }
    }

    private void OnKeyUp(object? sender, GlobalKeyEventArgs e)
    {
        if (e.IsSelfInjected) return;
        lock (_gate)
        {
            _down.Remove(e.Key);
            if (!_holding.Remove(e.Key, out var list)) return;
            foreach (var entry in list)
            {
                var up = entry.OnUp!;
                var g = entry.Gesture;
                Post(() => up(g));
            }
        }
    }

    private void Post(Action action)
    {
        if (!_callbacks.IsAddingCompleted) _callbacks.Add(action);
    }

    /// <summary>注销全部热键并停止回调线程（不释放监听本身，监听由调用方管理）</summary>
    public void Dispose()
    {
        _hook.KeyDown -= OnKeyDown;
        _hook.KeyUp -= OnKeyUp;
        lock (_gate)
        {
            _entries.Clear();
            _holding.Clear();
        }

        _callbacks.CompleteAdding();
    }

    private sealed class Registration(GlobalInputHotkeyService owner, Entry entry) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Remove(entry);
        }
    }
}
