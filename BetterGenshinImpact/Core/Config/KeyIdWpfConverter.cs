using System;
using System.Windows.Forms;
using System.Windows.Input;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
/// <see cref="KeyId"/> 与 WPF <see cref="Key"/> / <see cref="MouseButton"/>、WinForms <see cref="Keys"/> 之间的转换。
/// 从 <see cref="KeyIdConverter"/> 拆出（键位配置移入 Core 后，WPF / WinForms 相关部分留在 Windows 主程序），实现未改
/// </summary>
public static class KeyIdWpfConverter
{
    /// <summary>
    /// 将KeyId转换为System.Windows.Input.Key
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static Key ToInputKey(this KeyId value)
    {
        // 部分按键名称相同，使用名称转换
        try
        {
            return Enum.Parse<Key>(value.ToString());
        }
        catch
        {
            // 其他键使用查表法转换
            return value switch
            {
                KeyId.LeftWin => Key.LWin,
                KeyId.RightWin => Key.RWin,
                KeyId.Apostrophe => Key.Oem7,
                KeyId.Comma => Key.OemComma,
                KeyId.Minus => Key.OemMinus,
                KeyId.Equal => Key.OemPlus,
                KeyId.Period => Key.OemPeriod,
                KeyId.Slash => Key.Oem2,
                KeyId.Semicolon => Key.Oem1,
                KeyId.LeftSquareBracket => Key.Oem4,
                KeyId.Backslash => Key.Oem5,
                KeyId.RightSquareBracket => Key.Oem6,
                KeyId.Tilde => Key.Oem3,
                KeyId.Enter => Key.Enter,
                KeyId.ScrollLock => Key.Scroll,
                KeyId.PageUp => Key.Prior,
                KeyId.PageDown => Key.Next,
                KeyId.Backspace => Key.Back,
                KeyId.CapsLock => Key.Capital,
                // None、Unknown和鼠标按键抛异常
                _ => throw new ArgumentOutOfRangeException(nameof(value)),
            };
        }
    }

    /// <summary>
    /// 将KeyId转换为MouseButton
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static MouseButton ToMouseButton(this KeyId value)
    {
        return value switch
        {
            KeyId.MouseLeftButton => MouseButton.Left,
            KeyId.MouseRightButton => MouseButton.Right,
            KeyId.MouseMiddleButton => MouseButton.Middle,
            KeyId.MouseSideButton1 => MouseButton.XButton1,
            KeyId.MouseSideButton2 => MouseButton.XButton2,
            _ => throw new ArgumentOutOfRangeException(nameof(value), "键盘按键请使用ToInputKey方法"),
        };
    }

    /// <summary>
    /// [实验] 将KeyId转换为WinForm中的Keys（用于兼容按键连发功能）
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static Keys ToWinFormKeys(this KeyId value)
    {
        try
        {
            return Enum.Parse<Keys>(value.ToInputKey().ToString());
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// 将System.Windows.Input.Key转换为KeyId
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static KeyId FromInputKey(Key value)
    {
        // 部分按键名称相同，使用名称转换
        try
        {
            return Enum.Parse<KeyId>(value.ToString());
        }
        catch
        {
            // 其他键使用查表法转换
            return value switch
            {
                Key.LWin => KeyId.LeftWin,
                Key.RWin => KeyId.RightWin,
                Key.Oem7 => KeyId.Apostrophe,
                Key.OemComma => KeyId.Comma,
                Key.OemMinus => KeyId.Minus,
                Key.OemPlus => KeyId.Equal,
                Key.OemPeriod => KeyId.Period,
                Key.Oem2 => KeyId.Slash,
                Key.Oem1 => KeyId.Semicolon,
                Key.Oem4 => KeyId.LeftSquareBracket,
                Key.Oem5 => KeyId.Backslash,
                Key.Oem6 => KeyId.RightSquareBracket,
                Key.Oem3 => KeyId.Tilde,
                Key.Enter => KeyId.Enter,
                Key.Scroll => KeyId.ScrollLock,
                Key.Prior => KeyId.PageUp,
                Key.Next => KeyId.PageDown,
                Key.Back => KeyId.Backspace,
                Key.Capital => KeyId.CapsLock,
                // 支持列表外的值返回Unknown
                _ => KeyId.Unknown,
            };
        }
    }

    /// <summary>
    /// 将MouseButton转换为KeyId
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static KeyId FromMouseButton(MouseButton value)
    {
        return value switch
        {
            MouseButton.Left => KeyId.MouseLeftButton,
            MouseButton.Right => KeyId.MouseRightButton,
            MouseButton.Middle => KeyId.MouseMiddleButton,
            MouseButton.XButton1 => KeyId.MouseSideButton1,
            MouseButton.XButton2 => KeyId.MouseSideButton2,
            _ => KeyId.Unknown,
        };
    }
}
