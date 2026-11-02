using Avalonia.Controls;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Host.Common;

namespace BetterGenshinImpact.Avalonia;

internal sealed partial class UiTests
{
    /// <summary>
    /// 硬件加速选择（--ui-test inference-select）：设置页“硬件加速”只列 CPU 与 ROCm，显示 ROCm 检测结果；
    /// 选 ROCm 点“应用”经 InferenceRuntimeService.Apply 写入配置（待重启生效），配置文件里写出 ROCm 设备；再选回 CPU
    /// </summary>
    private async Task InferenceSelectAsync()
    {
        var host = vm.Host;
        if (host is null) { Check("宿主", false); return; }
        var config = host.Config.HardwareAccelerationConfig;
        vm.Navigate("设置");
        await Settle(800);
        var combo = await WaitFor(() => Named<ComboBox>("InferenceEngineCombo"));
        Check("设置页有推理引擎下拉", combo is not null);
        if (combo is null || vm.Settings.Inference is not { } inf) return;
        var names = inf.Options.Select(o => o.Engine).ToArray();
        Check("只列 CPU 与 ROCm", names.SequenceEqual([InferenceDeviceType.Cpu, InferenceDeviceType.ROCm]), string.Join(",", names));
        Check("ROCm 检测结果", inf.RocmDetection.Length > 0, inf.RocmDetection);
        Check("当前引擎显示", inf.Status.Contains("当前"), inf.Status);

        combo.SelectedItem = inf.Options.First(o => o.Engine == InferenceDeviceType.ROCm);
        await Settle(300);
        await Click(Named<Button>("InferenceApplyButton"), "应用 ROCm");
        Check("配置改为 ROCm、待重启", config.InferenceDevice == InferenceDeviceType.ROCm && config.ValidationState == InferenceValidationState.PendingRestart
            && config.Rocm.DeviceId == LinuxInferenceDeviceDiscovery.RocmDevice.Id, $"{config.InferenceDevice} {config.ValidationState} {config.Rocm.DeviceId}");
        Check("状态提示重启后生效", inf.Status.Contains("重启"), inf.Status);
        Shot("inference-select-rocm.png");
        var file = host.ConfigFilePath;
        Check("配置文件写出 ROCm 设备", file is not null && await Until(() => File.ReadAllText(file).Contains("ROCm:0"), 5000), file);

        combo.SelectedItem = inf.Options.First(o => o.Engine == InferenceDeviceType.Cpu);
        await Settle(300);
        await Click(Named<Button>("InferenceApplyButton"), "应用 CPU");
        Check("选回 CPU", config.InferenceDevice == InferenceDeviceType.Cpu, config.InferenceDevice);
    }

    /// <summary>
    /// ROCm 启动回退（--ui-test inference-fallback，配置为 ROCm 启动）：运行库加载失败（没有 /dev/kfd、没有系统 onnxruntime）
    /// 或会话验证失败（运行库不带 MIGraphX / ROCm provider）时回退 CPU：当前引擎 CPU、配置改回 CPU、记录失败引擎 ROCm，
    /// 提示里有原因（环境变量 BGI_TEST_EXPECT_REASON 给出应包含的文字），设置页显示同一提示；推理照常可用（OCR 会话在 CPU 上创建）
    /// </summary>
    private async Task InferenceFallbackAsync()
    {
        var host = vm.Host;
        if (host is null) { Check("宿主", false); return; }
        var expect = Environment.GetEnvironmentVariable("BGI_TEST_EXPECT_REASON") ?? "onnxruntime-rocm";
        var inference = host.Inference;
        var config = host.Config.HardwareAccelerationConfig;
        Check("推理初始化完成", await Until(() => inference.IsReady && inference.IsCurrentValidated, 90000), inference.IsReady);
        if (!inference.IsReady) return;
        Check("当前引擎回退 CPU", inference.Current.Engine == InferenceDeviceType.Cpu, inference.Current.Description);
        Check("配置改回 CPU、记录失败引擎 ROCm", config.InferenceDevice == InferenceDeviceType.Cpu && config.LastFailedEngine == InferenceDeviceType.ROCm,
            $"{config.InferenceDevice} {config.LastFailedEngine}");
        Check("提示含原因", inference.StartupMessage.Contains("ROCm") && (inference.StartupMessage + config.LastValidationError).Contains(expect),
            inference.StartupMessage.Replace("\r\n", " ") + " | " + config.LastValidationError.Replace("\r\n", " ")[..Math.Min(300, config.LastValidationError.Length)]);
        vm.Navigate("设置");
        await Settle(800);
        vm.Settings.Inference?.Refresh();
        var status = vm.Settings.Inference?.Status ?? "";
        Check("设置页显示回退提示", status.Contains("CPU") && status.Contains("ROCm"), status.Replace("\n", " "));
        Check("下拉显示 CPU", vm.Settings.Inference?.Selected.Engine == InferenceDeviceType.Cpu && Named<ComboBox>("InferenceEngineCombo")?.SelectedItem == vm.Settings.Inference?.Selected,
            vm.Settings.Inference?.Selected.Name);
        Shot("inference-fallback.png");
        Check("回退后 OCR 可用", await Until(() => host.RuntimeService.Current is not null, 30000));
    }
}
