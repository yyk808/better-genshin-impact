using System;
using System.Windows;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Mask;

/// <summary>
/// 遮罩窗口宿主：唯一负责遮罩窗口的创建、显隐、置顶和跟随游戏窗口。
/// View 层以外的代码只能通过它接触遮罩窗口。
/// </summary>
public interface IMaskWindowHost : IGameWindowStateSink
{
    /// <summary>
    /// UI 线程调用。首次调用时创建窗口，绑定游戏窗口并显示
    /// </summary>
    void Attach(nint gameHandle);

    /// <summary>
    /// UI 线程调用。隐藏窗口并解除绑定，窗口实例保留复用
    /// </summary>
    void Detach();

    /// <summary>
    /// UI 线程调用。关闭并释放窗口（程序退出时）
    /// </summary>
    void Close();

    /// <summary>
    /// 最近一次应用到窗口上的状态，任意线程可读
    /// </summary>
    MaskWindowState State { get; }

    /// <summary>
    /// 可见性或位置变化后在 UI 线程触发，附属遮罩（HtmlMask 等）订阅
    /// </summary>
    event EventHandler<MaskWindowState>? StateChanged;
}

/// <summary>
/// 遮罩窗口的状态
/// </summary>
/// <param name="IsVisible">是否可见</param>
/// <param name="Bounds">窗口位置（DIP）</param>
public readonly record struct MaskWindowState(bool IsVisible, Rect Bounds);
