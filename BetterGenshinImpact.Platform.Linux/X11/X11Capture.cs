using System.Runtime.InteropServices;
using BetterGenshinImpact.Platform.Linux.X11.Native;
using OpenCvSharp;
using static BetterGenshinImpact.Platform.Linux.X11.Native.Xlib;

namespace BetterGenshinImpact.Platform.Linux.X11;

public enum X11CaptureMode
{
    /// <summary>MIT-SHM 从根窗口截取游戏客户区所在的屏幕区域（相当于 Windows 上 BitBlt 屏幕 DC：窗口必须可见、未被遮挡）</summary>
    ShmRoot,

    /// <summary>XGetImage 直接读窗口 drawable（不用 SHM，较慢；没有合成器时被遮挡部分的内容未定义）</summary>
    GetImageWindow,

    /// <summary>XGetImage 读根窗口区域</summary>
    GetImageRoot,
}

/// <summary>
/// X11 截图器：默认 XShmGetImage，失败自动回退 XGetImage。
/// <para>
/// 输出帧与 Windows 截图器（BitBlt / WGC）一致，为 BGR 三通道 Mat（CV_8UC3）：X 服务器的 32bpp 像素先零拷贝包装成 BGRA Mat，
/// 再一次 CvtColor 转成独立的 BGR Mat（顺带丢掉 depth 24 下没有意义的 alpha 字节），帧可以跨线程持有。
/// </para>
/// 设置项（<see cref="Start"/> 的 settings）：
/// <list type="bullet">
/// <item><c>x11CaptureMode</c>（或 <c>mode</c>）：<see cref="X11CaptureMode"/> 或其名称</item>
/// <item><c>pixelFormat</c>：<c>"bgr"</c>（默认）或 <c>"bgra"</c>（输出 CV_8UC4，alpha 置 255）</item>
/// </list>
/// </summary>
public sealed unsafe class X11Capture : IGameCapture
{
    private readonly X11Connection _c;
    private readonly bool _owns;
    private IPlatformWindow? _target;
    private X11CaptureMode _mode;
    private bool _bgra;
    private XShmSegmentInfo* _shm;
    private nint _shmImage;
    private int _shmW, _shmH;

    /// <param name="c">X 连接，为 null 时按 $DISPLAY 自行创建</param>
    /// <param name="ownsConnection">Dispose 时是否关闭连接；<paramref name="c"/> 为 null 时总是 true</param>
    /// <param name="mode">默认截图方式，可被 Start 的设置覆盖</param>
    public X11Capture(X11Connection? c = null, bool ownsConnection = false, X11CaptureMode mode = X11CaptureMode.ShmRoot)
    {
        _owns = c == null || ownsConnection;
        _c = c ?? new X11Connection();
        _mode = mode;
        if (_mode == X11CaptureMode.ShmRoot && !XShmQueryExtension(_c.Display)) _mode = X11CaptureMode.GetImageRoot;
    }

    public X11CaptureMode Mode => _mode;

    /// <summary>最近一次回退的原因，用于诊断</summary>
    public string? LastFallbackReason { get; private set; }

    public CaptureCapabilities Capabilities => new(
        _mode switch
        {
            X11CaptureMode.ShmRoot => "X11 XShmGetImage(root)",
            X11CaptureMode.GetImageWindow => "X11 XGetImage(window)",
            _ => "X11 XGetImage(root)",
        },
        CanCaptureOccluded: false, NeedsUserConsent: false, SupportsHdr: false);

    public bool IsCapturing => _target != null;

    public void Start(IPlatformWindow window, Dictionary<string, object>? settings = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (settings != null)
        {
            if ((settings.TryGetValue("x11CaptureMode", out var m) || settings.TryGetValue("mode", out m)) && TryParseMode(m, out var mode))
            {
                _mode = mode;
                if (_mode == X11CaptureMode.ShmRoot && !XShmQueryExtension(_c.Display)) _mode = X11CaptureMode.GetImageRoot;
            }

            if (settings.TryGetValue("pixelFormat", out var pf) && pf is string s)
            {
                _bgra = s.Equals("bgra", StringComparison.OrdinalIgnoreCase);
            }
        }

        _target = window;
    }

    private static bool TryParseMode(object value, out X11CaptureMode mode)
    {
        switch (value)
        {
            case X11CaptureMode m:
                mode = m;
                return true;
            case string s when Enum.TryParse(s, true, out mode):
                return true;
            case int i when Enum.IsDefined(typeof(X11CaptureMode), i):
                mode = (X11CaptureMode)i;
                return true;
            default:
                mode = default;
                return false;
        }
    }

    public GameCaptureFrame? Capture()
    {
        var target = _target;
        if (target == null) return null;
        var rect = target.Viewport.ScreenRect;
        if (rect.IsEmpty) return null;
        lock (_c.Gate)
        {
            // 停止 / 释放与截图线程并发时，连接可能已关闭
            if (_c.IsDisposed) return null;
            if (_mode == X11CaptureMode.ShmRoot)
            {
                var f = CaptureShm(rect);
                if (f != null) return f;
                LastFallbackReason = $"XShmGetImage 失败(err={LastErrorCode})，回退 XGetImage";
                _mode = X11CaptureMode.GetImageRoot;
            }

            return _mode == X11CaptureMode.GetImageWindow
                ? CaptureGetImage(target.Handle, 0, 0, rect.Width, rect.Height, rect)
                : CaptureGetImage(_c.Root, rect.X, rect.Y, rect.Width, rect.Height, rect);
        }
    }

    private GameCaptureFrame? CaptureShm(PixelRect rect)
    {
        // 裁剪到屏幕范围内
        var screen = new PixelRect(0, 0, XDisplayWidth(_c.Display, _c.Screen), XDisplayHeight(_c.Display, _c.Screen));
        var r = rect.Intersect(screen);
        if (r.IsEmpty) return null;
        if (_shmImage == 0 || _shmW != r.Width || _shmH != r.Height)
        {
            FreeShm();
            if (!AllocShm(r.Width, r.Height)) return null;
        }

        LastErrorCode = 0;
        if (!XShmGetImage(_c.Display, _c.Root, _shmImage, r.X, r.Y, AllPlanes)) return null;
        XSync(_c.Display, false);
        if (LastErrorCode != 0) return null;
        return CopyOut(_shmImage, r);
    }

    private bool AllocShm(int w, int h)
    {
        _shm = (XShmSegmentInfo*)NativeMemory.AllocZeroed((nuint)sizeof(XShmSegmentInfo));
        var vis = XDefaultVisual(_c.Display, _c.Screen);
        var depth = (uint)XDefaultDepth(_c.Display, _c.Screen);
        _shmImage = XShmCreateImage(_c.Display, vis, depth, ZPixmap, 0, _shm, (uint)w, (uint)h);
        if (_shmImage == 0) return false;
        var size = (nuint)(XImageLayout.BytesPerLine(_shmImage) * h);
        _shm->shmid = shmget(IPC_PRIVATE, size, IPC_CREAT | 0x180 /*0600*/);
        if (_shm->shmid < 0)
        {
            LastFallbackReason = "shmget 失败";
            return false;
        }

        _shm->shmaddr = shmat(_shm->shmid, 0, 0);
        XImageLayout.SetData(_shmImage, _shm->shmaddr);
        _shm->readOnly = 0;
        LastErrorCode = 0;
        if (!XShmAttach(_c.Display, _shm)) return false;
        XSync(_c.Display, false);
        shmctl(_shm->shmid, IPC_RMID, 0); // 双方 attach 后标记删除，进程退出时自动回收
        if (LastErrorCode != 0) return false;
        _shmW = w;
        _shmH = h;
        return true;
    }

    private void FreeShm()
    {
        if (_shmImage != 0)
        {
            XShmDetach(_c.Display, _shm);
            XImageLayout.SetData(_shmImage, 0);
            XDestroyImage(_shmImage);
            shmdt(_shm->shmaddr);
            _shmImage = 0;
        }

        if (_shm != null)
        {
            NativeMemory.Free(_shm);
            _shm = null;
        }
    }

    private GameCaptureFrame? CaptureGetImage(nint drawable, int x, int y, int w, int h, PixelRect rect)
    {
        LastErrorCode = 0;
        var img = XGetImage(_c.Display, drawable, x, y, (uint)w, (uint)h, AllPlanes, ZPixmap);
        if (img == 0) return null;
        try
        {
            return CopyOut(img, new PixelRect(rect.X, rect.Y, w, h));
        }
        finally
        {
            XDestroyImage(img);
        }
    }

    private GameCaptureFrame CopyOut(nint img, PixelRect r)
    {
        int w = XImageLayout.Width(img), h = XImageLayout.Height(img), bpl = XImageLayout.BytesPerLine(img), bpp = XImageLayout.BitsPerPixel(img);
        if (bpp != 32) throw new NotSupportedException($"仅支持 32bpp TrueColor，当前 {bpp}bpp");

        // 零拷贝包装 X 服务器写入的像素，再转换成独立的 Mat
        using var src = Mat.FromPixelData(h, w, MatType.CV_8UC4, XImageLayout.Data(img), bpl);
        Mat dst;
        if (_bgra)
        {
            dst = src.Clone();
            // depth 24 下 X 像素的 alpha 字节没有意义，统一置 255
            using var flat = dst.Reshape(1, h * w);
            using var alpha = flat.Col(3);
            alpha.SetTo(Scalar.All(255));
        }
        else
        {
            dst = new Mat();
            Cv2.CvtColor(src, dst, ColorConversionCodes.BGRA2BGR);
        }

        return new GameCaptureFrame(dst, new PixelRect(r.X, r.Y, w, h));
    }

    public void Stop()
    {
        _target = null;
        lock (_c.Gate)
        {
            if (!_c.IsDisposed) FreeShm();
        }
    }

    public void Dispose()
    {
        Stop();
        if (_owns) _c.Dispose();
    }
}
