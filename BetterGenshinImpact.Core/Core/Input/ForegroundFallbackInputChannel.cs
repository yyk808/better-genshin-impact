using System;
using System.Collections.Generic;
using BetterGenshinImpact.Platform;

namespace BetterGenshinImpact.Core.Input;

/// <summary>
/// 后端没有后台通道时（Linux 原生）<see cref="InputHub.Background"/> 返回的通道：
/// 游戏窗口在前台时改走前台通道（此时与后台输入效果相同）；不在前台时跳过并限频告警，避免把输入发给其它窗口。
/// <para>
/// 抬起类操作只补发给经本通道按下过的键 / 按钮，保证切到后台后不会留下一直按着的键。
/// Win32 后端始终有后台通道，不会用到本类。
/// </para>
/// </summary>
public sealed class ForegroundFallbackInputChannel(Func<IInputChannel> foreground, Func<bool> isGameForeground)
    : InputChannelBase
{
    private const string SkipReason = "当前输入后端没有后台通道，且游戏窗口不在前台，已跳过";

    private readonly object _downLock = new();
    private readonly HashSet<GameKey> _keysDown = [];
    private readonly HashSet<InputMouseButton> _buttonsDown = [];

    public override InputCapabilities Capabilities => foreground().Capabilities;

    private bool CanForward(string operation)
    {
        if (isGameForeground())
        {
            return true;
        }

        WarnUnsupported($"{SkipReason}：{operation}");
        return false;
    }

    protected override void OnKeyDown(GameKey key)
    {
        if (!CanForward($"按下 {key}")) return;
        lock (_downLock) _keysDown.Add(key);
        foreground().Keyboard.KeyDown(key);
    }

    protected override void OnKeyUp(GameKey key)
    {
        bool wasDown;
        lock (_downLock) wasDown = _keysDown.Remove(key);
        if (wasDown || CanForward($"抬起 {key}"))
        {
            foreground().Keyboard.KeyUp(key);
        }
    }

    protected override void OnKeyPress(GameKey key)
    {
        if (CanForward($"按键 {key}")) foreground().Keyboard.KeyPress(key);
    }

    protected override void OnMouseButton(InputMouseButton button, bool down)
    {
        if (down)
        {
            if (!CanForward($"鼠标 {button} 按下")) return;
            lock (_downLock) _buttonsDown.Add(button);
        }
        else
        {
            bool wasDown;
            lock (_downLock) wasDown = _buttonsDown.Remove(button);
            if (!wasDown && !CanForward($"鼠标 {button} 抬起")) return;
        }

        var mouse = foreground().Mouse;
        switch (button, down)
        {
            case (InputMouseButton.Left, true): mouse.LeftButtonDown(); break;
            case (InputMouseButton.Left, false): mouse.LeftButtonUp(); break;
            case (InputMouseButton.Right, true): mouse.RightButtonDown(); break;
            case (InputMouseButton.Right, false): mouse.RightButtonUp(); break;
            case (InputMouseButton.Middle, true): mouse.MiddleButtonDown(); break;
            case (InputMouseButton.Middle, false): mouse.MiddleButtonUp(); break;
            case (InputMouseButton.X1, true): mouse.XButtonDown(1); break;
            case (InputMouseButton.X1, false): mouse.XButtonUp(1); break;
            case (_, true): mouse.XButtonDown(2); break;
            case (_, false): mouse.XButtonUp(2); break;
        }
    }

    protected override void OnMouseClick(InputMouseButton button)
    {
        if (!CanForward($"鼠标 {button} 点击")) return;
        var mouse = foreground().Mouse;
        switch (button)
        {
            case InputMouseButton.Left: mouse.LeftButtonClick(); break;
            case InputMouseButton.Right: mouse.RightButtonClick(); break;
            case InputMouseButton.Middle: mouse.MiddleButtonClick(); break;
            case InputMouseButton.X1: mouse.XButtonClick(1); break;
            default: mouse.XButtonClick(2); break;
        }
    }

    protected override void OnMoveBy(int dx, int dy)
    {
        if (CanForward("相对移动")) foreground().Mouse.MoveMouseBy(dx, dy);
    }

    protected override void OnMoveTo(double absX, double absY)
    {
        if (CanForward("绝对移动")) foreground().Mouse.MoveMouseTo(absX, absY);
    }

    protected override void OnMoveToPixel(int screenX, int screenY)
    {
        if (CanForward("绝对移动")) foreground().Mouse.MoveMouseToPixel(screenX, screenY);
    }

    protected override void OnScroll(int clicks)
    {
        if (CanForward("滚轮")) foreground().Mouse.VerticalScroll(clicks);
    }

    public override bool IsKeyDown(GameKey key) => foreground().IsKeyDown(key);
}
