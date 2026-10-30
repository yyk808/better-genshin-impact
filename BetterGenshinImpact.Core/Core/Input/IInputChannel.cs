using BetterGenshinImpact.Platform;

namespace BetterGenshinImpact.Core.Input;

/// <summary>
/// 输入通道：后端内部的一路输出（前台或后台）。
/// <para>
/// 平台无关：按键使用 <see cref="GameKey"/>（数值与 Win32 VK 相同）。
/// 按用户键位配置模拟游戏动作的 <c>SimulateAction(GIActions)</c> 是业务层功能，
/// 由主程序中的扩展方法 <c>InputChannelActionExtensions.SimulateAction</c> 提供，调用写法不变。
/// </para>
/// </summary>
public interface IInputChannel
{
    IKeyboardInput Keyboard { get; }

    IMouseInput Mouse { get; }

    /// <summary>
    /// 通道能力。默认为 <see cref="InputCapabilities.Default"/>
    /// </summary>
    InputCapabilities Capabilities => InputCapabilities.Default;

    /// <summary>
    /// 按键是否处于按下状态，含鼠标键 VK_LBUTTON 等
    /// </summary>
    bool IsKeyDown(GameKey key);

    IInputChannel Sleep(int ms);

    /// <summary>
    /// 释放本通道中处于按下状态的按键
    /// </summary>
    void ReleaseAll();
}
