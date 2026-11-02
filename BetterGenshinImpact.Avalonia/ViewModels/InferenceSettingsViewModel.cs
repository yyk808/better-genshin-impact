using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Host.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>推理引擎的一项（CPU / ROCm）</summary>
public sealed record InferenceEngineOption(InferenceDeviceType Engine, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// 设置页“硬件加速”：只选推理引擎（CPU / ROCm）和 OCR 是否强制 CPU，经 Core 的 InferenceRuntimeService.Apply 保存，重启后生效；
/// 启动时选运行库、验证与失败回退 CPU 都是 Core 原有流程（LinuxOnnxRuntimeSelection 只负责找系统 onnxruntime）
/// </summary>
public sealed partial class InferenceSettingsViewModel : ObservableObject
{
    private readonly GameHost _host;

    [ObservableProperty] private InferenceEngineOption _selected;
    [ObservableProperty] private bool _cpuOcr;
    [ObservableProperty] private string _status = "";

    public InferenceSettingsViewModel(GameHost host)
    {
        _host = host;
        Options = InferenceDeviceTypeExtensions.Available.Select(e => new InferenceEngineOption(e, Label(e))).ToList();
        var config = host.Config.HardwareAccelerationConfig;
        _selected = Options.FirstOrDefault(o => o.Engine == config.InferenceDevice) ?? Options[0];
        _cpuOcr = config.CpuOcr;
        Refresh();
        // 启动验证失败回退 CPU 等：配置变化后同步下拉与提示
        host.Inference.SettingsChanged += (_, _) => global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Selected = Options.FirstOrDefault(o => o.Engine == _host.Config.HardwareAccelerationConfig.InferenceDevice) ?? Selected;
            Refresh();
        });
    }

    public IReadOnlyList<InferenceEngineOption> Options { get; }

    /// <summary>ROCm 是否可用的检测结果（/dev/kfd、系统 onnxruntime）</summary>
    public string RocmDetection
    {
        get
        {
            if (!LinuxOnnxRuntimeSelection.HasRocmDevice)
                return $"未检测到 {LinuxOnnxRuntimeSelection.KfdPath}（没有 AMD ROCm 显卡驱动），ROCm 不可用。";
            return LinuxOnnxRuntimeSelection.FindSystemLibrary(out var reason) is { } lib
                ? $"检测到 {LinuxOnnxRuntimeSelection.KfdPath} 与系统 onnxruntime：{lib}"
                : reason;
        }
    }

    public static string Label(InferenceDeviceType engine) => engine switch
    {
        InferenceDeviceType.Cpu => "CPU",
        InferenceDeviceType.ROCm => "ROCm（AMD 显卡，MIGraphX）",
        _ => engine.ToString(),
    };

    public void Refresh()
    {
        var inference = _host.Inference;
        var config = _host.Config.HardwareAccelerationConfig;
        if (!inference.HasPendingRestart && Options.FirstOrDefault(o => o.Engine == config.InferenceDevice) is { } saved) Selected = saved;
        var lines = new List<string>();
        lines.Add(inference.IsReady ? $"当前：{inference.Current.Description}" : "当前：推理尚未初始化（启动截图器时初始化）");
        if (inference.HasPendingRestart || config.ValidationState == InferenceValidationState.PendingRestart)
            lines.Add($"已保存：{Label(config.InferenceDevice)}，重启 BetterGI 后生效");
        if (!string.IsNullOrEmpty(inference.StartupMessage)) lines.Add(inference.StartupMessage.Replace("\r\n\r\n", "\n").Replace("\r\n", "\n"));
        else if (config.LastFailedEngine is { } failed && !string.IsNullOrEmpty(config.LastValidationError))
            lines.Add($"上次 {failed} 失败：" + config.LastValidationError.Replace("\r\n\r\n", "\n").Replace("\r\n", "\n"));
        Status = string.Join("\n", lines);
    }

    [RelayCommand]
    private void Apply()
    {
        var engine = Selected.Engine;
        var deviceId = engine == InferenceDeviceType.Cpu ? "CPU" : LinuxInferenceDeviceDiscovery.RocmDevice.Id;
        var config = _host.Config.HardwareAccelerationConfig;
        _host.Inference.Apply(engine, new InferenceEngineSettings { DeviceId = deviceId, EnableModelCache = config.GetEngineSettings(engine).EnableModelCache },
            CpuOcr, config.Cpu.CpuThreads, config.PaddleOcrCpu.ToOptions());
        Refresh();
    }
}
