using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Platform;

/// <summary>全局键盘事件</summary>
/// <param name="Key">按键（VK 码）。左右修饰键报告为 VK_LSHIFT / VK_RCONTROL 等</param>
/// <param name="Modifiers">事件发生时按住的修饰键（Shift / Ctrl / Alt；Win 键不计入，与 WinForms Keys 一致）</param>
/// <param name="Timestamp">系统时间戳（毫秒，仅用于先后比较，不同平台起点不同）</param>
public sealed record GlobalKeyEventArgs(GameKey Key, KeyModifiers Modifiers, int Timestamp);

/// <summary>全局鼠标事件</summary>
/// <param name="Button">VK_LBUTTON / VK_RBUTTON / VK_MBUTTON / VK_XBUTTON1 / VK_XBUTTON2；移动与滚轮事件为 <see cref="GameKey.None"/></param>
/// <param name="Location">屏幕坐标（物理像素）</param>
/// <param name="Delta">滚轮增量（120 的倍数，向上为正）；其它事件为 0</param>
/// <param name="Timestamp">系统时间戳（毫秒）</param>
public sealed record GlobalMouseEventArgs(GameKey Button, PixelPoint Location, int Delta, int Timestamp);

/// <summary>
/// 全局键鼠监听（不拦截）。Windows 为低级键鼠钩子（MouseKeyHook）；X11 为轮询键状态。
/// 事件在实现自己的线程上触发，订阅方不能长时间阻塞
/// </summary>
public interface IGlobalInputHook : IDisposable
{
    event EventHandler<GlobalKeyEventArgs>? KeyDown;
    event EventHandler<GlobalKeyEventArgs>? KeyUp;
    event EventHandler<GlobalMouseEventArgs>? MouseDown;
    event EventHandler<GlobalMouseEventArgs>? MouseUp;
    event EventHandler<GlobalMouseEventArgs>? MouseMove;

    /// <summary>垂直滚轮。轮询实现无法观察滚轮，不会触发</summary>
    event EventHandler<GlobalMouseEventArgs>? MouseWheel;

    /// <summary>是否能观察到滚轮事件</summary>
    bool SupportsWheel { get; }
}

/// <summary>
/// 系统（物理）键鼠状态，相当于 Win32 GetAsyncKeyState / X11 XQueryKeymap。
/// 与 <c>IInputChannel.IsKeyDown</c> 不同，这里反映的是整个系统的状态（含用户真实按键）
/// </summary>
public interface IKeyStateProvider
{
    /// <summary>键或鼠标按钮当前是否按下</summary>
    bool IsKeyDown(GameKey key);

    /// <summary>当前按下的全部键与鼠标按钮</summary>
    IReadOnlySet<GameKey> GetPressedKeys();

    /// <summary>光标的屏幕坐标；无法获取时为 null</summary>
    PixelPoint? GetCursorPosition();
}

/// <summary>修饰键换算</summary>
public static class KeyModifierHelper
{
    /// <summary>按下的键集合中包含的修饰键（Shift / Ctrl / Alt，含左右键）</summary>
    public static KeyModifiers FromPressedKeys(IReadOnlySet<GameKey> pressed)
    {
        var m = KeyModifiers.None;
        if (pressed.Contains(GameKey.VK_SHIFT) || pressed.Contains(GameKey.VK_LSHIFT) || pressed.Contains(GameKey.VK_RSHIFT))
            m |= KeyModifiers.Shift;
        if (pressed.Contains(GameKey.VK_CONTROL) || pressed.Contains(GameKey.VK_LCONTROL) || pressed.Contains(GameKey.VK_RCONTROL))
            m |= KeyModifiers.Control;
        if (pressed.Contains(GameKey.VK_MENU) || pressed.Contains(GameKey.VK_LMENU) || pressed.Contains(GameKey.VK_RMENU))
            m |= KeyModifiers.Alt;
        return m;
    }

    /// <summary>
    /// 鼠标按钮名称，与 WinForms <c>MouseButtons.ToString()</c> 相同（Left / Right / Middle / XButton1 / XButton2 / None），
    /// 键鼠录制脚本中保存的就是这个名称
    /// </summary>
    public static string MouseButtonName(GameKey button) => button switch
    {
        GameKey.VK_LBUTTON => "Left",
        GameKey.VK_RBUTTON => "Right",
        GameKey.VK_MBUTTON => "Middle",
        GameKey.VK_XBUTTON1 => "XButton1",
        GameKey.VK_XBUTTON2 => "XButton2",
        _ => "None",
    };

    /// <summary>是否为鼠标按钮</summary>
    public static bool IsMouseButton(GameKey key) =>
        key is GameKey.VK_LBUTTON or GameKey.VK_RBUTTON or GameKey.VK_MBUTTON or GameKey.VK_XBUTTON1 or GameKey.VK_XBUTTON2;
}
