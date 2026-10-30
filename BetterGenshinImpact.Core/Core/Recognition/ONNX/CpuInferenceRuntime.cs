using System.Collections.Generic;
using System.IO;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

/// <summary>
/// 只用 CPU 推理的运行库选择：使用随程序发布的 onnxruntime 原生库（Linux 为 runtimes/linux-x64/native/libonnxruntime.so），
/// 不加载插件目录中的 GPU 运行库。供非 Windows 宿主使用；Windows 主程序仍用 OnnxRuntimeLoader
/// </summary>
public sealed class CpuOnnxRuntimeSelection(string baseDirectory) : IOnnxRuntimeSelection
{
    public InferenceDeviceType InferenceDevice => InferenceDeviceType.Cpu;

    public string RuntimeDirectory { get; } = Path.GetFullPath(baseDirectory);

    public string StatusText => "内置 CPU";

    public string FailureReason => "";
}

/// <summary>只提供 CPU 设备的推理设备探测（与 CpuOnnxRuntimeSelection 搭配）</summary>
public sealed class CpuInferenceDeviceDiscovery : IInferenceDeviceDiscovery
{
    public IReadOnlyList<InferenceDeviceInfo> GetDevices(InferenceDeviceType engine, string runtimeDirectory) =>
        engine == InferenceDeviceType.Cpu ? [ResolvedInferenceRuntime.CpuDevice] : [];
}
