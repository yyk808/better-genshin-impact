using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Platform.Linux.X11.Native;
using static BetterGenshinImpact.Platform.Linux.X11.Native.Xlib;

namespace BetterGenshinImpact.Platform.Linux.X11;

/// <summary>
/// XTest 输入后端：只有前台通道，<see cref="Background"/> 为 null（Linux 原生没有 PostMessage 的等价物，
/// XSendEvent 合成的事件带 send_event 标记，会被游戏和 Wine 忽略）。主程序的 InputHub.Background 此时返回告警并忽略的通道。
/// </summary>
public sealed class XTestInputBackend : IInputBackend
{
    private readonly X11Connection _c;
    private readonly bool _owns;

    /// <param name="c">X 连接，为 null 时按 $DISPLAY 自行创建</param>
    /// <param name="ownsConnection">Dispose 时是否关闭连接；<paramref name="c"/> 为 null 时总是 true</param>
    /// <param name="absoluteMoveAffectsCamera">绝对移动是否会被游戏当成视角转动。目标是 Wine 窗口（原神）时为 true</param>
    public XTestInputBackend(X11Connection? c = null, bool ownsConnection = false, bool absoluteMoveAffectsCamera = true)
    {
        _owns = c == null || ownsConnection;
        _c = c ?? new X11Connection();
        if (!XTestQueryExtension(_c.Display, out _, out _, out _, out _))
        {
            if (_owns) _c.Dispose();
            throw new InvalidOperationException("X 服务器不支持 XTEST 扩展");
        }

        Foreground = new XTestInputChannel(_c, absoluteMoveAffectsCamera);
    }

    public InputBackendKind Kind => InputBackendKind.X11;

    public IInputChannel Foreground { get; }

    public IInputChannel? Background => null;

    public void ReleaseAll() => Foreground.ReleaseAll();

    public void Dispose()
    {
        if (_owns) _c.Dispose();
    }
}

/// <summary>
/// XTest 前台通道。基于 <see cref="InputChannelBase"/>：按下记录、鼠标键 VK 转发、不支持操作限频告警都与 Win32 通道共用。
/// <para>
/// XTest 事件等同物理输入，发往当前焦点窗口。平台验证工程实测：XTest 相对移动经 Wine 变成数值精确的 WM_INPUT；
/// XTest 按键在 Wine 里同时产生 WM_KEYDOWN、原始键盘输入并更新 GetAsyncKeyState。
/// </para>
/// </summary>
public sealed unsafe class XTestInputChannel : InputChannelBase
{
    /// <summary>KeyPress / Click 的按下时长。与平台验证工程一致，避免按帧轮询键状态的游戏漏掉按键</summary>
    public const int PressHoldMs = 30;

    private readonly X11Connection _c;

    public XTestInputChannel(X11Connection c, bool absoluteMoveAffectsCamera = true)
    {
        _c = c;
        Capabilities = new InputCapabilities(
            RequiresForeground: true,
            SupportsRelativeMotion: true,
            SupportsKeyStateQuery: true,
            AbsoluteMoveAffectsCamera: absoluteMoveAffectsCamera);
    }

    public override InputCapabilities Capabilities { get; }

    private void Key(GameKey k, bool down)
    {
        lock (_c.Gate)
        {
            // 解绑时触发器线程可能还在发键：连接已关闭则忽略
            if (_c.IsDisposed) return;
            var code = KeyMap.ToKeyCode(_c.Display, k);
            if (code == 0)
            {
                WarnUnsupported($"键 {k} 在当前键盘映射中没有 keycode");
                return;
            }

            XTestInjectionLedger.Note(false, code, down);
            XTestFakeKeyEvent(_c.Display, (uint)code, down, 0);
            XFlush(_c.Display);
        }
    }

    protected override void OnKeyDown(GameKey key) => Key(key, true);

    protected override void OnKeyUp(GameKey key) => Key(key, false);

    /// <summary>按下与抬起在同一把锁内完成：解绑时关闭连接会等这次按键结束，不会留下一直按着的键</summary>
    protected override void OnKeyPress(GameKey key)
    {
        lock (_c.Gate)
        {
            Key(key, true);
            Thread.Sleep(PressHoldMs);
            Key(key, false);
        }
    }

    private static uint XButton(InputMouseButton b) => b switch
    {
        InputMouseButton.Left => 1,
        InputMouseButton.Middle => 2,
        InputMouseButton.Right => 3,
        InputMouseButton.X1 => 8,
        _ => 9,
    };

    private void Button(uint btn, bool down)
    {
        lock (_c.Gate)
        {
            if (_c.IsDisposed) return;
            XTestInjectionLedger.Note(true, (int)btn, down);
            XTestFakeButtonEvent(_c.Display, btn, down, 0);
            XFlush(_c.Display);
        }
    }

    protected override void OnMouseButton(InputMouseButton button, bool down) => Button(XButton(button), down);

    protected override void OnMouseClick(InputMouseButton button)
    {
        lock (_c.Gate)
        {
            Button(XButton(button), true);
            Thread.Sleep(PressHoldMs);
            Button(XButton(button), false);
        }
    }

    protected override void OnMoveBy(int dx, int dy)
    {
        lock (_c.Gate)
        {
            if (_c.IsDisposed) return;
            XTestFakeRelativeMotionEvent(_c.Display, dx, dy, 0);
            XFlush(_c.Display);
        }
    }

    /// <summary>兼容 Win32 语义：0~65535 归一化坐标按默认屏幕尺寸换算成像素</summary>
    protected override void OnMoveTo(double absX, double absY)
    {
        int w, h;
        lock (_c.Gate)
        {
            if (_c.IsDisposed) return;
            w = XDisplayWidth(_c.Display, _c.Screen);
            h = XDisplayHeight(_c.Display, _c.Screen);
        }

        OnMoveToPixel((int)Math.Round(absX * w / 65535d), (int)Math.Round(absY * h / 65535d));
    }

    protected override void OnMoveToPixel(int screenX, int screenY)
    {
        lock (_c.Gate)
        {
            if (_c.IsDisposed) return;
            XTestFakeMotionEvent(_c.Display, -1, screenX, screenY, 0);
            XFlush(_c.Display);
        }
    }

    /// <summary>正数向上。X11 用按钮 4（上）/ 5（下）表示一格滚动</summary>
    protected override void OnScroll(int clicks)
    {
        uint btn = clicks > 0 ? 4u : 5u;
        for (int i = 0; i < Math.Abs(clicks); i++)
        {
            Button(btn, true);
            Button(btn, false);
            Thread.Sleep(10);
        }
    }

    /// <summary>查询 X 服务器的实际键状态（XQueryKeymap / XQueryPointer），相当于 GetAsyncKeyState</summary>
    public override bool IsKeyDown(GameKey key)
    {
        lock (_c.Gate)
        {
            if (_c.IsDisposed) return false;
            if (key is GameKey.VK_LBUTTON or GameKey.VK_RBUTTON or GameKey.VK_MBUTTON)
            {
                XQueryPointer(_c.Display, _c.Root, out _, out _, out _, out _, out _, out _, out var mask);
                uint bit = key switch { GameKey.VK_LBUTTON => 1u << 8, GameKey.VK_MBUTTON => 1u << 9, _ => 1u << 10 };
                return (mask & bit) != 0;
            }

            var code = KeyMap.ToKeyCode(_c.Display, key);
            if (code == 0) return false;
            var keys = stackalloc byte[32];
            XQueryKeymap(_c.Display, keys);
            return (keys[code >> 3] & (1 << (code & 7))) != 0;
        }
    }
}
