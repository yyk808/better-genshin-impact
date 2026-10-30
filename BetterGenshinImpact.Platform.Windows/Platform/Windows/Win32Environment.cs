using System;
using System.Runtime.InteropServices;

namespace BetterGenshinImpact.Platform.Windows;

/// <summary>Win32 运行环境探测：是否运行在 Wine 中（ntdll 导出 wine_get_version）</summary>
public static partial class Win32Environment
{
    private static readonly Lazy<string?> WineVersionLazy = new(QueryWineVersion);

    public static bool IsRunningOnWine => WineVersionLazy.Value is not null;

    /// <summary>Wine 版本号；不在 Wine 中时为 null</summary>
    public static string? WineVersion => WineVersionLazy.Value;

    public static PlatformKind Kind => IsRunningOnWine ? PlatformKind.WindowsOnWine : PlatformKind.Windows;

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string name);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetProcAddress(nint module, string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint WineGetVersion();

    private static string? QueryWineVersion()
    {
        try
        {
            var ntdll = GetModuleHandle("ntdll.dll");
            if (ntdll == 0)
            {
                return null;
            }

            var fn = GetProcAddress(ntdll, "wine_get_version");
            if (fn == 0)
            {
                return null;
            }

            var ptr = Marshal.GetDelegateForFunctionPointer<WineGetVersion>(fn)();
            return Marshal.PtrToStringAnsi(ptr) ?? "unknown";
        }
        catch
        {
            return null;
        }
    }
}
