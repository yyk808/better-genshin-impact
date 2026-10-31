using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.ViewModel.Pages.QuestVoiceSettings;

/// <summary>
/// 宿主注册的平台引擎（如 Linux 的 espeak-ng）没有专属设置模型时使用的通用设置：
/// 界面按引擎能力显示连接、声音列表、语速和音高；本机引擎在切换到该页时读取声音列表。
/// </summary>
public sealed partial class GenericEngineSettingsViewModel : EngineSettingsViewModel
{
    /// <summary>沿用宿主配置与生产服务。</summary>
    public GenericEngineSettingsViewModel(string providerId, QuestVoiceSettingsViewModel host, QuestVoiceSessionFactory factory, QuestVoiceService service)
        : base(providerId, host, factory, service) { }

    /// <summary>本机预设声音（不需要连接服务）在进入页面时直接读取。</summary>
    protected override bool RefreshVoicesOnActivate => Provider.Capabilities.PresetVoices && !Provider.Capabilities.ServerConnection;
}
