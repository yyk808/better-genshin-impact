using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BetterGenshinImpact.Platform.Linux.X11.Native;

/// <summary>libX11 / libXext(MIT-SHM) / libXtst / libXi / libXfixes / libc 的最小 P/Invoke 绑定（x86_64 LP64）。</summary>
public static unsafe partial class Xlib
{
    const string X11 = "libX11.so.6", Xext = "libXext.so.6", Xtst = "libXtst.so.6", Xi = "libXi.so.6", Xfixes = "libXfixes.so.3", LibC = "libc";

    public const int KeyPress = 2, KeyRelease = 3, ButtonPress = 4, ButtonRelease = 5, MotionNotify = 6,
        FocusIn = 9, FocusOut = 10, Expose = 12, DestroyNotify = 17, UnmapNotify = 18, MapNotify = 19, ConfigureNotify = 22,
        ClientMessage = 33, GenericEvent = 35;
    public const long KeyPressMask = 1L << 0, KeyReleaseMask = 1L << 1, ButtonPressMask = 1L << 2, ButtonReleaseMask = 1L << 3,
        PointerMotionMask = 1L << 6, ExposureMask = 1L << 15, StructureNotifyMask = 1L << 17,
        SubstructureNotifyMask = 1L << 19, SubstructureRedirectMask = 1L << 20, FocusChangeMask = 1L << 21, PropertyChangeMask = 1L << 22;
    public const int ZPixmap = 2, GrabModeAsync = 1, GrabSuccess = 0;
    public const uint ShiftMask = 1, LockMask = 2, ControlMask = 4, Mod1Mask = 8, Mod2Mask = 16, Mod4Mask = 64;
    public const nint XA_ATOM = 4, XA_CARDINAL = 6, XA_STRING = 31, XA_WINDOW = 33, AnyPropertyType = 0;
    public const ulong AllPlanes = ulong.MaxValue;

    static Xlib()
    {
        XInitThreads();
        XSetErrorHandler(&OnXError);
    }

    /// <summary>确保静态构造（XInitThreads + 错误处理器）在任何 XOpenDisplay 之前执行。</summary>
    public static void EnsureInit() { }

    [ThreadStatic] public static int LastErrorCode;
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    static int OnXError(nint display, nint errorEvent)
    {
        // XErrorEvent: int type; Display*; XID resourceid; ulong serial; uchar error_code; ...
        LastErrorCode = *((byte*)errorEvent + 32);
        return 0; // 不退出进程（默认处理器会 exit）
    }

    [LibraryImport(X11)] public static partial int XInitThreads();
    [LibraryImport(X11)] public static partial nint XSetErrorHandler(delegate* unmanaged[Cdecl]<nint, nint, int> handler);
    [LibraryImport(X11, StringMarshalling = StringMarshalling.Utf8)] public static partial nint XOpenDisplay(string? name);
    [LibraryImport(X11)] public static partial int XCloseDisplay(nint d);
    [LibraryImport(X11)] public static partial int XDefaultScreen(nint d);
    [LibraryImport(X11)] public static partial nint XRootWindow(nint d, int screen);
    [LibraryImport(X11)] public static partial nint XDefaultVisual(nint d, int screen);
    [LibraryImport(X11)] public static partial int XDefaultDepth(nint d, int screen);
    [LibraryImport(X11)] public static partial int XDisplayWidth(nint d, int screen);
    [LibraryImport(X11)] public static partial int XDisplayHeight(nint d, int screen);
    [LibraryImport(X11)] public static partial int XConnectionNumber(nint d);
    [LibraryImport(X11)] public static partial int XFlush(nint d);
    [LibraryImport(X11)] public static partial int XSync(nint d, [MarshalAs(UnmanagedType.Bool)] bool discard);
    [LibraryImport(X11)] public static partial int XPending(nint d);
    [LibraryImport(X11)] public static partial int XNextEvent(nint d, byte* ev);
    [LibraryImport(X11)] public static partial int XFree(nint p);
    [LibraryImport(X11, StringMarshalling = StringMarshalling.Utf8)] public static partial nint XInternAtom(nint d, string name, [MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);
    [LibraryImport(X11)] public static partial nint XGetAtomName(nint d, nint atom);
    [LibraryImport(X11)]
    public static partial int XGetWindowProperty(nint d, nint w, nint property, nint offset, nint length, [MarshalAs(UnmanagedType.Bool)] bool delete,
        nint reqType, out nint actualType, out int actualFormat, out nint nitems, out nint bytesAfter, out nint prop);
    [LibraryImport(X11)] public static partial int XChangeProperty(nint d, nint w, nint property, nint type, int format, int mode, byte* data, int nelements);
    [LibraryImport(X11)] public static partial int XQueryTree(nint d, nint w, out nint root, out nint parent, out nint children, out uint nchildren);
    [LibraryImport(X11)] public static partial int XGetGeometry(nint d, nint drawable, out nint root, out int x, out int y, out uint width, out uint height, out uint border, out uint depth);
    [LibraryImport(X11)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool XTranslateCoordinates(nint d, nint src, nint dest, int srcX, int srcY, out int destX, out int destY, out nint child);
    [LibraryImport(X11)] public static partial int XSendEvent(nint d, nint w, [MarshalAs(UnmanagedType.Bool)] bool propagate, nint mask, byte* ev);
    [LibraryImport(X11)] public static partial int XMapRaised(nint d, nint w);
    [LibraryImport(X11)] public static partial int XMapWindow(nint d, nint w);
    [LibraryImport(X11)] public static partial int XRaiseWindow(nint d, nint w);
    [LibraryImport(X11)] public static partial int XSetInputFocus(nint d, nint w, int revertTo, nint time);
    [LibraryImport(X11)] public static partial int XGetInputFocus(nint d, out nint w, out int revertTo);
    [LibraryImport(X11)] public static partial nint XGetImage(nint d, nint drawable, int x, int y, uint w, uint h, ulong planeMask, int format);
    [LibraryImport(X11)] public static partial int XDestroyImage(nint image);
    [LibraryImport(X11)] public static partial nint XCreateImage(nint d, nint visual, uint depth, int format, int offset, nint data, uint w, uint h, int bitmapPad, int bytesPerLine);
    [LibraryImport(X11)] public static partial int XPutImage(nint d, nint drawable, nint gc, nint image, int srcX, int srcY, int dstX, int dstY, uint w, uint h);
    [LibraryImport(X11)] public static partial nint XCreateGC(nint d, nint drawable, nuint mask, nint values);
    [LibraryImport(X11)] public static partial int XFreeGC(nint d, nint gc);
    [LibraryImport(X11)] public static partial nint XCreateSimpleWindow(nint d, nint parent, int x, int y, uint w, uint h, uint border, nuint borderPixel, nuint background);
    [LibraryImport(X11)] public static partial int XDestroyWindow(nint d, nint w);
    [LibraryImport(X11)] public static partial int XSelectInput(nint d, nint w, nint mask);
    [LibraryImport(X11, StringMarshalling = StringMarshalling.Utf8)] public static partial int XStoreName(nint d, nint w, string name);
    [LibraryImport(X11)] public static partial int XSetClassHint(nint d, nint w, nint* classHint);
    [LibraryImport(X11)] public static partial int XSetWMProtocols(nint d, nint w, nint* protocols, int count);
    [LibraryImport(X11)] public static partial int XKeysymToKeycode(nint d, nint keysym);
    [LibraryImport(X11)] public static partial nint XkbKeycodeToKeysym(nint d, byte keycode, int group, int level);
    [LibraryImport(X11)] public static partial int XQueryKeymap(nint d, byte* keys32);
    [LibraryImport(X11)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool XQueryPointer(nint d, nint w, out nint root, out nint child, out int rootX, out int rootY, out int winX, out int winY, out uint mask);
    [LibraryImport(X11)] public static partial int XGrabKey(nint d, int keycode, uint modifiers, nint grabWindow, [MarshalAs(UnmanagedType.Bool)] bool ownerEvents, int pointerMode, int keyboardMode);
    [LibraryImport(X11)] public static partial int XUngrabKey(nint d, int keycode, uint modifiers, nint grabWindow);
    [LibraryImport(X11)] public static partial int XGrabPointer(nint d, nint w, [MarshalAs(UnmanagedType.Bool)] bool ownerEvents, uint mask, int pointerMode, int keyboardMode, nint confineTo, nint cursor, nint time);
    [LibraryImport(X11)] public static partial int XUngrabPointer(nint d, nint time);
    [LibraryImport(X11)] public static partial int XWarpPointer(nint d, nint src, nint dest, int srcX, int srcY, uint srcW, uint srcH, int destX, int destY);
    [LibraryImport(X11, StringMarshalling = StringMarshalling.Utf8)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool XQueryExtension(nint d, string name, out int opcode, out int firstEvent, out int firstError);
    [LibraryImport(X11)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool XGetEventData(nint d, byte* cookie);
    [LibraryImport(X11)] public static partial void XFreeEventData(nint d, byte* cookie);
    [LibraryImport(X11)] public static partial nint XSetWMNormalHints(nint d, nint w, byte* hints);

    [LibraryImport(X11)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool XkbSetDetectableAutoRepeat(nint d, [MarshalAs(UnmanagedType.Bool)] bool detectable, nint supported);
    [LibraryImport(X11)] public static partial int XMoveWindow(nint d, nint w, int x, int y);

    // MIT-SHM
    [LibraryImport(Xext)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool XShmQueryExtension(nint d);
    [LibraryImport(Xext)] public static partial nint XShmCreateImage(nint d, nint visual, uint depth, int format, nint data, XShmSegmentInfo* shminfo, uint w, uint h);
    [LibraryImport(Xext)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool XShmAttach(nint d, XShmSegmentInfo* shminfo);
    [LibraryImport(Xext)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool XShmDetach(nint d, XShmSegmentInfo* shminfo);
    [LibraryImport(Xext)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool XShmGetImage(nint d, nint drawable, nint image, int x, int y, ulong planeMask);

    // XTEST
    [LibraryImport(Xtst)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool XTestQueryExtension(nint d, out int ev, out int err, out int major, out int minor);
    [LibraryImport(Xtst)] public static partial int XTestFakeKeyEvent(nint d, uint keycode, [MarshalAs(UnmanagedType.Bool)] bool isPress, nuint delay);
    [LibraryImport(Xtst)] public static partial int XTestFakeButtonEvent(nint d, uint button, [MarshalAs(UnmanagedType.Bool)] bool isPress, nuint delay);
    [LibraryImport(Xtst)] public static partial int XTestFakeMotionEvent(nint d, int screen, int x, int y, nuint delay);
    [LibraryImport(Xtst)] public static partial int XTestFakeRelativeMotionEvent(nint d, int dx, int dy, nuint delay);

    // XInput2
    [LibraryImport(Xi)] public static partial int XIQueryVersion(nint d, ref int major, ref int minor);
    [LibraryImport(Xi)] public static partial int XISelectEvents(nint d, nint w, XIEventMask* masks, int num);

    // XFixes
    [LibraryImport(Xfixes)] public static partial void XFixesHideCursor(nint d, nint w);
    [LibraryImport(Xfixes)] public static partial void XFixesShowCursor(nint d, nint w);

    // libc SysV SHM
    public const int IPC_PRIVATE = 0, IPC_CREAT = 0x200, IPC_RMID = 0;
    [LibraryImport(LibC, SetLastError = true)] public static partial int shmget(int key, nuint size, int flags);
    [LibraryImport(LibC, SetLastError = true)] public static partial nint shmat(int id, nint addr, int flags);
    [LibraryImport(LibC)] public static partial int shmdt(nint addr);
    [LibraryImport(LibC)] public static partial int shmctl(int id, int cmd, nint buf);
    [LibraryImport(LibC)] public static partial int poll(PollFd* fds, nuint n, int timeout);

    public static string? GetAtomName(nint d, nint atom)
    {
        var p = XGetAtomName(d, atom); if (p == 0) return null;
        try { return Marshal.PtrToStringUTF8(p); } finally { XFree(p); }
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct XShmSegmentInfo { public nint shmseg; public int shmid; public nint shmaddr; public int readOnly; }

[StructLayout(LayoutKind.Sequential)]
public unsafe struct XIEventMask { public int deviceid; public int mask_len; public byte* mask; }

[StructLayout(LayoutKind.Sequential)]
public struct PollFd { public int fd; public short events; public short revents; }

/// <summary>XImage 字段偏移（x86_64）。</summary>
public static unsafe class XImageLayout
{
    public static int Width(nint img) => *(int*)(img + 0);
    public static int Height(nint img) => *(int*)(img + 4);
    public static nint Data(nint img) => *(nint*)(img + 16);
    public static void SetData(nint img, nint data) => *(nint*)(img + 16) = data;
    public static int Depth(nint img) => *(int*)(img + 40);
    public static int BytesPerLine(nint img) => *(int*)(img + 44);
    public static int BitsPerPixel(nint img) => *(int*)(img + 48);
}
