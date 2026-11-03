using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoSkip;
using BetterGenshinImpact.GameTask.Runtime;
using ColorConversionCodes = OpenCvSharp.ColorConversionCodes;
using Cv2 = OpenCvSharp.Cv2;
using Mat = OpenCvSharp.Mat;
using MatType = OpenCvSharp.MatType;
using Size = OpenCvSharp.Size;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>
/// 画中画窗口（WPF View/Windows/PictureInPictureWindow）：游戏失焦时显示游戏画面的置顶小窗，
/// 420×240、无边框、圆角 16 + 半透明白色描边、不在任务栏、显示时不抢焦点、淡入 120 ms；
/// 滚轮缩放（每格 ×1.08 / ×0.92，宽 220–1280，按画面宽高比定高），左键单击切回游戏，拖动移动窗口，右键关闭（本次失焦期间不再显示）；
/// 第一次拿到画面时放到游戏画面右上角（边距 16）。
/// 画面来源与 WPF 相同：“60帧模式”每个渲染帧自己截一次图，“截图器供图”由截图调度把每帧交给 <see cref="SetFrame"/>
/// </summary>
public sealed class PictureInPictureWindow : Window
{
    private const double MinPipWidth = 220;
    private const double MaxPipWidth = 1280;
    private const double MarginSize = 16;

    private readonly Image _image;
    private readonly Border _border;
    private WriteableBitmap? _bitmap;
    private Mat? _bgra;
    private double _aspectRatio = 16d / 9d;
    private bool _initializedPosition;
    private bool _pointerDown;
    private bool _dragging;
    private Point _downPoint;
    private Size _cacheSize;
    private bool _closed;
    private bool _loopRequested;
    private PointerPressedEventArgs? _press;

    /// <summary>niri IPC（不是 niri 时为 null）：XWayland 窗口的位置与显示时不抢焦点</summary>
    private readonly BetterGenshinImpact.Platform.Linux.Wayland.NiriIpc? _niri = BetterGenshinImpact.Platform.Linux.Wayland.NiriIpc.FromEnvironment();

    /// <summary>niri 工作区视图中的逻辑位置（第一次出画面时算出，隐藏时更新）</summary>
    private (double X, double Y)? _niriPos;

    /// <summary>niri：显示前的焦点窗口</summary>
    private ulong? _niriPreviousFocus;

    public event Action? ClosedByUser;

    /// <summary>当前运行环境，截图器未启动时为 null（每次读取，不缓存）</summary>
    private static GameRuntime? CurrentRuntime => CoreServices.GetService<GameRuntimeService>()?.Current;

    private static AllConfig Config => ConfigHost.Config!;

    public PictureInPictureWindow()
    {
        Title = "原神 - 画中画";
        if (_niri is not null) _niri.CacheDisabled = true;
        Width = 420;
        Height = 240;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        _image = new Image { Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapInterpolationMode(_image, BitmapInterpolationMode.HighQuality);
        _border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            // 同 WPF UpdateClip：画面按圆角裁剪
            ClipToBounds = true,
            Child = _image,
        };
        Content = _border;
        Opacity = 0;
        Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(120) }];
        Opened += (_, _) =>
        {
            Opacity = 1;
            RequestLoop();
        };
        PointerWheelChanged += OnWheel;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
    }

    /// <summary>当前显示的画面（用于验证）</summary>
    public WriteableBitmap? Frame => _bitmap;

    /// <summary>画面宽高比（用于验证）</summary>
    public double AspectRatio => _aspectRatio;

    /// <summary>已显示的画面帧数（用于验证）</summary>
    public int PresentedFrames { get; private set; }

    /// <summary>画中画是否已被用户关闭或窗口已关闭</summary>
    public bool IsClosed => _closed;

    /// <summary>同 WPF CompositionTarget.Rendering：窗口显示期间每个渲染帧回调一次，“60帧模式”在这里截图</summary>
    private void RequestLoop()
    {
        if (_closed || _loopRequested) return;
        _loopRequested = true;
        RequestAnimationFrame(_ =>
        {
            _loopRequested = false;
            if (_closed || !IsVisible) return;
            Loop();
            RequestLoop();
        });
    }

    /// <summary>重新显示时恢复渲染帧回调</summary>
    public void ShowPip()
    {
        if (!IsVisible)
        {
            ulong? previousFocus = null;
            if (_niri is not null)
            {
                try
                {
                    previousFocus = _niri.Windows().FirstOrDefault(w => w.IsFocused)?.Id;
                }
                catch (Exception e) when (IsNiriError(e))
                {
                }
            }

            _niriPreviousFocus = previousFocus;
            Show();
            if (_niri is not null)
            {
                var (title, pos) = (Title ?? "", _niriPos);
                _ = Task.Run(() => PlaceOnNiri(title, pos, previousFocus));
            }
        }

        RequestLoop();
    }

    private void Loop()
    {
        if (Config.AutoSkipConfig.PictureInPictureSourceType != nameof(PictureSourceType.CaptureLoop)) return;
        // 本帧内截图与定位使用同一个运行环境
        var runtime = CurrentRuntime;
        using var captureFrame = runtime?.Capture.Capture();
        if (captureFrame?.Frame is not { } mat) return;
        Present(mat, runtime);
    }

    /// <summary>“截图器供图”：接管 <paramref name="frame"/> 的所有权并释放（同 WPF SetFrame）</summary>
    public void SetFrame(Mat? frame)
    {
        if (frame == null) return;
        try
        {
            if (_closed || Config.AutoSkipConfig.PictureInPictureSourceType != nameof(PictureSourceType.TriggerDispatcher)) return;
            Present(frame, CurrentRuntime);
        }
        finally
        {
            frame.Dispose();
        }
    }

    private void Present(Mat frame, GameRuntime? runtime)
    {
        if (frame.Empty() || frame.Width <= 0 || frame.Height <= 0) return;
        PresentedFrames++;
        var size = new Size(frame.Width, frame.Height);
        if (_bitmap is null || _cacheSize != size)
        {
            _bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            _cacheSize = size;
            _aspectRatio = frame.Width * 1d / frame.Height;
            Copy(frame, _bitmap);
            _image.Source = _bitmap;
            // 截图器已停止时没有游戏区域可参考，留到下次有运行环境时再定位
            if (!_initializedPosition && runtime is not null)
            {
                PositionNearGame(runtime.SystemInfo.CaptureAreaRect);
                _initializedPosition = true;
            }
        }
        else
        {
            Copy(frame, _bitmap);
            // 同一个位图对象内容改变后要求重绘
            _image.InvalidateVisual();
        }
    }

    private void Copy(Mat frame, WriteableBitmap bitmap)
    {
        var src = frame;
        if (frame.Type() != MatType.CV_8UC4)
        {
            _bgra ??= new Mat();
            var code = frame.Channels() switch
            {
                1 => ColorConversionCodes.GRAY2BGRA,
                3 => ColorConversionCodes.BGR2BGRA,
                _ => (ColorConversionCodes?)null,
            };
            if (code is null) return;
            Cv2.CvtColor(frame, _bgra, code.Value);
            src = _bgra;
        }

        using var fb = bitmap.Lock();
        var rowBytes = src.Width * 4;
        unsafe
        {
            for (var y = 0; y < src.Height; y++)
            {
                Buffer.MemoryCopy((byte*)src.Ptr(y), (byte*)fb.Address + (long)y * fb.RowBytes, fb.RowBytes, rowBytes);
            }
        }
    }

    /// <summary>放到游戏画面的右上角（<paramref name="captureRect"/> 为物理像素，窗口位置也是物理像素）</summary>
    private void PositionNearGame(BetterGenshinImpact.Platform.PixelRect captureRect)
    {
        var scale = RenderScaling > 0 ? RenderScaling : 1;
        var left = captureRect.Left + captureRect.Width - (Width + MarginSize) * scale;
        var top = captureRect.Top + MarginSize * scale;
        Position = new PixelPoint((int)Math.Max(0, Math.Round(left)), (int)Math.Max(0, Math.Round(top)));
        if (_niri is not null && NiriLogical(captureRect.Left, captureRect.Top) is { } o)
        {
            // niri 下本窗口经 XWayland 显示，niri 不采用 X 客户端自己设的位置（浮动窗口放在中间）→ 经 IPC 移到同一位置
            _niriPos = (Math.Max(0, Math.Round(o.X + captureRect.Width / o.Scale - Width - MarginSize)), Math.Max(0, Math.Round(o.Y + MarginSize)));
            var (title, pos) = (Title ?? "", _niriPos);
            _ = Task.Run(() => PlaceOnNiri(title, pos, null));
        }
    }

    /// <summary>物理像素点所在 niri 输出：换成工作区视图中的逻辑坐标，并给出该输出的缩放</summary>
    private (double X, double Y, double Scale)? NiriLogical(int px, int py)
    {
        try
        {
            var outputs = _niri!.Outputs();
            var o = outputs.FirstOrDefault(o => px >= o.X * o.Scale && px < (o.X + o.Width) * o.Scale && py >= o.Y * o.Scale && py < (o.Y + o.Height) * o.Scale)
                    ?? outputs.FirstOrDefault();
            return o is null ? null : (px / o.Scale - o.X, py / o.Scale - o.Y, o.Scale);
        }
        catch (Exception e) when (IsNiriError(e))
        {
            return null;
        }
    }

    private static bool IsNiriError(Exception e) => e is IOException or System.Net.Sockets.SocketException or InvalidOperationException or System.Text.Json.JsonException;

    /// <summary>
    /// niri：找到本进程标题为 <paramref name="title"/> 的窗口（刚显示时 niri 可能还没有，最多重试 2 秒）；
    /// 有 <paramref name="pos"/> 时改为浮动并移到该位置；niri 把新窗口设为焦点时把焦点还给 <paramref name="previousFocus"/>（同 WPF 显示时不激活）
    /// </summary>
    private async Task PlaceOnNiri(string title, (double X, double Y)? pos, ulong? previousFocus)
    {
        try
        {
            for (var i = 0; i < 20 && !_closed; i++)
            {
                var windows = _niri!.Windows();
                var self = windows.FirstOrDefault(w => w.Title == title && w.Pid == Environment.ProcessId) ?? windows.FirstOrDefault(w => w.Title == title);
                if (self is not null)
                {
                    if (pos is { } p)
                    {
                        if (!self.IsFloating) _niri.MoveWindowToFloating(self.Id);
                        _niri.MoveFloatingWindow(self.Id, p.X, p.Y);
                    }

                    if (previousFocus is { } prev && prev != self.Id && self.IsFocused) _niri.FocusWindow(prev);
                    return;
                }

                await Task.Delay(100);
            }
        }
        catch (Exception e) when (IsNiriError(e))
        {
            // 只影响初始位置 / 焦点（窗口仍显示在 niri 默认位置）
        }
    }

    /// <summary>隐藏；niri 下先记下当前位置（重新映射的 X 窗口在 niri 里是新窗口，再显示时移回原处）</summary>
    public void HidePip()
    {
        if (_niri is not null && IsVisible)
        {
            try
            {
                if (_niri.Windows().FirstOrDefault(w => w.Title == Title && w.Pid == Environment.ProcessId)?.ScreenRect is { } r && NiriLogical(r.X, r.Y) is { } o)
                    _niriPos = (Math.Round(o.X), Math.Round(o.Y));
            }
            catch (Exception e) when (IsNiriError(e))
            {
            }
        }

        Hide();
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0) return;
        var ratio = e.Delta.Y > 0 ? 1.08 : 0.92;
        var next = Math.Clamp(Width * ratio, MinPipWidth, MaxPipWidth);
        Width = next;
        Height = next / _aspectRatio;
        e.Handled = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _pointerDown = true;
        _dragging = false;
        _downPoint = e.GetPosition(this);
        _press = e;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_pointerDown || _dragging) return;
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _downPoint.X) > 4 || Math.Abs(current.Y - _downPoint.Y) > 4)
        {
            _dragging = true;
            // 同 WPF DragMove：由窗口管理器 / 合成器接管移动（按下事件作为起点）
            if (_press is { } press) BeginMoveDrag(press);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Right)
        {
            ClosedByUser?.Invoke();
            Close();
            // niri：点击时小窗得到了焦点，关闭后 niri 会把焦点交给别的窗口（常常是游戏，相当于左键“切回游戏”）→ 还给显示前的焦点窗口
            if (_niriPreviousFocus is { } prev && BetterGenshinImpact.Platform.Linux.Wayland.NiriIpc.FromEnvironment() is { } niri)
            {
                _ = Task.Run(() =>
                {
                    using (niri)
                    {
                        try
                        {
                            niri.FocusWindow(prev);
                        }
                        catch (Exception ex) when (IsNiriError(ex))
                        {
                        }
                    }
                });
            }

            return;
        }

        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (!_dragging)
        {
            // 点击画中画切回游戏；截图器未启动时没有可激活的窗口。Linux 的激活会等待成为前台（最多 2 秒），放到线程池不阻塞界面
            if (CurrentRuntime?.Window is { } game) _ = Task.Run(game.Activate);
        }

        _pointerDown = false;
        _dragging = false;
        _press = null;
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _image.Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
        _bgra?.Dispose();
        _bgra = null;
        _niri?.Dispose();
        base.OnClosed(e);
    }
}
