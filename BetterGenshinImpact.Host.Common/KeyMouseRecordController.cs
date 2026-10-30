using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.Core.Recorder;

namespace BetterGenshinImpact.Host.Common;

/// <summary>
/// Linux 宿主的键鼠录制入口（录制回放页与“启动/停止键鼠录制”快捷键共用）：
/// 录制期间把全局键鼠监听（XInput2 原始事件）接到 Core 的录制，保存与 Windows 版相同（User/KeyMouseScript/BetterGI_GCM_*.json）
/// </summary>
public sealed class KeyMouseRecordController(KeyMouseRecordService service)
{
    private readonly object _gate = new();
    private KeyMouseRecordInputBridge? _bridge;

    public bool IsRecording => service.IsRecording;

    /// <summary>开始录制（3 秒倒计时）；失败返回 false</summary>
    public async Task<bool> StartAsync(string? subFolder = null)
    {
        lock (_gate)
        {
            _bridge ??= new KeyMouseRecordInputBridge(SharedGlobalInputHook.Instance);
        }

        var started = await service.StartAsync(subFolder, this);
        if (!started && !service.IsRecording)
        {
            DisposeBridge();
        }

        return started;
    }

    /// <summary>停止并保存，返回文件路径；未在录制时返回 null</summary>
    public string? Stop()
    {
        var path = service.Stop();
        if (!service.IsRecording)
        {
            DisposeBridge();
        }

        return path;
    }

    /// <summary>与 WPF 快捷键相同：未录制时延迟 300 ms（避免录入快捷键本身）后开始，录制中则停止</summary>
    public async Task<string?> ToggleAsync()
    {
        if (!service.IsRecording)
        {
            await Task.Delay(300);
            await StartAsync();
            return null;
        }

        return Stop();
    }

    private void DisposeBridge()
    {
        lock (_gate)
        {
            _bridge?.Dispose();
            _bridge = null;
        }
    }
}
