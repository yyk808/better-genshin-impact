using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.Core.Recognition;
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

        // 配置里的颜色是 WPF 颜色文本（颜色名或 #AARRGGBB），用 Avalonia 的解析（语义相同）
        MaskColorParser.ColorTextConverter ??= text =>
        {
            var c = Color.Parse(text);
            return MaskColor.FromArgb(c.A, c.R, c.G, c.B);
        };

        // 绘制板可在任意线程变化，合并到 UI 线程上刷新
        host.MaskDrawing.Changed += OnDrawingChanged;
    }

    private int _drawingQueued;
    private bool _drawingLogged;

    private void OnDrawingChanged()
    {
        if (Interlocked.Exchange(ref _drawingQueued, 1) == 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Volatile.Write(ref _drawingQueued, 0);
                RefreshDrawing();
            }, DispatcherPriority.Render);
        }
    }

    /// <summary>最近一次绘制的图形数（用于验证）</summary>
    public int DrawnShapes => _window?.DrawingLayer.LastShapeCount ?? 0;

    /// <summary>当前绘制快照中的分组名（用于验证）</summary>
    public IReadOnlyList<string> DrawingGroups => _host.MaskDrawing.Current.Entries.Select(e => e.Group.Name).ToList();

    private void RefreshDrawing()
    {
        if (_window is not { IsVisible: true } window || _lastRect.Width <= 0)
        {
            return;
        }

        var config = _host.Config.MaskWindowConfig;
        window.DrawingLayer.Update(_host.MaskDrawing.Current, CreateStyle(config, _lastRect.Width / 1920d),
            config.DisplayRecognitionResultsOnMask, _lastRect.Size);
    }

    /// <summary>由 MaskWindowConfig 的 Recognition* 配置合成绘制样式（同 WPF MaskWindowViewModel.CreateDrawingStyle）</summary>
    private static MaskWindowDrawingStyle CreateStyle(Core.Config.MaskWindowConfig config, double scaleTo1080P) =>
        new(config.RecognitionUseDrawableStyle,
            new MaskWindowDrawingStroke(ParseColor(config.RecognitionRectStrokeColor, MaskColor.Red), Math.Max(0, config.RecognitionRectStrokeThickness)),
            new MaskWindowDrawingStroke(ParseColor(config.RecognitionLineStrokeColor, MaskColor.Red), Math.Max(0, config.RecognitionLineStrokeThickness)),
            ParseColor(config.RecognitionTextColor, MaskColor.Black),
            Math.Max(1, config.RecognitionTextFontSize) * scaleTo1080P);

    private static MaskColor ParseColor(string? text, MaskColor fallback)
    {
        try
        {
            return string.IsNullOrWhiteSpace(text) ? fallback : MaskColorParser.ConvertFromString(text);
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    /// <summary>遮罩窗口的 X11 窗口号（未显示时为 0），用于验证</summary>
    public nint Handle { get; private set; }

    public bool IsShown => _window?.IsVisible == true;

    private bool Wanted => _host.LinuxConfig.MaskWindowEnabled && _host.Config.MaskWindowConfig.MaskEnabled && _host.RuntimeService.Current is not null;

    private void Update()
    {
        try
        {
            // 调试：遮罩上显示全部识别结果
            RecognitionObject.ForceDrawOnWindow = Wanted && _host.LinuxConfig.MaskDrawAllRecognition;
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
            _window.SetLog(_host.Config.MaskWindowConfig.ShowLogBox ? RecentLog() : null, _window.Height);
            RefreshDrawing();
            if (!_drawingLogged && DrawnShapes > 0)
            {
                _drawingLogged = true;
                _log.Information("遮罩绘制：{Count} 个图形，分组 {Groups}", DrawnShapes, string.Join("、", DrawingGroups));
            }

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

    /// <summary>日志框内容：最近 6 行信息及以上级别的日志（时间 + 消息）</summary>
    private static string RecentLog()
    {
        var lines = UiLogSink.Instance.Lines;
        var start = Math.Max(0, lines.Count - 6);
        var text = new System.Text.StringBuilder();
        for (var i = start; i < lines.Count; i++)
        {
            var line = lines[i];
            text.Append(line.Time.ToString("HH:mm:ss")).Append(' ').Append(line.Message.Replace('\n', ' '));
            if (i < lines.Count - 1)
            {
                text.Append('\n');
            }
        }

        return text.ToString();
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
        _host.MaskDrawing.Changed -= OnDrawingChanged;
        RecognitionObject.ForceDrawOnWindow = false;
        _timer.Stop();
        _window?.Close();
        _window = null;
    }
}
