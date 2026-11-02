using System.Runtime.InteropServices;

namespace BetterGenshinImpact.Platform.Linux.Wayland;

/// <summary>evdev / uinput 用到的 libc 调用与常量（linux/input.h、linux/uinput.h，x86_64）</summary>
internal static unsafe partial class LinuxInputNative
{
    public const int O_RDONLY = 0, O_WRONLY = 1, O_NONBLOCK = 0x800, O_CLOEXEC = 0x80000;
    public const short POLLIN = 1;
    public const ushort EV_SYN = 0, EV_KEY = 1, EV_REL = 2, EV_ABS = 3;
    public const ushort SYN_REPORT = 0, REL_X = 0, REL_Y = 1, REL_WHEEL = 8, ABS_X = 0, ABS_Y = 1;
    public const int KEY_MAX = 0x2ff;

    [StructLayout(LayoutKind.Sequential)]
    public struct InputEvent
    {
        public long TvSec;
        public long TvUsec;
        public ushort Type;
        public ushort Code;
        public int Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int open(string path, int flags);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int close(int fd);

    [LibraryImport("libc", SetLastError = true)]
    public static partial nint read(int fd, void* buf, nint count);

    [LibraryImport("libc", SetLastError = true)]
    public static partial nint write(int fd, void* buf, nint count);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int poll(PollFd* fds, nuint nfds, int timeout);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int ioctl(int fd, nuint request, void* arg);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int ioctl(int fd, nuint request, nint arg);

    // _IOC(dir, type, nr, size)：dir 2 位、size 14 位、type 8 位、nr 8 位
    private static nuint Ioc(uint dir, uint type, uint nr, uint size) => (nuint)((dir << 30) | (size << 16) | (type << 8) | nr);

    /// <summary>EVIOCGBIT(ev, len)：读取设备支持的事件码位图</summary>
    public static nuint EVIOCGBIT(uint ev, uint len) => Ioc(2, 'E', 0x20 + ev, len);

    /// <summary>EVIOCGNAME(len)</summary>
    public static nuint EVIOCGNAME(uint len) => Ioc(2, 'E', 0x06, len);

    /// <summary>EVIOCGKEY(len)：当前按下的键位图</summary>
    public static nuint EVIOCGKEY(uint len) => Ioc(2, 'E', 0x18, len);

    public static readonly nuint UI_DEV_CREATE = Ioc(0, 'U', 1, 0);
    public static readonly nuint UI_DEV_DESTROY = Ioc(0, 'U', 2, 0);
    public static readonly nuint UI_DEV_SETUP = Ioc(1, 'U', 3, 92); // struct uinput_setup
    public static readonly nuint UI_ABS_SETUP = Ioc(1, 'U', 4, 28); // struct uinput_abs_setup
    public static readonly nuint UI_SET_EVBIT = Ioc(1, 'U', 100, 4);
    public static readonly nuint UI_SET_KEYBIT = Ioc(1, 'U', 101, 4);
    public static readonly nuint UI_SET_RELBIT = Ioc(1, 'U', 102, 4);
    public static readonly nuint UI_SET_ABSBIT = Ioc(1, 'U', 103, 4);

    public static bool TestBit(ReadOnlySpan<byte> bits, int bit) => bit / 8 < bits.Length && (bits[bit / 8] & (1 << (bit % 8))) != 0;

    public static string Errno() => Marshal.GetLastPInvokeErrorMessage();
}
