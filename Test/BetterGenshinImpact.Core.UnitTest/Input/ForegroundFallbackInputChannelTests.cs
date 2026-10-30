using System.Collections.Generic;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Platform;

namespace BetterGenshinImpact.Core.UnitTest.Input;

/// <summary>
/// 没有后台通道时的后台输入：游戏在前台改走前台通道，不在前台跳过；抬起只补发给本通道按下过的键
/// </summary>
public class ForegroundFallbackInputChannelTests
{
    private sealed class RecordingChannel : InputChannelBase
    {
        public List<string> Events { get; } = [];
        protected override void OnKeyDown(GameKey key) => Events.Add($"down {key}");
        protected override void OnKeyUp(GameKey key) => Events.Add($"up {key}");
        protected override void OnMouseButton(InputMouseButton button, bool down) => Events.Add($"{(down ? "down" : "up")} {button}");
        protected override void OnMouseClick(InputMouseButton button) => Events.Add($"click {button}");
        protected override void OnMoveBy(int dx, int dy) => Events.Add($"move {dx},{dy}");
        protected override void OnMoveTo(double absX, double absY) => Events.Add("moveTo");
        protected override void OnScroll(int clicks) => Events.Add($"scroll {clicks}");
    }

    [Fact]
    public void Foreground_ForwardsToForegroundChannel()
    {
        var fg = new RecordingChannel();
        var channel = new ForegroundFallbackInputChannel(() => fg, () => true);

        channel.Keyboard.KeyPress(GameKey.VK_SPACE);
        channel.Mouse.LeftButtonClick();
        channel.Mouse.MoveMouseBy(3, 4);

        Assert.Equal(["down VK_SPACE", "up VK_SPACE", "click Left", "move 3,4"], fg.Events);
    }

    [Fact]
    public void NotForeground_Skips()
    {
        var fg = new RecordingChannel();
        var channel = new ForegroundFallbackInputChannel(() => fg, () => false);

        channel.Keyboard.KeyPress(GameKey.VK_SPACE);
        channel.Keyboard.KeyDown(GameKey.VK_W);
        channel.Mouse.LeftButtonClick();
        channel.Mouse.VerticalScroll(1);

        Assert.Empty(fg.Events);
    }

    [Fact]
    public void KeyDownWhileForeground_IsReleasedAfterLosingForeground()
    {
        var fg = new RecordingChannel();
        var foreground = true;
        var channel = new ForegroundFallbackInputChannel(() => fg, () => foreground);

        channel.Keyboard.KeyDown(GameKey.VK_W);
        channel.Mouse.RightButtonDown();
        foreground = false;
        channel.Keyboard.KeyUp(GameKey.VK_W);
        channel.Mouse.RightButtonUp();
        channel.Keyboard.KeyUp(GameKey.VK_S); // 没有按下过：不补发

        Assert.Equal(["down VK_W", "down Right", "up VK_W", "up Right"], fg.Events);
    }
}
