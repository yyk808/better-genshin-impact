namespace BetterGenshinImpact.Core.Input;

/// <summary>
/// 鼠标输入。方法名与 Fischless.WindowsInput.IMouseSimulator 保持一致
/// </summary>
public interface IMouseInput
{
    /// <summary>
    /// 相对移动（视角控制）
    /// </summary>
    IMouseInput MoveMouseBy(int dx, int dy);

    /// <summary>
    /// 绝对定位。与 Fischless 相同：桌面绝对坐标，按主屏幕归一化到 0~65535。
    /// 是否是 1080P 坐标由调用方（Region 体系）换算，输入层不感知游戏分辨率
    /// </summary>
    IMouseInput MoveMouseTo(double absX, double absY);

    /// <summary>
    /// 绝对定位，参数为虚拟桌面上的物理像素坐标（跨平台推荐写法）。
    /// Win32 后端换算成主屏幕 0~65535 归一化坐标后与 <see cref="MoveMouseTo"/> 走同一条 SendInput 路径。
    /// 注意 Wine 下绝对移动会被游戏当成视角转动，见 <see cref="BetterGenshinImpact.Platform.InputCapabilities.AbsoluteMoveAffectsCamera"/>
    /// </summary>
    IMouseInput MoveMouseToPixel(int screenX, int screenY);

    IMouseInput LeftButtonDown();

    IMouseInput LeftButtonUp();

    IMouseInput LeftButtonClick();

    IMouseInput RightButtonDown();

    IMouseInput RightButtonUp();

    IMouseInput RightButtonClick();

    IMouseInput MiddleButtonDown();

    IMouseInput MiddleButtonUp();

    IMouseInput MiddleButtonClick();

    /// <param name="buttonId">1 = 侧键1（XBUTTON1），2 = 侧键2（XBUTTON2）</param>
    IMouseInput XButtonDown(int buttonId);

    /// <param name="buttonId">1 = 侧键1（XBUTTON1），2 = 侧键2（XBUTTON2）</param>
    IMouseInput XButtonUp(int buttonId);

    /// <param name="buttonId">1 = 侧键1（XBUTTON1），2 = 侧键2（XBUTTON2）</param>
    IMouseInput XButtonClick(int buttonId);

    IMouseInput VerticalScroll(int scrollAmountInClicks);

    IMouseInput Sleep(int ms);
}
