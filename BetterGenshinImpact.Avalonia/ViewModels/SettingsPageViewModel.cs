using System.Collections.ObjectModel;
using System.Reflection;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common.Hotkeys;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask.Macro;
using BetterGenshinImpact.GameTask.OneDragon;
using BetterGenshinImpact.GameTask.QuickBuy;
using BetterGenshinImpact.GameTask.QuickClaimReward;
using BetterGenshinImpact.GameTask.QuickSereniteaPot;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Host.Common;
using BetterGenshinImpact.Host.Common.Notification;
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

    /// <summary>长按类快捷键（固定为键鼠监听）</summary>
    public bool IsHold => HotkeyActionCatalog.HoldHotkeys.Contains(Property);

    /// <summary>快捷键方式（HotKeyConfig 中的 xxxType），缺省为全局热键</summary>
    public HotKeyTypeEnum Type
    {
        get => IsHold ? HotKeyTypeEnum.KeyboardMonitor
            : _type?.GetValue(_config) is string t && Enum.TryParse<HotKeyTypeEnum>(t, out var e) ? e : HotKeyTypeEnum.GlobalRegister;
        set
        {
            _type?.SetValue(_config, value.ToString());
            OnPropertyChanged();
            OnPropertyChanged(nameof(TypeText));
        }
    }

    public string TypeText => Type.ToChineseName();

    partial void OnIsRecordingChanged(bool value) => OnPropertyChanged(nameof(DisplayValue));
}

/// <summary>
/// “设置”页（对应 WPF 的 CommonSettingsPage 常用项 + HotKeyPage + NotificationSettingsPage + 关于）：
/// 直接绑定 Core 的 AllConfig（配置文件与 Windows 版共用，修改即保存）。
/// 快捷键在 Linux 上经平台的 IHotkeyService（X11 XGrabKey）注册，动作与 WPF 相同（键鼠录制、模板素材制作、路径记录器除外）
/// </summary>
public sealed partial class SettingsPageViewModel : PageViewModel, IDisposable
{
    private readonly GameHost? _host;
    private readonly ILogger _log;
    private readonly Func<Task>? _toggleCapture;
    private readonly Func<string, Task>? _toggleSoloTask;
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

    public SettingsPageViewModel(GameHost? host, ILogger log, Func<Task>? toggleCapture = null, Func<string, Task>? toggleSoloTask = null)
    {
        _host = host;
        _log = log;
        _toggleCapture = toggleCapture;
        _toggleSoloTask = toggleSoloTask;
        // 快速传送“按住时生效”：Windows 由键鼠钩子提供按住状态，这里由长按快捷键提供
        HotkeyHoldState.Query ??= hotkey => _holdRunners.TryGetValue(hotkey, out var r) && r.IsPressed;
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

    /// <summary>Linux 宿主设置（日志文件等）；平台初始化失败时为空</summary>
    public BetterGenshinImpact.Host.Common.Config.LinuxHostConfig? LinuxConfig => _host?.LinuxConfig;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void OpenLogFile() => TryOpen(Services.LogFiles.OpenLatest);

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void OpenLogFolder() => TryOpen(Services.LogFiles.OpenFolder);

    private void TryOpen(Action open)
    {
        try
        {
            open();
        }
        catch (Exception e)
        {
            _log.Warning("打开日志失败：{Message}", e.Message);
        }
    }

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

    /// <summary>一个快捷键的动作：普通快捷键只有 Press；长按类（见 HotkeyActionCatalog.HoldHotkeys）按住期间反复 Press，或分别处理 Down / Up</summary>
    private sealed record HotkeyBinding(Action? Press, Action? Down = null, Action? Up = null, bool Hold = false);

    /// <summary>
    /// Linux 宿主的快捷键动作，与 WPF HotKeyPageViewModel 中对应条目相同；与界面无关的部分在 Core 的 HotkeyActionCatalog 中。
    /// 模板素材制作打开 Linux 版框选窗口；路径记录器与添加路径点使用 Core 的 PathRecordSession（不打开 WPF 网页编辑器，直接保存文件）
    /// </summary>
    private Dictionary<string, HotkeyBinding> HotkeyActions
    {
        get
        {
            var map = new Dictionary<string, HotkeyBinding>
            {
                ["BgiEnabledHotkey"] = new(() => UiHost.Dispatcher.Post(() => _ = _toggleCapture?.Invoke())),
                ["CancelTaskHotkey"] = new(() =>
                {
                    _log.Information("检测到您配置的停止快捷键{Key}按下，停止当前执行任务", Config.HotKeyConfig.CancelTaskHotkey);
                    _host?.Services.GetService<TaskStopService>()?.StopAll(TaskStopReason.UserRequested);
                }),
                ["SuspendHotkey"] = new(() => RunnerContext.Instance.IsSuspend = !RunnerContext.Instance.IsSuspend),
                ["TakeScreenshotHotkey"] = new(() =>
                {
                    if (_host?.RuntimeService.Current is { } runtime)
                    {
                        Screenshots?.SaveCurrentFrame(runtime);
                    }
                    else
                    {
                        _log.Information("截图失败，未获取到图像");
                    }
                }),
                ["LogBoxDisplayHotkey"] = new(() => UiHost.Dispatcher.Post(() => HotkeyActionCatalog.ToggleLogBox(Config.MaskWindowConfig))),
                ["OverlayMetricsDisplayHotkey"] = new(() => UiHost.Dispatcher.Post(() => HotkeyActionCatalog.ToggleOverlayMetrics(Config.MaskWindowConfig))),
                ["QuickTeleportTickHotkey"] = new(() => Thread.Sleep(100), Hold: true),
                ["TurnAroundHotkey"] = new(TurnAroundMacro.Done, Hold: true),
                ["EnhanceArtifactHotkey"] = new(() => WithRuntime(QuickEnhanceArtifactMacro.Done), Hold: true),
                ["QuickBuyHotkey"] = new(() => WithRuntime(QuickBuyTask.Done), Hold: true),
                ["OneKeyClaimRewardHotkey"] = new(null, () => WithRuntime(r => OneKeyClaimRewardTask.Instance.KeyDown(r)),
                    () => OneKeyClaimRewardTask.Instance.KeyUp(), Hold: true),
                ["QuickSereniteaPotHotkey"] = new(() => WithRuntime(QuickSereniteaPotTask.Done)),
                ["ClickGenshinConfirmButtonHotkey"] = new(() => WithRuntime(r => HotkeyActionCatalog.ClickGenshinButton(r, true)), Hold: true),
                ["ClickGenshinCancelButtonHotkey"] = new(() => WithRuntime(r => HotkeyActionCatalog.ClickGenshinButton(r, false)), Hold: true),
                ["OneKeyFightHotkey"] = new(null, () =>
                {
                    // 一键宏未启用时与 WPF 相同地静默返回
                    if (OneKeyFightTask.IsEnabled())
                    {
                        WithRuntime(r => OneKeyFightTask.Instance.KeyDown(r));
                    }
                }, () => OneKeyFightTask.Instance.KeyUp(), Hold: true),
                ["KeyMouseMacroRecordHotkey"] = new(() =>
                {
                    if (_host?.Services.GetService<KeyMouseRecordController>() is not { } recorder)
                    {
                        _log.Warning("请先启动截图器再使用键鼠录制");
                        return;
                    }

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await recorder.ToggleAsync();
                        }
                        catch (Exception e)
                        {
                            _log.Error(e, "键鼠录制失败");
                        }
                    });
                }),
                ["RecognitionTemplateEditorHotkey"] = new(() => UiHost.Dispatcher.Post(() => _ = Views.TemplateEditorWindow.OpenAsync(_host, Config.DevConfig))),
                ["PathRecorderHotkey"] = new(TogglePathRecorder),
                ["AddWaypointHotkey"] = new(() =>
                {
                    // 与 WPF 相同：只在记录中且截图器已启动时添加
                    if (_pathRecording && _host?.RuntimeService.Current is { } runtime)
                    {
                        _ = Task.Run(() => RunPathRecorder(() => _pathRecorder.AddWaypoint(runtime)));
                    }
                }),
                ["RecBigMapPosHotkey"] = new(() => WithRuntime(r =>
                {
                    try
                    {
                        var p = new TpTask(r, Config, r.Stopping).GetPositionFromBigMap(MapTypes.Teyvat.ToString());
                        _log.Information("大地图位置：{Position}", p);
                    }
                    catch (Exception e)
                    {
                        _log.Error(e.Message);
                    }
                })),
            };
            foreach (var toggle in HotkeyActionCatalog.RealtimeToggles)
            {
                map[toggle.HotkeyProperty] = new(() => UiHost.Dispatcher.Post(() => HotkeyActionCatalog.Toggle(toggle, Config, MsLogger)));
            }

            foreach (var (property, taskId) in HotkeyActionCatalog.SoloTaskHotkeys)
            {
                map[property] = new(() => UiHost.Dispatcher.Post(() => _ = _toggleSoloTask?.Invoke(taskId)));
            }

            return map;
        }
    }

    /// <summary>路径记录（与 WPF PathRecorder 共用 Core 的记录逻辑）</summary>
    private readonly BetterGenshinImpact.GameTask.AutoPathing.PathRecordSession _pathRecorder = new();

    private bool _pathRecording;

    /// <summary>路径记录器正在记录（快捷键切换）</summary>
    public bool IsPathRecording => _pathRecording;

    /// <summary>当前记录的路径点数</summary>
    public int PathRecordCount => _pathRecorder.Count;

    /// <summary>最近一次保存的路径记录文件</summary>
    public string? LastPathRecordFile { get; private set; }

    /// <summary>
    /// 启动 / 停止路径记录器（与 WPF 相同：停止时保存；启动需要截图器，未启动时不切换状态）。
    /// Linux 没有网页路径点编辑器，停止时直接保存到地图追踪目录。
    /// </summary>
    private void TogglePathRecorder()
    {
        if (_pathRecording)
        {
            RunPathRecorder(() =>
            {
                // 新环境下地图追踪目录可能还不存在（Windows 版安装时已有）
                Directory.CreateDirectory(BetterGenshinImpact.GameTask.AutoPathing.PathingPaths.PathJsonPath);
                LastPathRecordFile = _pathRecorder.SaveToFile();
            });
        }
        else
        {
            if (_host?.RuntimeService.Current is not { } runtime)
            {
                WithRuntime(_ => { });
                return;
            }

            _ = Task.Run(() => RunPathRecorder(() =>
            {
                if (_pathRecorder.Start(runtime) is { } waypoint)
                {
                    _log.Information("已创建初始路径点({X},{Y})", waypoint.X, waypoint.Y);
                }
            }));
        }

        _pathRecording = !_pathRecording;
    }

    private void RunPathRecorder(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            _log.Error(e, "路径记录失败：{Message}", e.Message);
        }
    }

    private GameScreenshotService? _screenshots;

    private GameScreenshotService? Screenshots => _screenshots ??= _host is null
        ? null
        : _host.Services.GetService<GameScreenshotService>() ?? ActivatorUtilities.CreateInstance<GameScreenshotService>(_host.Services);

    private Microsoft.Extensions.Logging.ILogger MsLogger =>
        _host?.Services.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("BetterGenshinImpact.ViewModel.Pages.HotKeyPageViewModel")
        ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    private long _lastNoRuntimeTick;

    /// <summary>截图器未启动时提示“请先启动”（长按类会反复回调，限制提示频率），与 WPF TryGetRuntime 相同</summary>
    private void WithRuntime(Action<GameRuntime> action)
    {
        if (_host?.RuntimeService.Current is { } runtime)
        {
            action(runtime);
            return;
        }

        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastNoRuntimeTick) >= 3000)
        {
            Interlocked.Exchange(ref _lastNoRuntimeTick, now);
            UiHost.Dispatcher.Post(() => UiHost.Dialogs.Toast(DialogKind.Warning, "请先启动"));
        }
    }

    private readonly Dictionary<string, HoldHotkeyRunner> _holdRunners = new(StringComparer.Ordinal);

    private GlobalInputHotkeyService? _passthroughHotkeys;
    private bool _passthroughFailed;

    /// <summary>键鼠监听方式的热键（共用的全局键鼠监听，Linux 为 XInput2 原始事件）；不可用时为 null</summary>
    private GlobalInputHotkeyService? PassthroughHotkeys
    {
        get
        {
            if (_passthroughHotkeys != null || _passthroughFailed || _host is null)
            {
                return _passthroughHotkeys;
            }

            try
            {
                var hook = SharedGlobalInputHook.Instance;
                _log.Information("快捷键：键鼠监听使用 {Hook}", hook.GetType().Name);
                _passthroughHotkeys = new GlobalInputHotkeyService(hook);
            }
            catch (Exception e)
            {
                _passthroughFailed = true;
                _log.Warning("快捷键：键鼠监听不可用（{Message}），全部改用全局热键", e.Message);
            }

            return _passthroughHotkeys;
        }
    }

    /// <summary>切换快捷键方式（全局热键 / 键鼠监听），长按类固定为键鼠监听</summary>
    [RelayCommand]
    private void ToggleHotkeyType(HotkeyItemViewModel? item)
    {
        if (item is null || item.IsHold)
        {
            return;
        }

        item.Type = item.Type == HotKeyTypeEnum.GlobalRegister ? HotKeyTypeEnum.KeyboardMonitor : HotKeyTypeEnum.GlobalRegister;
        if (item.IsSupported)
        {
            RegisterHotkeys();
        }
    }

    // —— 通知 ——

    [ObservableProperty] private string _notificationStatus = string.Empty;

    /// <summary>系统通知（freedesktop）在当前环境是否可用</summary>
    public string SystemNotificationDesc => FreedesktopNotifier.IsAvailable
        ? "通过 freedesktop 通知服务（notify-send / D-Bus）显示桌面通知，与 Windows 版“Windows 通知”共用开关"
        : "未找到 notify-send 或 gdbus，系统通知不会显示（其他通知渠道不受影响）";

    /// <summary>向当前启用的全部通知渠道发送测试通知</summary>
    [RelayCommand]
    public async Task SendTestNotificationAsync()
    {
        var service = _host?.Services.GetService<LinuxNotificationService>();
        if (service is null)
        {
            NotificationStatus = "平台服务未初始化，无法发送通知";
            return;
        }

        NotificationStatus = "发送中…";
        try
        {
            service.RefreshNotifiers();
            NotificationStatus = await Task.Run(service.SendTestAsync);
        }
        catch (Exception e)
        {
            NotificationStatus = "发送失败：" + e.Message;
        }

        _log.Information("测试通知：{Status}", NotificationStatus);
    }

    /// <summary>按当前配置（重新）注册已实现动作的快捷键</summary>
    [RelayCommand]
    public void RegisterHotkeys()
    {
        foreach (var r in _registrations)
        {
            r.Dispose();
        }

        _registrations.Clear();
        foreach (var runner in _holdRunners.Values)
        {
            runner.KeyUp();
        }

        _holdRunners.Clear();
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
                // 与 Windows 相同：长按类固定用键鼠监听；其余按配置的方式（全局热键 / 键鼠监听）。
                // 键鼠监听经 XInput2 原始事件，不抓取按键，游戏照常收到；只在游戏位于前台时触发（同 WPF KeyboardHook）
                var monitor = action.Hold || item.Type == HotKeyTypeEnum.KeyboardMonitor;
                var modeNote = "";
                if (monitor && gesture.Modifiers != KeyModifiers.None && !action.Hold)
                {
                    monitor = false;
                    modeNote = "（组合键改用全局热键）";
                }

                var passthrough = monitor ? PassthroughHotkeys : null;
                if (monitor && passthrough is null)
                {
                    modeNote = "（键鼠监听不可用，改用全局热键）";
                }

                Func<bool> foreground = () => GameSystem.IsGameForeground(_host.RuntimeService.Current?.Window);
                if (action.Hold)
                {
                    var holdService = passthrough as IHotkeyHoldService ?? _host.Platform.Hotkeys as IHotkeyHoldService
                        ?? throw new NotSupportedException("平台热键服务不支持长按");
                    var runner = new HoldHotkeyRunner
                    {
                        OnKeyPress = action.Press,
                        OnKeyDown = action.Down,
                        OnKeyUp = action.Up,
                        CanRun = foreground,
                    };
                    var name = item.Name;
                    runner.Error += e => _log.Warning("快捷键“{Name}”执行异常：{Message}", name, e.Message);
                    _holdRunners[item.Value] = runner;
                    _registrations.Add(holdService.RegisterHold(gesture, _ => runner.KeyDown(), _ => runner.KeyUp()));
                }
                else
                {
                    var press = action.Press!;
                    var service = (IHotkeyService?)passthrough ?? _host.Platform.Hotkeys;
                    var checkForeground = passthrough != null;
                    _registrations.Add(service.Register(gesture, _ =>
                    {
                        if (checkForeground && !foreground())
                        {
                            return;
                        }

                        try
                        {
                            press();
                        }
                        catch (Exception e)
                        {
                            _log.Warning("快捷键执行异常：{Message}", e.Message);
                        }
                    }, noRepeat: true));
                }

                item.Status = (passthrough != null ? "已注册：键鼠监听（不拦截）" : "已注册：全局热键（XGrabKey，游戏收不到该键）") + modeNote;
                ok.Add($"{item.Name}={item.Value}{(passthrough != null ? "[监听]" : "[抓取]")}");
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
        foreach (var runner in _holdRunners.Values)
        {
            runner.KeyUp();
        }

        _holdRunners.Clear();
        _passthroughHotkeys?.Dispose();
        _passthroughHotkeys = null;
    }
}

/// <summary>设置页的绑定转换</summary>
public static class SettingsConverters
{
    public static readonly global::Avalonia.Data.Converters.IValueConverter Supported =
        new global::Avalonia.Data.Converters.FuncValueConverter<bool, string>(s => s ? "Linux 已实现" : "仅保存绑定");
}
