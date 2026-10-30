using Fischless.HotkeyCapture;
using System;
using System.Windows.Forms;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Platform.Windows;

/// <summary>
/// 全局热键：包装 Fischless.HotkeyCapture 的 RegisterHotKey。
/// 每次注册使用独立的 <see cref="HotkeyHook"/>（各自的消息窗口），Dispose 即注销。
/// 必须在有消息循环的线程（UI 线程）上注册，回调也在该线程上触发
/// </summary>
public sealed class Win32HotkeyService : IHotkeyService
{
    public IDisposable Register(HotkeyGesture hotkey, Action<HotkeyGesture> onPressed)
    {
        ArgumentNullException.ThrowIfNull(onPressed);
        var hook = new HotkeyHook();
        hook.KeyPressed += (_, _) => onPressed(hotkey);
        try
        {
            // WinForms Keys 的取值与 VK 相同
            hook.RegisterHotKey(ToModifiers(hotkey.Modifiers), (Keys)(int)hotkey.Key);
        }
        catch
        {
            hook.Dispose();
            throw;
        }

        return hook;
    }

    public static User32.HotKeyModifiers ToModifiers(KeyModifiers m) =>
        (m.HasFlag(KeyModifiers.Super) ? User32.HotKeyModifiers.MOD_WIN : User32.HotKeyModifiers.MOD_NONE) |
        (m.HasFlag(KeyModifiers.Control) ? User32.HotKeyModifiers.MOD_CONTROL : User32.HotKeyModifiers.MOD_NONE) |
        (m.HasFlag(KeyModifiers.Shift) ? User32.HotKeyModifiers.MOD_SHIFT : User32.HotKeyModifiers.MOD_NONE) |
        (m.HasFlag(KeyModifiers.Alt) ? User32.HotKeyModifiers.MOD_ALT : User32.HotKeyModifiers.MOD_NONE);

    public void Dispose()
    {
    }
}
