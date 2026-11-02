using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using BetterGenshinImpact.Avalonia.Views;
using BetterGenshinImpact.Platform.Linux.Wayland;

namespace BetterGenshinImpact.Avalonia.Services;

/// <summary>
/// Wayland 会话下的遮罩：遮罩窗口的内容（识别框、日志框、状态文字，与 X11 遮罩是同一套控件）不放进窗口，
/// 而是在 UI 线程上离屏渲染（Skia → <see cref="RenderTargetBitmap"/>），整幅位图提交到 wlr-layer-shell 覆盖层
/// （overlay 层、空输入区域 = 点击穿透、不抢键盘），放在游戏窗口所在的输出上。
/// X 窗口在 niri 下无法置顶也无法点击穿透，所以不用 X 窗口做遮罩
/// </summary>
internal sealed class WaylandMaskSurface : IDisposable
{
    private readonly LayerShellOverlay _overlay;
    private readonly Control _content;
    private WaylandOverlayPlacement? _placement;
    private RenderTargetBitmap? _bitmap;
    private byte[] _pixels = [];

    private WaylandMaskSurface(LayerShellOverlay overlay, Control content)
    {
        _overlay = overlay;
        _content = content;
    }

    /// <summary>连接合成器并接管 <paramref name="window"/> 的内容（窗口本身不再显示）；不支持 layer-shell 时抛出</summary>
    public static WaylandMaskSurface Create(MaskWindow window)
    {
        var overlay = LayerShellOverlay.Connect();
        var content = (Control)(window.Content ?? throw new InvalidOperationException("遮罩窗口没有内容"));
        window.Content = null;
        return new WaylandMaskSurface(overlay, content);
    }

    public WaylandOverlayPlacement? Placement => _placement;

    /// <summary>覆盖层上的指针事件（只在输入区域内；在 <see cref="Pump"/> 里触发）</summary>
    public event Action<OverlayPointerEvent>? Pointer
    {
        add => _overlay.Pointer += value;
        remove => _overlay.Pointer -= value;
    }

    /// <summary>输入区域（逻辑坐标；空 = 完全点击穿透），用于布局编辑</summary>
    public void SetInputRegion(IReadOnlyList<System.Drawing.Rectangle> rects) =>
        _overlay.SetInputRegion(rects.Select(r => (r.X, r.Y, r.Width, r.Height)).ToList());

    /// <summary>读取已到达的合成器事件（指针事件在此触发）</summary>
    public void Pump() => _overlay.Pump();

    /// <summary>指针在覆盖层上时的光标形状（cursor-shape-v1 编号）</summary>
    public void SetCursorShape(uint shape) => _overlay.SetCursorShape(shape);

    public bool IsShown => _placement is not null;

    /// <summary>合成器关闭了覆盖层（如输出被拔掉）</summary>
    public bool Closed => _overlay.Closed;

    public IReadOnlyCollection<string> OutputNames => _overlay.OutputNames;

    /// <summary>移动 / 改变大小 / 换输出；位置没变时什么也不做</summary>
    public void Place(WaylandOverlayPlacement placement)
    {
        if (placement == _placement) return;
        _overlay.Show(placement.Output, placement.X, placement.Y, placement.Width, placement.Height, placement.BufferScale);
        _placement = placement;
    }

    /// <summary>按当前位置离屏渲染遮罩内容并提交</summary>
    public void Render()
    {
        if (_placement is not { } p) return;
        var size = new Size(p.Width, p.Height);
        _content.Measure(size);
        _content.Arrange(new Rect(size));
        var pixelSize = new PixelSize(p.Width * p.BufferScale, p.Height * p.BufferScale);
        if (_bitmap is null || _bitmap.PixelSize != pixelSize)
        {
            _bitmap?.Dispose();
            _bitmap = new RenderTargetBitmap(pixelSize, new Vector(96d * p.BufferScale, 96d * p.BufferScale));
        }

        // 每帧先清空：RenderTargetBitmap.Render 只往上叠加
        using (var ctx = _bitmap.CreateDrawingContext(true))
        {
        }

        _bitmap.Render(_content);
        var stride = pixelSize.Width * 4;
        var length = stride * pixelSize.Height;
        if (_pixels.Length != length) _pixels = new byte[length];
        unsafe
        {
            fixed (byte* ptr = _pixels)
            {
                _bitmap.CopyPixels(new PixelRect(pixelSize), (nint)ptr, length, stride);
            }
        }

        var rgba = _bitmap.Format is { } f && f == PixelFormat.Rgba8888;
        _rgba = rgba;
        _pixelSize = pixelSize;
        _overlay.RenderPixels(_pixels, pixelSize.Width, pixelSize.Height, stride, rgba);
    }

    private bool _rgba;
    private PixelSize _pixelSize;

    /// <summary>最近一次提交的位图中 (x, y) 的 ARGB（预乘；验证用）</summary>
    public uint? Pixel(int x, int y)
    {
        if (_placement is null || x < 0 || y < 0 || x >= _pixelSize.Width || y >= _pixelSize.Height) return null;
        var i = (y * _pixelSize.Width + x) * 4;
        var (b, g, r) = _rgba ? (_pixels[i + 2], _pixels[i + 1], _pixels[i]) : (_pixels[i], _pixels[i + 1], _pixels[i + 2]);
        return (uint)_pixels[i + 3] << 24 | (uint)r << 16 | (uint)g << 8 | b;
    }

    public void Hide()
    {
        if (_placement is null) return;
        _overlay.Hide();
        _placement = null;
    }

    public void Dispose()
    {
        _bitmap?.Dispose();
        _overlay.Dispose();
    }
}
