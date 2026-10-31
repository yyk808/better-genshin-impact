using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Input.Backends.WebSdk;
using BetterGenshinImpact.Platform;

namespace BetterGenshinImpact.Core.UnitTest.Input;

/// <summary>
/// 云原神网页版后端：键鼠操作翻译成页面 SDK 调用；绝对坐标按传入的屏幕尺寸换算后相对游戏画面归一化
/// </summary>
public class WebSdkInputBackendTests
{
    private sealed class RecordingBridge : IWebInputBridge
    {
        public List<string> Calls { get; } = [];

        public void Invoke(string method, params object[] args) =>
            Calls.Add(args.Length == 0 ? method : $"{method}({string.Join(",", args.Select(a => a is double d ? d.ToString("0.###") : a.ToString()))})");
    }

    private static (WebSdkInputBackend Backend, RecordingBridge Bridge) Create()
    {
        var bridge = new RecordingBridge();
        // 游戏画面在 2560x1440 屏幕的 (320,180) 处，大小 1920x1080
        var backend = new WebSdkInputBackend(bridge, () => new PixelRect(320, 180, 1920, 1080), () => new Size(2560, 1440));
        return (backend, bridge);
    }

    [Fact]
    public void MoveTo_NormalizesToCanvas()
    {
        var (backend, bridge) = Create();

        // 屏幕像素 (1280,720) = 画面中心
        backend.Foreground.Mouse.MoveMouseTo(1280 * 65535d / 2560, 720 * 65535d / 1440);

        Assert.Equal(["setAbsPosition(0.5,0.5)", "mouseMove(0,0)"], bridge.Calls);
    }

    [Fact]
    public void MoveTo_OutsideCanvas_IsClamped()
    {
        var (backend, bridge) = Create();

        backend.Foreground.Mouse.MoveMouseTo(0, 65535);

        Assert.Equal("setAbsPosition(0,1)", bridge.Calls[0]);
    }

    [Fact]
    public void KeysButtonsAndScroll_MapToSdkMethods()
    {
        var (backend, bridge) = Create();

        backend.Foreground.Keyboard.KeyDown(GameKey.VK_W);
        backend.Foreground.Keyboard.KeyUp(GameKey.VK_W);
        backend.Foreground.Keyboard.KeyPress(GameKey.VK_ESCAPE);
        backend.Foreground.Mouse.LeftButtonClick();
        backend.Foreground.Mouse.MoveMouseBy(10, -5);
        backend.Foreground.Mouse.VerticalScroll(-1);

        Assert.Equal(["keyDown(KeyW)", "keyUp(KeyW)", "tapKey(Escape)", "click(left)", "mouseMove(10,-5)", "scroll(-120)"], bridge.Calls);
        Assert.Same(backend, backend.Background);
    }
}
