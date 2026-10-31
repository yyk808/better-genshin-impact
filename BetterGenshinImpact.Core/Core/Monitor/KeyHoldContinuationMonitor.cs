using System;
using System.Diagnostics;
using System.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Platform;

namespace BetterGenshinImpact.Core.Monitor;

/// <summary>
/// 辅助操控的长按连发：“长按空格等于连续按下空格”“长按 F 等于连续按下 F”（MacroConfig 开关与间隔、KeyBindingsConfig 跳跃 / 拾取键）。
/// 规则与 WPF MouseKeyMonitor 相同：游戏在前台且开关打开时，跳跃键按住超过 300 毫秒（拾取键 200 毫秒）后按配置间隔连续发送该键，松开即停。
/// 供没有低级键盘钩子自动重复的宿主使用（Linux 的 XInput2 原始事件没有自动重复，因此按按下时长计时）；
/// 自己发送的按键在全局监听里标记为 IsSelfInjected，忽略这些事件，避免把自己的抬起当成松开。
/// </summary>
public sealed class KeyHoldContinuationMonitor : IDisposable
{
    private readonly IGlobalInputHook _hook;
    private readonly Func<bool> _isGameForeground;
    private readonly Action<GameKey> _press;
    private readonly Slot[] _slots;

    /// <param name="hook">全局键鼠监听</param>
    /// <param name="config">配置（读取时取最新值，修改开关或按键后立即生效）</param>
    /// <param name="isGameForeground">游戏是否在前台</param>
    /// <param name="press">发送一次按键（按下并抬起）</param>
    public KeyHoldContinuationMonitor(IGlobalInputHook hook, Func<AllConfig> config, Func<bool> isGameForeground, Action<GameKey> press)
    {
        _hook = hook;
        _isGameForeground = isGameForeground;
        _press = press;
        _slots =
        [
            new Slot(this, () => config().KeyBindingsConfig.Jump.ToVK(), 300,
                () => config().MacroConfig.SpacePressHoldToContinuationEnabled, () => config().MacroConfig.SpaceFireInterval),
            new Slot(this, () => config().KeyBindingsConfig.PickUpOrInteract.ToVK(), 200,
                () => config().MacroConfig.FPressHoldToContinuationEnabled, () => config().MacroConfig.FFireInterval),
        ];
        _hook.KeyDown += OnKeyDown;
        _hook.KeyUp += OnKeyUp;
    }

    /// <summary>已发送的连发次数（自检用）</summary>
    public int FiredCount => _slots[0].Fired + _slots[1].Fired;

    private void OnKeyDown(object? sender, GlobalKeyEventArgs e)
    {
        if (e.IsSelfInjected) return;
        foreach (var slot in _slots)
        {
            if (slot.Matches(e.Key)) slot.Down();
        }
    }

    private void OnKeyUp(object? sender, GlobalKeyEventArgs e)
    {
        if (e.IsSelfInjected) return;
        foreach (var slot in _slots)
        {
            if (slot.Matches(e.Key)) slot.Up();
        }
    }

    public void Dispose()
    {
        _hook.KeyDown -= OnKeyDown;
        _hook.KeyUp -= OnKeyUp;
        foreach (var slot in _slots) slot.Dispose();
    }

    private sealed class Slot : IDisposable
    {
        private readonly KeyHoldContinuationMonitor _owner;
        private readonly Func<GameKey> _key;
        private readonly int _thresholdMs;
        private readonly Func<bool> _enabled;
        private readonly Func<int> _interval;
        private readonly Timer _timer;
        private readonly object _lock = new();
        private GameKey _heldKey = GameKey.None;
        private int _fired;

        public Slot(KeyHoldContinuationMonitor owner, Func<GameKey> key, int thresholdMs, Func<bool> enabled, Func<int> interval)
        {
            _owner = owner;
            _key = key;
            _thresholdMs = thresholdMs;
            _enabled = enabled;
            _interval = interval;
            _timer = new Timer(_ => Fire());
        }

        public int Fired => Volatile.Read(ref _fired);

        public bool Matches(GameKey key)
        {
            try
            {
                return key != GameKey.None && key == _key();
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void Down()
        {
            lock (_lock)
            {
                // 已在计时（重复的按下）不重新开始
                if (_heldKey != GameKey.None || !_enabled() || !_owner._isGameForeground()) return;
                _heldKey = _key();
                // 与 WPF 相同：间隔小于 1 毫秒时按 1 毫秒处理
                _timer.Change(_thresholdMs, Math.Max(1, _interval()));
            }
        }

        public void Up()
        {
            lock (_lock)
            {
                _heldKey = GameKey.None;
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        private void Fire()
        {
            GameKey key;
            lock (_lock)
            {
                key = _heldKey;
                if (key == GameKey.None) return;
                // 切出游戏或关闭开关后停止
                if (!_enabled() || !_owner._isGameForeground())
                {
                    _heldKey = GameKey.None;
                    _timer.Change(Timeout.Infinite, Timeout.Infinite);
                    return;
                }
            }

            try
            {
                _owner._press(key);
                Interlocked.Increment(ref _fired);
            }
            catch (Exception e)
            {
                Debug.WriteLine("长按连发发送失败：" + e.Message);
            }
        }

        public void Dispose() => _timer.Dispose();
    }
}
