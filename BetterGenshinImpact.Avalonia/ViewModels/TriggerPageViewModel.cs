using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPick;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// “实时触发”页：直接绑定 Core 的 AllConfig（与 WPF 的 TriggerSettingsPageViewModel 绑定同一批配置属性），
/// 触发器每帧读取 IsEnabledByConfig，开关立即生效。名单编辑、技能 CD 等独立窗口尚未迁移
/// </summary>
public sealed partial class TriggerPageViewModel : PageViewModel
{
    [ObservableProperty] private bool _isBlacklistAutoPickMode;
    [ObservableProperty] private bool _isWhitelistAutoPickMode;

    public TriggerPageViewModel(AllConfig? config)
    {
        Config = config ?? new AllConfig();
        IsAvailable = config is not null;
        PickButtonNames = ["F", "E", "G"];
        if (!string.IsNullOrEmpty(Config.AutoPickConfig.PickKey)
            && Config.AutoPickConfig.PickKey.Length == 1
            && char.IsUpper(Config.AutoPickConfig.PickKey[0])
            && !PickButtonNames.Contains(Config.AutoPickConfig.PickKey))
        {
            PickButtonNames.Add(Config.AutoPickConfig.PickKey);
        }

        UpdateAutoPickModeVisibility();
    }

    public override string Title => "实时触发";

    public AllConfig Config { get; }

    /// <summary>配置来自正在运行的宿主（平台初始化失败时为占位配置，修改无效）</summary>
    public bool IsAvailable { get; }

    public List<string> PickButtonNames { get; }

    public string[] ClickChatOptionNames { get; } = ["优先选择第一个选项", "随机选择选项", "优先选择最后一个选项", "不选择选项"];

    [RelayCommand]
    private void SetAutoPickMode(AutoPickMode mode)
    {
        // 拾取模式是名单缓存键的一部分，自动拾取下一次拾取时按新模式重读名单（与 WPF 相同）
        Config.AutoPickConfig.Mode = mode;
        UpdateAutoPickModeVisibility();
    }

    private void UpdateAutoPickModeVisibility()
    {
        IsBlacklistAutoPickMode = Config.AutoPickConfig.Mode == AutoPickMode.Blacklist;
        IsWhitelistAutoPickMode = Config.AutoPickConfig.Mode == AutoPickMode.Whitelist;
    }
}
