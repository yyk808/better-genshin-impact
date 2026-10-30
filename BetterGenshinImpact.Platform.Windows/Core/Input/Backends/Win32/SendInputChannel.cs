using BetterGenshinImpact.Core.Simulator;
using Fischless.WindowsInput;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Platform.Windows;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Input.Backends.Win32;

/// <summary>
/// 前台通道：包装 Win32Input.SendInput（Fischless.WindowsInput，与主程序 Simulation.SendInput 为同一实例）
/// </summary>
internal sealed class SendInputChannel : InputChannelBase
{
    /// <summary>
    /// SendInput 只发往前台窗口；IsKeyDown 查询系统键状态。
    /// Wine 下 SendInput 的绝对移动会变成 WM_INPUT 相对增量，游戏会当成视角转动
    /// </summary>
    public override InputCapabilities Capabilities { get; } = new(
        RequiresForeground: true,
        SupportsRelativeMotion: true,
        SupportsKeyStateQuery: true,
        AbsoluteMoveAffectsCamera: Win32Environment.IsRunningOnWine);

    private static InputSimulator Input => Win32Input.SendInput;

    // 扩展标志按真实键盘判断（见 ExtendedKeys）：左 Alt 等不带，避免被识别成右 Alt；方向键等带，避免被识别成小键盘按键。
    // 不使用 Fischless 的自动判断，它把左 Alt 与泛指的 Alt / Ctrl 也算作扩展键
    protected override void OnKeyDown(GameKey key) => Input.Keyboard.KeyDown(ExtendedKeys.IsExtended(key), (User32.VK)key);

    protected override void OnKeyUp(GameKey key) => Input.Keyboard.KeyUp(ExtendedKeys.IsExtended(key), (User32.VK)key);

    protected override void OnKeyPress(GameKey key) => Input.Keyboard.KeyPress(ExtendedKeys.IsExtended(key), (User32.VK)key);

    protected override void OnMouseButton(InputMouseButton button, bool down)
    {
        var mouse = Input.Mouse;
        switch (button)
        {
            case InputMouseButton.Left:
                _ = down ? mouse.LeftButtonDown() : mouse.LeftButtonUp();
                break;
            case InputMouseButton.Right:
                _ = down ? mouse.RightButtonDown() : mouse.RightButtonUp();
                break;
            case InputMouseButton.Middle:
                _ = down ? mouse.MiddleButtonDown() : mouse.MiddleButtonUp();
                break;
            case InputMouseButton.X1:
                _ = down ? mouse.XButtonDown(0x0001) : mouse.XButtonUp(0x0001);
                break;
            case InputMouseButton.X2:
                _ = down ? mouse.XButtonDown(0x0002) : mouse.XButtonUp(0x0002);
                break;
        }
    }

    protected override void OnMouseClick(InputMouseButton button)
    {
        var mouse = Input.Mouse;
        switch (button)
        {
            case InputMouseButton.Left:
                mouse.LeftButtonClick();
                break;
            case InputMouseButton.Right:
                mouse.RightButtonClick();
                break;
            case InputMouseButton.Middle:
                mouse.MiddleButtonClick();
                break;
            case InputMouseButton.X1:
                mouse.XButtonClick(0x0001);
                break;
            case InputMouseButton.X2:
                mouse.XButtonClick(0x0002);
                break;
        }
    }

    protected override void OnMoveBy(int dx, int dy) => Input.Mouse.MoveMouseBy(dx, dy);

    protected override void OnMoveTo(double absX, double absY) => Input.Mouse.MoveMouseTo(absX, absY);

    /// <summary>
    /// 像素坐标换算成主屏幕 0~65535 归一化坐标，与业务代码原有的 x * 65535 / PrimaryScreen.WorkingArea.Width 换算一致
    /// </summary>
    protected override void OnMoveToPixel(int screenX, int screenY)
    {
        var screen = PrimaryScreen.WorkingArea;
        Input.Mouse.MoveMouseTo(screenX * 65535d / screen.Width, screenY * 65535d / screen.Height);
    }

    protected override void OnScroll(int clicks) => Input.Mouse.VerticalScroll(clicks);

    /// <summary>
    /// 与原 Simulation.IsKeyDown 一致，查询系统的实际按键状态
    /// </summary>
    public override bool IsKeyDown(GameKey key) => Win32Input.IsKeyDown(key);

    /// <summary>
    /// 按记录抬起，再用 Simulation.ReleaseAllKey 兜底（与原 Simulation.ReleaseAllKey 行为一致）
    /// </summary>
    public override void ReleaseAll()
    {
        base.ReleaseAll();
        Win32Input.ReleaseAllKeys(Logger);
    }
}
