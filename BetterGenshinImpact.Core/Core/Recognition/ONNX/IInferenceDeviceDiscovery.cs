using System.Collections.Generic;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

/// <summary>推理设备探测（Windows 实现 InferenceDeviceDiscovery 经 DXGI / NVML 探测，留在主程序）</summary>
public interface IInferenceDeviceDiscovery
{
    IReadOnlyList<InferenceDeviceInfo> GetDevices(InferenceDeviceType engine, string runtimeDirectory);
}
