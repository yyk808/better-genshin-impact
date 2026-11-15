using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BetterGenshinImpact.Host.Common;

/// <summary>
/// 在首次 P/Invoke 之前为 OpenCvSharp 注册原生库搜索路径。
/// <c>dotnet exec</c> 可移植布局（bin/Debug 无 RID）的默认探测是 runtimes/linux-x64/native 与共享框架目录，
/// 不会搜索 AppContext.BaseDirectory；linux-x64 扁平发布与 apphost 则会搜索程序根目录。
/// 把完整 libOpenCvSharpExtern.so 放在程序根（发布/包装约定）后，若没有本解析器，
/// 本地 <c>dotnet bgi-avalonia.dll</c> 仍会逐帧 TypeInitializationException。
/// 本解析器优先加载程序根目录中的库，其次 runtimes/linux-x64/native，以及环境变量 OpenCvSharpExternPath。
/// </summary>
public static class OpenCvNativeLibrary
{
    private static int _installed;

    [ModuleInitializer]
    internal static void InitializeModule() => InstallResolver();

    public static void InstallResolver()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
        {
            return;
        }

        NativeLibrary.SetDllImportResolver(typeof(OpenCvSharp.Cv2).Assembly, Resolve);
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName.IndexOf("OpenCvSharpExtern", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return IntPtr.Zero;
        }

        foreach (var path in CandidatePaths())
        {
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    public static IEnumerable<string> CandidatePaths()
    {
        var dir = AppContext.BaseDirectory;
        string[] names = OperatingSystem.IsWindows()
            ? ["OpenCvSharpExtern.dll", "libOpenCvSharpExtern.dll"]
            : ["libOpenCvSharpExtern.so", "OpenCvSharpExtern.so", "libOpenCvSharpExtern", "OpenCvSharpExtern"];
        foreach (var name in names)
        {
            yield return Path.Combine(dir, name);
            yield return Path.Combine(dir, "runtimes", "linux-x64", "native", name);
        }

        var env = Environment.GetEnvironmentVariable("OpenCvSharpExternPath");
        if (!string.IsNullOrWhiteSpace(env))
        {
            yield return env;
        }
    }
}
