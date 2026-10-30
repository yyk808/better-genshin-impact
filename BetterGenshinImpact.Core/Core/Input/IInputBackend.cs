using System;

namespace BetterGenshinImpact.Core.Input;

/// <summary>
/// 一组可以整体切换的输入输出：Win32 (SendInput + PostMessage)、WebSdk、Gamepad、X11 (XTest)
/// </summary>
public interface IInputBackend : IDisposable
{
    InputBackendKind Kind { get; }

    /// <summary>
    /// 前台通道
    /// </summary>
    IInputChannel Foreground { get; }

    /// <summary>
    /// 后台通道。WebSdk / Gamepad 没有前后台之分，与 <see cref="Foreground"/> 是同一实例。
    /// 没有后台通道的平台（Linux 原生 X11：XSendEvent 会被游戏和 Wine 忽略）为 null，
    /// 主程序的 InputHub.Background 此时返回一个忽略操作并告警的通道
    /// </summary>
    IInputChannel? Background { get; }

    /// <summary>
    /// 释放所有通道中处于按下状态的按键
    /// </summary>
    void ReleaseAll();
}
