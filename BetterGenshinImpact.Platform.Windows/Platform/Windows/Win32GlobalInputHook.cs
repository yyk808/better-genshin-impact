using Gma.System.MouseKeyHook;
using System;
using System.Windows.Forms;

namespace BetterGenshinImpact.Platform.Windows;

/// <summary>
/// 低级键鼠钩子（MouseKeyHook 的 <c>Hook.GlobalEvents()</c>）。钩子装在创建它的线程上，
/// 该线程需要消息循环（UI 线程），事件也在该线程上同步触发，与原先直接使用 MouseKeyHook 相同
/// </summary>
public sealed class Win32GlobalInputHook : IGlobalInputHook
{
    private IKeyboardMouseEvents? _events;

    public Win32GlobalInputHook()
    {
        _events = Hook.GlobalEvents();
        _events.KeyDown += OnKeyDown;
        _events.KeyUp += OnKeyUp;
        _events.MouseDownExt += OnMouseDown;
        _events.MouseUpExt += OnMouseUp;
        _events.MouseMoveExt += OnMouseMove;
        _events.MouseWheelExt += OnMouseWheel;
    }

    public event EventHandler<GlobalKeyEventArgs>? KeyDown;
    public event EventHandler<GlobalKeyEventArgs>? KeyUp;
    public event EventHandler<GlobalMouseEventArgs>? MouseDown;
    public event EventHandler<GlobalMouseEventArgs>? MouseUp;
    public event EventHandler<GlobalMouseEventArgs>? MouseMove;
    public event EventHandler<GlobalMouseEventArgs>? MouseWheel;

    public bool SupportsWheel => true;

    private void OnKeyDown(object? sender, KeyEventArgs e) => KeyDown?.Invoke(this, ToArgs(e));

    private void OnKeyUp(object? sender, KeyEventArgs e) => KeyUp?.Invoke(this, ToArgs(e));

    private void OnMouseDown(object? sender, MouseEventExtArgs e) => MouseDown?.Invoke(this, ToArgs(e));

    private void OnMouseUp(object? sender, MouseEventExtArgs e) => MouseUp?.Invoke(this, ToArgs(e));

    private void OnMouseMove(object? sender, MouseEventExtArgs e) => MouseMove?.Invoke(this, ToArgs(e));

    private void OnMouseWheel(object? sender, MouseEventExtArgs e) => MouseWheel?.Invoke(this, ToArgs(e));

    private static GlobalKeyEventArgs ToArgs(KeyEventArgs e) =>
        new(Win32KeyConvert.ToGameKey(e.KeyCode), Win32KeyConvert.ToModifiers(e.Modifiers),
            e is KeyEventArgsExt ext ? ext.Timestamp : Environment.TickCount);

    private static GlobalMouseEventArgs ToArgs(MouseEventExtArgs e) =>
        new(Win32KeyConvert.ToGameKey(e.Button), new PixelPoint(e.X, e.Y), e.Delta, e.Timestamp);

    public void Dispose()
    {
        var events = _events;
        _events = null;
        if (events == null) return;
        events.KeyDown -= OnKeyDown;
        events.KeyUp -= OnKeyUp;
        events.MouseDownExt -= OnMouseDown;
        events.MouseUpExt -= OnMouseUp;
        events.MouseMoveExt -= OnMouseMove;
        events.MouseWheelExt -= OnMouseWheel;
        events.Dispose();
    }
}

/// <summary>WinForms Keys / MouseButtons 与 GameKey / KeyModifiers 的互相转换（VK 码相同）</summary>
public static class Win32KeyConvert
{
    public static GameKey ToGameKey(Keys keyCode) => (GameKey)(ushort)(keyCode & Keys.KeyCode);

    public static KeyModifiers ToModifiers(Keys modifiers)
    {
        var m = KeyModifiers.None;
        if ((modifiers & Keys.Shift) != 0) m |= KeyModifiers.Shift;
        if ((modifiers & Keys.Control) != 0) m |= KeyModifiers.Control;
        if ((modifiers & Keys.Alt) != 0) m |= KeyModifiers.Alt;
        return m;
    }

    /// <summary>GameKey + 修饰键 → WinForms KeyData（与 MouseKeyHook 给出的 KeyData 相同）</summary>
    public static Keys ToKeyData(GameKey key, KeyModifiers modifiers)
    {
        var k = (Keys)(ushort)key;
        if (modifiers.HasFlag(KeyModifiers.Shift)) k |= Keys.Shift;
        if (modifiers.HasFlag(KeyModifiers.Control)) k |= Keys.Control;
        if (modifiers.HasFlag(KeyModifiers.Alt)) k |= Keys.Alt;
        return k;
    }

    public static GameKey ToGameKey(MouseButtons button) => button switch
    {
        MouseButtons.Left => GameKey.VK_LBUTTON,
        MouseButtons.Right => GameKey.VK_RBUTTON,
        MouseButtons.Middle => GameKey.VK_MBUTTON,
        MouseButtons.XButton1 => GameKey.VK_XBUTTON1,
        MouseButtons.XButton2 => GameKey.VK_XBUTTON2,
        _ => GameKey.None,
    };

    public static MouseButtons ToMouseButtons(GameKey button) => button switch
    {
        GameKey.VK_LBUTTON => MouseButtons.Left,
        GameKey.VK_RBUTTON => MouseButtons.Right,
        GameKey.VK_MBUTTON => MouseButtons.Middle,
        GameKey.VK_XBUTTON1 => MouseButtons.XButton1,
        GameKey.VK_XBUTTON2 => MouseButtons.XButton2,
        _ => MouseButtons.None,
    };
}
