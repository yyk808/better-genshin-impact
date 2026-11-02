using System.Runtime.InteropServices;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;
using Microsoft.ML.OnnxRuntime;
using Serilog;

namespace BetterGenshinImpact.Host.Common;

/// <summary>
/// Linux 推理运行库选择：CPU 用随程序发布的 onnxruntime；ROCm（AMD 显卡）直接用系统已安装、带 MIGraphX 的 onnxruntime
/// （如 Arch 的 onnxruntime-rocm 包提供的 /usr/lib/libonnxruntime.so），不自带、不下载、不编译。
/// 在任何 ONNX 原生调用之前经 <see cref="NativeLibrary.SetDllImportResolver"/> 把托管 OnnxRuntime 的 "onnxruntime" 指到系统库；
/// 条件不满足时选 CPU 并给出原因，由 <see cref="InferenceRuntimeService"/> 回退 CPU、在设置页和日志里提示（与 Windows 运行库加载失败相同）
/// </summary>
public sealed class LinuxOnnxRuntimeSelection : IOnnxRuntimeSelection
{
    /// <summary>需要安装的包（检测失败时提示）</summary>
    public const string PackageHint =
        "Arch / Manjaro / EndeavourOS：sudo pacman -S onnxruntime-rocm（会一并装上 migraphx、rocm-hip-sdk），并把用户加入 render、video 组；" +
        "其他发行版：安装 ROCm 与带 MIGraphX 的 onnxruntime 共享库（libonnxruntime.so 与 libonnxruntime_providers_migraphx.so），" +
        "或用环境变量 BGI_ONNXRUNTIME_LIB 指定 libonnxruntime.so 的路径。详见 Docs/linux-migration/rocm-checklist.md";

    private static int _resolverInstalled;

    private LinuxOnnxRuntimeSelection(InferenceDeviceType device, string runtimeDirectory, string status, string failure)
    {
        InferenceDevice = device;
        RuntimeDirectory = runtimeDirectory;
        StatusText = status;
        FailureReason = failure;
    }

    public InferenceDeviceType InferenceDevice { get; }
    public string RuntimeDirectory { get; }
    public string StatusText { get; }
    public string FailureReason { get; }

    /// <summary>已加载的系统 onnxruntime 路径（未使用系统库时为 null）</summary>
    public string? LibraryPath { get; private init; }

    /// <summary>/dev/kfd（ROCm 内核驱动接口；测试可用 BGI_ROCM_KFD 指定）</summary>
    public static string KfdPath => Environment.GetEnvironmentVariable("BGI_ROCM_KFD") is { Length: > 0 } p ? p : "/dev/kfd";

    /// <summary>有 AMD ROCm 设备（只看 /dev/kfd 是否存在）</summary>
    public static bool HasRocmDevice => File.Exists(KfdPath) || Directory.Exists(KfdPath);

    /// <summary>系统 onnxruntime 的候选路径（环境变量 BGI_ONNXRUNTIME_LIB 优先）</summary>
    public static IEnumerable<string> Candidates()
    {
        if (Environment.GetEnvironmentVariable("BGI_ONNXRUNTIME_LIB") is { Length: > 0 } env) yield return env;
        foreach (var dir in new[] { "/usr/lib", "/usr/lib64", "/usr/lib/x86_64-linux-gnu", "/usr/local/lib", "/opt/rocm/lib" })
        {
            yield return Path.Combine(dir, "libonnxruntime.so.1");
            yield return Path.Combine(dir, "libonnxruntime.so");
        }
    }

    /// <summary>找带 MIGraphX（或 ROCm）provider 的系统 onnxruntime；找不到时返回 null 并给出原因</summary>
    public static string? FindSystemLibrary(out string reason)
    {
        var tried = new List<string>();
        foreach (var path in Candidates().Distinct())
        {
            if (!File.Exists(path)) { tried.Add(path); continue; }
            var dir = Path.GetDirectoryName(path)!;
            if (File.Exists(Path.Combine(dir, "libonnxruntime_providers_migraphx.so")) || File.Exists(Path.Combine(dir, "libonnxruntime_providers_rocm.so")))
            {
                reason = "";
                return path;
            }

            tried.Add(path + "（同目录没有 libonnxruntime_providers_migraphx.so，不是 ROCm 版）");
        }

        reason = "没有找到带 MIGraphX 的系统 onnxruntime（查找过：" + string.Join("，", tried) + "）。" + PackageHint;
        return null;
    }

    /// <summary>
    /// 按配置选运行库：选了 ROCm 且有 /dev/kfd、找到系统库时把 onnxruntime 指到系统库；否则用 CPU（原因交给回退提示）。
    /// 必须在任何 ONNX 调用之前、每个进程只调用一次
    /// </summary>
    public static LinuxOnnxRuntimeSelection Select(HardwareAccelerationConfig config, string baseDirectory, ILogger log)
    {
        var dir = Path.GetFullPath(baseDirectory);
        if (config.InferenceDevice != InferenceDeviceType.ROCm)
        {
            return new(InferenceDeviceType.Cpu, dir, "内置 CPU", "");
        }

        if (!HasRocmDevice)
        {
            return Cpu(dir, $"没有 {KfdPath}：没有可用的 AMD ROCm 显卡驱动（amdgpu 内核驱动），或当前用户无权访问。" + PackageHint, log);
        }

        if (FindSystemLibrary(out var reason) is not { } lib)
        {
            return Cpu(dir, reason, log);
        }

        try
        {
            var handle = NativeLibrary.Load(lib);
            if (Interlocked.Exchange(ref _resolverInstalled, 1) == 0)
            {
                NativeLibrary.SetDllImportResolver(typeof(InferenceSession).Assembly,
                    (name, _, _) => name is "onnxruntime" or "libonnxruntime" or "libonnxruntime.so" ? handle : IntPtr.Zero);
            }

            log.Information("推理：ROCm 使用系统 onnxruntime {Lib}", lib);
            return new(InferenceDeviceType.ROCm, Path.GetDirectoryName(lib)!, $"系统 onnxruntime（{lib}）", "") { LibraryPath = lib };
        }
        catch (Exception e)
        {
            return Cpu(dir, $"加载系统 onnxruntime 失败：{lib}：{e.Message}。" + PackageHint, log);
        }
    }

    private static LinuxOnnxRuntimeSelection Cpu(string dir, string reason, ILogger log)
    {
        log.Warning("推理：ROCm 不可用，使用 CPU：{Reason}", reason);
        return new(InferenceDeviceType.Cpu, dir, "内置 CPU", reason);
    }
}

/// <summary>Linux 推理设备：CPU；有 /dev/kfd 时 ROCm 设备 0（不做复杂显卡探测）</summary>
public sealed class LinuxInferenceDeviceDiscovery : IInferenceDeviceDiscovery
{
    public static InferenceDeviceInfo RocmDevice { get; } = new("ROCm:0", "AMD 显卡（ROCm 设备 0）", 0, "0");

    public IReadOnlyList<InferenceDeviceInfo> GetDevices(InferenceDeviceType engine, string runtimeDirectory) => engine switch
    {
        InferenceDeviceType.Cpu => [ResolvedInferenceRuntime.CpuDevice],
        InferenceDeviceType.ROCm when LinuxOnnxRuntimeSelection.HasRocmDevice => [RocmDevice],
        _ => [],
    };
}
