using BetterGenshinImpact.Platform;

namespace BetterGenshinImpact.Core.Input;

/// <summary>
/// 键盘输入。传入鼠标键（VK_LBUTTON、VK_RBUTTON、VK_MBUTTON、VK_XBUTTON1、VK_XBUTTON2）时按对应鼠标键处理
/// </summary>
public interface IKeyboardInput
{
    IKeyboardInput KeyDown(GameKey key);

    IKeyboardInput KeyUp(GameKey key);

    IKeyboardInput KeyPress(GameKey key);

    IKeyboardInput Sleep(int ms);
}
