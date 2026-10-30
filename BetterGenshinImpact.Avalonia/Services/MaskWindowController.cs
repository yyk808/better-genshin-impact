using Avalonia;
using Avalonia.Threading;
using BetterGenshinImpact.Avalonia.Views;
using BetterGenshinImpact.Host.Common;
using BetterGenshinImpact.Platform.Linux.X11;
using Serilog;

namespace BetterGenshinImpact.Avalonia.Services;

/// <summary>
/// 遮罩窗口的显示与跟随：截图器运行且启用遮罩时，把遮罩窗口盖在游戏画面上（每 500 ms 跟随位置和大小），
/// 停止后隐藏。点击穿透：窗口打开后把 X11 SHAPE 输入区域设为空。
/// </summary>
public sealed class MaskWindowController : IDisposable
{
    private readonly GameHost _host;
    private readonly string? _display;
    private readonly ILogger _log;
    private readonly DispatcherTimer _timer;
    private MaskWindow? _window;
    private PixelRect _lastRect;

    public MaskWindowController(GameHost host, string? display, ILogger log)
    {
        _host = host;
        _display = display;
        _log = log;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => Update());
        _timer.Start();
    }

    /// <summary>遮罩窗口的 X11 窗口号（未显示时为 0），用于验证</summary>
    public nint Handle { get; private set; }

    public bool IsShown => _window?.IsVisible == true;

    private bool Wanted => _host.LinuxConfig.MaskWindowEnabled && _host.Config.MaskWindowConfig.MaskEnabled && _host.RuntimeService.Current is not null;

    private void Update()
    {
        try
        {
            if (!Wanted)
            {
                Hide();
                return;
            }

            var window = _host.RuntimeService.Current!.Window;
            var r = window.Viewport.ScreenRect;
            var rect = new PixelRect(r.X, r.Y, r.Width, r.Height);
            if (rect.Width <= 0 || rect.Height <= 0 || window.IsMinimized)
            {
                Hide();
                return;
            }

            if (_window is null)
            {
                _window = new MaskWindow();
                _window.Opened += (_, _) => OnOpened();
            }

            if (rect != _lastRect || !_window.IsVisible)
            {
                var scale = _window.RenderScaling > 0 ? _window.RenderScaling : 1;
                _window.Position = rect.Position;
                _window.Width = rect.Width / scale;
                _window.Height = rect.Height / scale;
                _lastRect = rect;
            }

            _window.SetStatus($"BetterGI · 已处理 {_host.FramesProcessed} 帧", _window.Height);
            if (!_window.IsVisible)
            {
                _window.Show();
                _log.Information("遮罩窗口已显示：画面 {Rect}", rect);
            }
        }
        catch (Exception e)
        {
            _log.Debug(e, "更新遮罩窗口失败");
        }
    }

    private void OnOpened()
    {
        if (_window?.TryGetPlatformHandle() is not { } handle)
        {
            return;
        }

        Handle = handle.Handle;
        if (!X11Overlay.HasCompositor(_display))
        {
            _log.Warning("没有检测到合成器（_NET_WM_CM_S0），遮罩的透明部分会显示为黑色并挡住游戏画面（Composite 窗口截图不受影响，屏幕区域截图会截到黑色）；请启用桌面合成（如 picom）或关闭遮罩");
        }

        var error = X11Overlay.SetClickThrough(_display, Handle);
        if (error is null)
        {
            _log.Information("遮罩窗口 0x{Handle:x}：已设置点击穿透（SHAPE 输入区域为空）", Handle);
        }
        else
        {
            _log.Warning("遮罩窗口无法设置点击穿透：{Error}", error);
        }
    }

    private void Hide()
    {
        if (_window?.IsVisible == true)
        {
            _window.Hide();
            _log.Information("遮罩窗口已隐藏");
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _window?.Close();
        _window = null;
    }
}
