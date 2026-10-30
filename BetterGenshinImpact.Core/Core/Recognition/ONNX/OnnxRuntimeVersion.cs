using System;
using Microsoft.ML.OnnxRuntime;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

/// <summary>
/// 主程序按推理类型选定原生运行库，在任何 ONNX 原生调用之前创建一次。
/// 运行中的进程不切换、不卸载已选中的运行库。
/// </summary>
public interface IOnnxRuntimeSelection
{
    InferenceDeviceType InferenceDevice { get; }
    string RuntimeDirectory { get; }
    string StatusText { get; }
    string FailureReason { get; }
}

/// <summary>
/// 编译时引用的托管 ONNX Runtime 版本（主.次.修订）。原生库必须与之一致。
/// 原为 OnnxRuntimeLoader.RequiredVersion（Windows 插件加载器留在主程序，版本号与之共用）
/// </summary>
public static class OnnxRuntimeVersion
{
    public static string Required { get; } = GetRequiredVersion();

    private static string GetRequiredVersion()
    {
        var version = typeof(InferenceSession).Assembly.GetName().Version
                      ?? throw new InvalidOperationException("无法读取编译时 ONNX Runtime 版本。");
        return version.ToString(3);
    }
}
