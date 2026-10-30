using BetterGenshinImpact.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace BetterGenshinImpact.Core.Input;

/// <summary>
/// 输入通道基类。
/// 一个实例同时实现通道、键盘、鼠标三个接口，<see cref="Keyboard"/> 与 <see cref="Mouse"/> 都返回自身。
/// 负责：按下状态记录、鼠标键转发、不支持操作的限频 warn（GIActions 映射见主程序 InputChannelActionExtensions）。
/// 子类只需实现 On* 原子操作。
/// </summary>
public abstract class InputChannelBase : IInputChannel, IKeyboardInput, IMouseInput
{
    /// <summary>
    /// 同一种不支持的操作在此时间内只打一次 warn
    /// </summary>
    private const long WarnIntervalMs = 10_000;

    private static ILogger? _logger;

    /// <summary>
    /// 日志来源。Core 不依赖主程序的 App/DI，由宿主在启动时设置（主程序在模块初始化器里设为 App.GetLogger）；
    /// 未设置时不记录日志
    /// </summary>
    public static Func<ILogger>? LoggerFactory { get; set; }

    private readonly object _pressedLock = new();
    private readonly HashSet<GameKey> _pressed = [];
    private readonly ConcurrentDictionary<string, long> _lastWarnTicks = new();

    protected static ILogger Logger => _logger ?? (LoggerFactory is { } factory ? _logger = factory() : NullLogger.Instance);

    public IKeyboardInput Keyboard => this;

    public IMouseInput Mouse => this;

    #region 子类实现的原子操作

    protected abstract void OnKeyDown(GameKey key);

    protected abstract void OnKeyUp(GameKey key);

    /// <summary>
    /// 默认为按下 + 抬起，子类可覆写以保持各自的时序
    /// </summary>
    protected virtual void OnKeyPress(GameKey key)
    {
        OnKeyDown(key);
        OnKeyUp(key);
    }

    protected abstract void OnMouseButton(InputMouseButton button, bool down);

    /// <summary>
    /// 默认为按下 + 抬起，子类可覆写以保持各自的时序
    /// </summary>
    protected virtual void OnMouseClick(InputMouseButton button)
    {
        OnMouseButton(button, true);
        OnMouseButton(button, false);
    }

    protected abstract void OnMoveBy(int dx, int dy);

    protected abstract void OnMoveTo(double absX, double absY);

    /// <summary>
    /// 按虚拟桌面物理像素绝对定位。默认不支持，子类覆写
    /// </summary>
    protected virtual void OnMoveToPixel(int screenX, int screenY) => WarnUnsupported("按像素绝对定位");

    protected abstract void OnScroll(int clicks);

    #endregion

    #region IInputChannel

    /// <summary>
    /// 通道能力，子类按实际情况覆写
    /// </summary>
    public virtual InputCapabilities Capabilities => InputCapabilities.Default;

    public virtual bool IsKeyDown(GameKey key)
    {
        lock (_pressedLock)
        {
            return _pressed.Contains(key);
        }
    }

    IInputChannel IInputChannel.Sleep(int ms)
    {
        Thread.Sleep(ms);
        return this;
    }

    /// <summary>
    /// 按记录逐个抬起。单个按键失败不影响其余按键
    /// </summary>
    public virtual void ReleaseAll()
    {
        foreach (var key in TakePressedKeys())
        {
            try
            {
                if (TryGetMouseButton(key, out var button))
                {
                    OnMouseButton(button, false);
                }
                else
                {
                    OnKeyUp(key);
                }
            }
            catch (Exception e)
            {
                Logger.LogDebug(e, "释放按键 {Key} 失败", key);
            }
        }
    }

    #endregion

    #region IKeyboardInput

    public IKeyboardInput KeyDown(GameKey key)
    {
        if (TryGetMouseButton(key, out var button))
        {
            ButtonDown(button);
            return this;
        }

        OnKeyDown(key);
        SetPressed(key, true);
        return this;
    }

    public IKeyboardInput KeyUp(GameKey key)
    {
        if (TryGetMouseButton(key, out var button))
        {
            ButtonUp(button);
            return this;
        }

        SetPressed(key, false);
        OnKeyUp(key);
        return this;
    }

    public IKeyboardInput KeyPress(GameKey key)
    {
        if (TryGetMouseButton(key, out var button))
        {
            ButtonClick(button);
            return this;
        }

        SetPressed(key, false);
        OnKeyPress(key);
        return this;
    }

    IKeyboardInput IKeyboardInput.Sleep(int ms)
    {
        Thread.Sleep(ms);
        return this;
    }

    #endregion

    #region IMouseInput

    public IMouseInput MoveMouseBy(int dx, int dy)
    {
        OnMoveBy(dx, dy);
        return this;
    }

    public IMouseInput MoveMouseTo(double absX, double absY)
    {
        OnMoveTo(absX, absY);
        return this;
    }

    public IMouseInput MoveMouseToPixel(int screenX, int screenY)
    {
        OnMoveToPixel(screenX, screenY);
        return this;
    }

    public IMouseInput LeftButtonDown() => ButtonDown(InputMouseButton.Left);

    public IMouseInput LeftButtonUp() => ButtonUp(InputMouseButton.Left);

    public IMouseInput LeftButtonClick() => ButtonClick(InputMouseButton.Left);

    public IMouseInput RightButtonDown() => ButtonDown(InputMouseButton.Right);

    public IMouseInput RightButtonUp() => ButtonUp(InputMouseButton.Right);

    public IMouseInput RightButtonClick() => ButtonClick(InputMouseButton.Right);

    public IMouseInput MiddleButtonDown() => ButtonDown(InputMouseButton.Middle);

    public IMouseInput MiddleButtonUp() => ButtonUp(InputMouseButton.Middle);

    public IMouseInput MiddleButtonClick() => ButtonClick(InputMouseButton.Middle);

    public IMouseInput XButtonDown(int buttonId) => TryGetXButton(buttonId, out var b) ? ButtonDown(b) : this;

    public IMouseInput XButtonUp(int buttonId) => TryGetXButton(buttonId, out var b) ? ButtonUp(b) : this;

    public IMouseInput XButtonClick(int buttonId) => TryGetXButton(buttonId, out var b) ? ButtonClick(b) : this;

    public IMouseInput VerticalScroll(int scrollAmountInClicks)
    {
        OnScroll(scrollAmountInClicks);
        return this;
    }

    IMouseInput IMouseInput.Sleep(int ms)
    {
        Thread.Sleep(ms);
        return this;
    }

    #endregion

    #region 辅助

    /// <summary>
    /// 不支持的操作：限频打 warn 后直接忽略
    /// </summary>
    protected void WarnUnsupported(string operation)
    {
        var now = Environment.TickCount64;
        var last = _lastWarnTicks.GetOrAdd(operation, long.MinValue);
        if (last != long.MinValue && now - last < WarnIntervalMs)
        {
            return;
        }

        _lastWarnTicks[operation] = now;
        Logger.LogWarning("[{Channel}] 不支持的输入操作已忽略：{Operation}", GetType().Name, operation);
    }

    /// <summary>
    /// 取出并清空按下记录
    /// </summary>
    protected IReadOnlyList<GameKey> TakePressedKeys()
    {
        lock (_pressedLock)
        {
            var keys = _pressed.ToList();
            _pressed.Clear();
            return keys;
        }
    }

    protected static bool TryGetMouseButton(GameKey key, out InputMouseButton button)
    {
        switch (key)
        {
            case GameKey.VK_LBUTTON:
                button = InputMouseButton.Left;
                return true;
            case GameKey.VK_RBUTTON:
                button = InputMouseButton.Right;
                return true;
            case GameKey.VK_MBUTTON:
                button = InputMouseButton.Middle;
                return true;
            case GameKey.VK_XBUTTON1:
                button = InputMouseButton.X1;
                return true;
            case GameKey.VK_XBUTTON2:
                button = InputMouseButton.X2;
                return true;
            default:
                button = default;
                return false;
        }
    }

    protected static GameKey ToVirtualKey(InputMouseButton button) => button switch
    {
        InputMouseButton.Left => GameKey.VK_LBUTTON,
        InputMouseButton.Right => GameKey.VK_RBUTTON,
        InputMouseButton.Middle => GameKey.VK_MBUTTON,
        InputMouseButton.X1 => GameKey.VK_XBUTTON1,
        InputMouseButton.X2 => GameKey.VK_XBUTTON2,
        _ => throw new ArgumentOutOfRangeException(nameof(button), button, null),
    };

    private bool TryGetXButton(int buttonId, out InputMouseButton button)
    {
        switch (buttonId)
        {
            case 1:
                button = InputMouseButton.X1;
                return true;
            case 2:
                button = InputMouseButton.X2;
                return true;
            default:
                button = default;
                WarnUnsupported($"侧键编号 {buttonId}");
                return false;
        }
    }

    private InputChannelBase ButtonDown(InputMouseButton button)
    {
        OnMouseButton(button, true);
        SetPressed(ToVirtualKey(button), true);
        return this;
    }

    private InputChannelBase ButtonUp(InputMouseButton button)
    {
        SetPressed(ToVirtualKey(button), false);
        OnMouseButton(button, false);
        return this;
    }

    private InputChannelBase ButtonClick(InputMouseButton button)
    {
        SetPressed(ToVirtualKey(button), false);
        OnMouseClick(button);
        return this;
    }

    private void SetPressed(GameKey key, bool pressed)
    {
        lock (_pressedLock)
        {
            if (pressed)
            {
                _pressed.Add(key);
            }
            else
            {
                _pressed.Remove(key);
            }
        }
    }

    #endregion
}
