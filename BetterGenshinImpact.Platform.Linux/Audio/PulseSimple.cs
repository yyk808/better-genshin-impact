using System.Runtime.InteropServices;

namespace BetterGenshinImpact.Platform.Linux.Audio;

/// <summary>
/// libpulse-simple 的最小绑定（PulseAudio 与 PipeWire 的 pipewire-pulse 都提供该接口）。运行时按 soname 加载，不随程序分发 .so
/// </summary>
internal static partial class PulseSimple
{
    private const string Simple = "libpulse-simple.so.0";
    private const string Pulse = "libpulse.so.0";

    public const int StreamPlayback = 1;

    public enum SampleFormat
    {
        U8 = 0,
        S16Le = 3,
        Float32Le = 5,
        S32Le = 7,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SampleSpec
    {
        public SampleFormat Format;
        public uint Rate;
        public byte Channels;
    }

    [LibraryImport(Simple, EntryPoint = "pa_simple_new", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint New(string? server, string name, int dir, string? dev, string streamName, in SampleSpec ss, nint map, nint attr, out int error);

    /// <summary>pa_buffer_attr；uint.MaxValue 表示由服务器决定</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BufferAttr
    {
        public uint MaxLength;
        public uint TLength;
        public uint PreBuf;
        public uint MinReq;
        public uint FragSize;
    }

    [LibraryImport(Simple, EntryPoint = "pa_simple_new", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint New(string? server, string name, int dir, string? dev, string streamName, in SampleSpec ss, nint map, in BufferAttr attr, out int error);

    [LibraryImport(Simple, EntryPoint = "pa_simple_write")]
    public static unsafe partial int Write(nint s, byte* data, nuint bytes, out int error);

    [LibraryImport(Simple, EntryPoint = "pa_simple_drain")]
    public static partial int Drain(nint s, out int error);

    [LibraryImport(Simple, EntryPoint = "pa_simple_flush")]
    public static partial int Flush(nint s, out int error);

    [LibraryImport(Simple, EntryPoint = "pa_simple_free")]
    public static partial void Free(nint s);

    [LibraryImport(Pulse, EntryPoint = "pa_strerror")]
    private static partial nint StrErrorNative(int error);

    public static string StrError(int error)
    {
        try
        {
            return Marshal.PtrToStringUTF8(StrErrorNative(error)) ?? $"错误 {error}";
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return $"错误 {error}";
        }
    }
}
