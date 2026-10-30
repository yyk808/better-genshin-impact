using System.Collections.ObjectModel;
using System.Reflection;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.OneDragon;
using BetterGenshinImpact.Host.Common;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Service;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>快捷键页的一行：WPF HotKeyPage 中的名称、所属分组与 HotKeyConfig 中的属性</summary>
public sealed partial class HotkeyItemViewModel : ObservableObject
{
    private readonly HotKeyConfig _config;
    private readonly PropertyInfo _value;
    private readonly PropertyInfo? _type;

    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private string _status = string.Empty;

    public HotkeyItemViewModel(HotKeyConfig config, string group, string name, string property, bool supported)
    {
        _config = config;
        Group = group;
        Name = name;
        Property = property;
        IsSupported = supported;
        _value = typeof(HotKeyConfig).GetProperty(property) ?? throw new ArgumentException(property);
        _type = typeof(HotKeyConfig).GetProperty(property + "Type");
    }

    public string Group { get; }

    public string Name { get; }

    public string Property { get; }

    /// <summary>Linux 宿主已实现该快捷键的动作</summary>
    public bool IsSupported { get; }

    public string Value
    {
        get => _value.GetValue(_config) as string ?? string.Empty;
        set
        {
            _value.SetValue(_config, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayValue));
        }
    }

    public string DisplayValue => IsRecording ? "请按下组合键…（Esc 取消，Backspace 清除）" : string.IsNullOrEmpty(Value) ? "< None >" : Value;

    public string TypeText => _type?.GetValue(_config) is string t && Enum.TryParse<HotKeyTypeEnum>(t, out var e) ? e.ToChineseName() : "";

    partial void OnIsRecordingChanged(bool value) => OnPropertyChanged(nameof(DisplayValue));
}

/// <summary>
/// “设置”页（对应 WPF 的 CommonSettingsPage 常用项 + HotKeyPage + NotificationSettingsPage + 关于）：
/// 直接绑定 Core 的 AllConfig（配置文件与 Windows 版共用，修改即保存）。
/// 快捷键在 Linux 上经平台的 IHotkeyService（X11 XGrabKey）注册，先实现了常用的几个动作，其余只能查看和修改绑定
/// </summary>
public sealed partial class SettingsPageViewModel : PageViewModel, IDisposable
{
    private readonly GameHost? _host;
    private readonly ILogger _log;
    private readonly Func<Task>? _toggleCapture;
    private readonly Func<Task>? _toggleOneDragon;
    private readonly List<IDisposable> _registrations = [];

    [ObservableProperty] private int _selectedTab;
    [ObservableProperty] private string _hotkeyStatus = string.Empty;

    /// <summary>WPF 快捷键页的条目（分组、名称、配置属性），顺序相同；“内部测试”分组未列出</summary>
    private static readonly (string Group, string Name, string Property)[] HotkeyTable =
    [
        ("", "启动停止 BetterGI", "BgiEnabledHotkey"),
        ("系统控制", "停止当前脚本/独立任务", "CancelTaskHotkey"),
        ("系统控制", "暂停当前脚本/独立任务", "SuspendHotkey"),
        ("系统控制", "游戏截图", "TakeScreenshotHotkey"),
        ("系统控制", "日志与状态窗口展示开关", "LogBoxDisplayHotkey"),
        ("系统控制", "遮罩指标栏展示开关", "OverlayMetricsDisplayHotkey"),
        ("实时任务", "自动拾取开关", "AutoPickEnabledHotkey"),
        ("实时任务", "自动剧情开关", "AutoSkipEnabledHotkey"),
        ("实时任务", "自动邀约开关", "AutoSkipHangoutEnabledHotkey"),
        ("实时任务", "自动钓鱼开关", "AutoFishingEnabledHotkey"),
        ("实时任务", "快速传送开关", "QuickTeleportEnabledHotkey"),
        ("实时任务", "冷却提示开关", "SkillCdEnabledHotkey"),
        ("实时任务", "手动触发快速传送触发快捷键（按住起效）", "QuickTeleportTickHotkey"),
        ("实时任务", "地图遮罩开关", "MapMaskEnabledHotkey"),
        ("独立任务", "启动/停止一条龙", "OnedragonHotkey"),
        ("独立任务", "启动/停止自动七圣召唤", "AutoGeniusInvokationHotkey"),
        ("独立任务", "启动/停止自动伐木", "AutoWoodHotkey"),
        ("独立任务", "启动/停止自动战斗", "AutoFightHotkey"),
        ("独立任务", "启动/停止自动秘境", "AutoDomainHotkey"),
        ("独立任务", "启动/停止自动音游", "AutoMusicGameHotkey"),
        ("独立任务", "启动/停止自动钓鱼", "AutoFishingGameHotkey"),
        ("独立任务", "启动/停止自动烹饪", "AutoCookGameHotkey"),
        ("操控辅助", "长按旋转视角 - 那维莱特转圈", "TurnAroundHotkey"),
        ("操控辅助", "按下快速强化圣遗物", "EnhanceArtifactHotkey"),
        ("操控辅助", "按下快速购买商店物品", "QuickBuyHotkey"),
        ("操控辅助", "一键领取奖励", "OneKeyClaimRewardHotkey"),
        ("操控辅助", "按下快速进出尘歌壶", "QuickSereniteaPotHotkey"),
        ("操控辅助", "快捷点击原神内确认按钮", "ClickGenshinConfirmButtonHotkey"),
        ("操控辅助", "快捷点击原神内取消按钮", "ClickGenshinCancelButtonHotkey"),
        ("操控辅助", "一键战斗宏快捷键", "OneKeyFightHotkey"),
        ("开发者", "启动/停止键鼠录制", "KeyMouseMacroRecordHotkey"),
        ("开发者", "（开发）模板素材制作", "RecognitionTemplateEditorHotkey"),
        ("开发者", "（开发）获取当前大地图中心点位置", "RecBigMapPosHotkey"),
        ("开发者", "启动/停止路径记录器", "PathRecorderHotkey"),
        ("开发者", "添加路径点", "AddWaypointHotkey"),
    ];

    public SettingsPageViewModel(GameHost? host, ILogger log, Func<Task>? toggleCapture = null, Func<Task>? toggleOneDragon = null)
    {
        _host = host;
        _log = log;
        _toggleCapture = toggleCapture;
        _toggleOneDragon = toggleOneDragon;
        Config = host?.Config ?? new AllConfig();
        IsAvailable = host is not null;
        foreach (var (group, name, property) in HotkeyTable)
        {
            if (typeof(HotKeyConfig).GetProperty(property) is null)
            {
                continue;
            }

            Hotkeys.Add(new HotkeyItemViewModel(Config.HotKeyConfig, group, name, property, HotkeyActions.ContainsKey(property)));
        }

        RegisterHotkeys();
    }

    public override string Title => "设置";

    public AllConfig Config { get; }

    public bool IsAvailable { get; }

    public ObservableCollection<HotkeyItemViewModel> Hotkeys { get; } = [];

    public List<string> GameLanguages { get; } = ["zh-Hans", "zh-Hant", "en", "fr", "ja", "ko", "ru", "es", "de", "pt", "it", "tr", "th", "vi", "id"];

    public List<string> MapMatchingMethods { get; } = ["SIFT", "TemplateMatch"];

    public List<TimeSpan> ServerTimeZones { get; } = [TimeSpan.FromHours(8), TimeSpan.FromHours(1), TimeSpan.FromHours(-5)];

    public string Version => Global.Version;

    public string AssetsDirectory => Global.StartUpPath;

    public string ConfigFile => Global.Absolute(@"User\config.json");

    public string Runtime => $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} · {System.Runtime.InteropServices.RuntimeInformation.OSDescription}";

    public string DisplayName => _host?.Options.Display ?? Environment.GetEnvironmentVariable("DISPLAY") ?? "";

    // —— 快捷键 ——

    /// <summary>Linux 宿主已实现动作的快捷键（与 WPF HotKeyPageViewModel 中对应条目的动作相同）</summary>
    private Dictionary<string, Action> HotkeyActions => new()
    {
        ["BgiEnabledHotkey"] = () => UiHost.Dispatcher.Post(() => _ = _toggleCapture?.Invoke()),
        ["CancelTaskHotkey"] = () =>
        {
            _log.Information("检测到您配置的停止快捷键{Key}按下，停止当前执行任务", Config.HotKeyConfig.CancelTaskHotkey);
            _host?.Services.GetService<TaskStopService>()?.StopAll(TaskStopReason.UserRequested);
        },
        ["SuspendHotkey"] = () => RunnerContext.Instance.IsSuspend = !RunnerContext.Instance.IsSuspend,
        ["AutoPickEnabledHotkey"] = () => UiHost.Dispatcher.Post(() =>
        {
            Config.AutoPickConfig.Enabled = !Config.AutoPickConfig.Enabled;
            _log.Information("切换{Name}状态为[{Enabled}]", "自动拾取", Config.AutoPickConfig.Enabled ? "开" : "关");
        }),
        ["AutoSkipEnabledHotkey"] = () => UiHost.Dispatcher.Post(() =>
        {
            Config.AutoSkipConfig.Enabled = !Config.AutoSkipConfig.Enabled;
            _log.Information("切换{Name}状态为[{Enabled}]", "自动剧情", Config.AutoSkipConfig.Enabled ? "开" : "关");
        }),
        ["OnedragonHotkey"] = () => UiHost.Dispatcher.Post(() => _ = _toggleOneDragon?.Invoke()),
    };

    /// <summary>按当前配置（重新）注册已实现动作的快捷键</summary>
    [RelayCommand]
    public void RegisterHotkeys()
    {
        foreach (var r in _registrations)
        {
            r.Dispose();
        }

        _registrations.Clear();
        if (_host is null)
        {
            HotkeyStatus = "平台服务未初始化，快捷键不可用";
            return;
        }

        var actions = HotkeyActions;
        var ok = new List<string>();
        var failed = new List<string>();
        foreach (var item in Hotkeys)
        {
            item.Status = string.Empty;
            if (!actions.TryGetValue(item.Property, out var action) || string.IsNullOrWhiteSpace(item.Value))
            {
                continue;
            }

            try
            {
                var gesture = HotkeyGesture.Parse(item.Value);
                _registrations.Add(_host.Platform.Hotkeys.Register(gesture, _ => action(), noRepeat: true));
                item.Status = "已注册";
                ok.Add($"{item.Name}={item.Value}");
            }
            catch (Exception e)
            {
                item.Status = $"注册失败：{e.Message}";
                failed.Add($"{item.Name}={item.Value}（{e.Message}）");
            }
        }

        HotkeyStatus = $"已注册 {ok.Count} 个快捷键" + (failed.Count > 0 ? $"，{failed.Count} 个失败" : "");
        _log.Information("快捷键：{Status} {List}", HotkeyStatus, string.Join("；", ok.Concat(failed)));
    }

    [RelayCommand]
    private void StartRecord(HotkeyItemViewModel? item)
    {
        foreach (var h in Hotkeys)
        {
            h.IsRecording = ReferenceEquals(h, item) && !h.IsRecording;
        }
    }

    /// <summary>录制中的快捷键收到按键（由页面的 KeyDown 调用）；键名与 WPF 的 HotKey.ToString 写法相同（如 “Ctrl + Shift + F11”）</summary>
    public bool OnRecordKey(string keyName, bool ctrl, bool shift, bool alt, bool win)
    {
        var item = Hotkeys.FirstOrDefault(h => h.IsRecording);
        if (item is null)
        {
            return false;
        }

        if (keyName is "LeftCtrl" or "RightCtrl" or "LeftShift" or "RightShift" or "LeftAlt" or "RightAlt" or "LWin" or "RWin")
        {
            return true;
        }

        item.IsRecording = false;
        if (keyName == "Escape")
        {
            return true;
        }

        if (keyName == "Back")
        {
            item.Value = string.Empty;
        }
        else
        {
            var text = (ctrl ? "Ctrl + " : "") + (shift ? "Shift + " : "") + (alt ? "Alt + " : "") + (win ? "Win + " : "") + keyName;
            item.Value = text;
        }

        if (item.IsSupported)
        {
            RegisterHotkeys();
        }

        return true;
    }

    [RelayCommand]
    private void ClearHotkey(HotkeyItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        item.Value = string.Empty;
        if (item.IsSupported)
        {
            RegisterHotkeys();
        }
    }

    public void Dispose()
    {
        foreach (var r in _registrations)
        {
            r.Dispose();
        }

        _registrations.Clear();
    }
}

/// <summary>设置页的绑定转换</summary>
public static class SettingsConverters
{
    public static readonly global::Avalonia.Data.Converters.IValueConverter Supported =
        new global::Avalonia.Data.Converters.FuncValueConverter<bool, string>(s => s ? "Linux 已实现" : "仅保存绑定");
}
