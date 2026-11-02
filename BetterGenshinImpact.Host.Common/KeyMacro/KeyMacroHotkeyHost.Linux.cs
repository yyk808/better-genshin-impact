using System.Collections.Specialized;
using System.ComponentModel;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.KeyMacro;
using BetterGenshinImpact.GameTask.KeyMacro.Script;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Platform;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.KeyMacro;

/// <summary>
/// 按键宏的热键宿主（Linux，对照 WPF Service/KeyMacro/KeyMacroHotkeyHost）：按宏目录和总开关注册热键，持有每个宏的 Runner，
/// 向界面提供运行状态和注册错误。规则与 WPF 相同：
/// 全局热键经平台热键服务（X11 XGrabKey，按住只触发一次）；键鼠监听经共用的全局键鼠监听（不拦截按键，按下 / 松开都报告，
/// 鼠标侧键也可作快捷键）；按住循环固定用键鼠监听；与软件已有快捷键冲突时不注册。
/// 注册与注销都在界面线程上进行（store 的同步上下文）
/// </summary>
public sealed class KeyMacroHotkeyHost : IDisposable
{
    /// <summary>
    /// 设置变化后重建注册的防抖时间
    /// </summary>
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(200);

    private readonly KeyMacroStore _store;
    private readonly AllConfig _config;
    private readonly IPlatformServices _platform;
    private readonly GameRuntimeService _runtimeService;
    private readonly TaskStopService _taskStopService;
    private readonly ILogger<KeyMacroHotkeyHost> _logger;
    private readonly SynchronizationContext _uiContext;
    private readonly object _entriesLock = new();
    private readonly Dictionary<KeyMacroDefinition, Entry> _entries = [];
    private readonly Timer _refreshTimer;
    private GlobalInputHotkeyService? _monitorHotkeys;
    private IDisposable? _stopRegistration;
    private bool _initialized;
    private bool _shutdown;

    /// <summary>
    /// 注册状态或运行状态变化，在界面线程上触发
    /// </summary>
    public event EventHandler? StatusChanged;

    /// <summary>
    /// 当前实例是否响应快捷键（Linux 没有云原神网页版实例，总是响应）
    /// </summary>
    public bool IsHotkeyAvailable => true;

    /// <summary>
    /// 创建热键宿主
    /// </summary>
    public KeyMacroHotkeyHost(KeyMacroStore store, AllConfig config, IPlatformServices platform, GameRuntimeService runtimeService,
        TaskStopService taskStopService, SynchronizationContext uiContext, ILogger<KeyMacroHotkeyHost> logger)
    {
        _store = store;
        _config = config;
        _platform = platform;
        _runtimeService = runtimeService;
        _taskStopService = taskStopService;
        _uiContext = uiContext;
        _logger = logger;
        _refreshTimer = new Timer(_ => _uiContext.Post(_ => Refresh(), null));
    }

    /// <summary>
    /// 按快捷键生成解析选项（与 WPF KeyMacroRunner.CreateParseOptions 相同）：单键快捷键不能出现在脚本里。
    /// 组合键（只有全局热键可用）单独按主键不会触发，不做这项检查
    /// </summary>
    public static KeyMacroParseOptions CreateParseOptions(KeyMacroTriggerMode mode, HotkeyText hotKey)
    {
        if (hotKey.IsMouseButton)
        {
            return new KeyMacroParseOptions(mode, null,
                hotKey.MouseButton == GameKey.VK_XBUTTON1 ? KeyMacroMouseButton.X1 : KeyMacroMouseButton.X2);
        }

        if (hotKey.Key == GameKey.None || hotKey.Modifiers != KeyModifiers.None)
        {
            return new KeyMacroParseOptions(mode);
        }

        return new KeyMacroParseOptions(mode, hotKey.Key);
    }

    /// <summary>
    /// 按宏当前的快捷键生成解析选项（快捷键无效时按没有快捷键处理）
    /// </summary>
    public static KeyMacroParseOptions CreateParseOptions(KeyMacroDefinition definition)
    {
        try
        {
            return CreateParseOptions(definition.TriggerMode, HotkeyText.Parse(definition.Hotkey));
        }
        catch (FormatException)
        {
            return new KeyMacroParseOptions(definition.TriggerMode);
        }
    }

    /// <summary>
    /// 在界面线程上载入宏目录、订阅变化并注册热键
    /// </summary>
    public void Start()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _store.EnsureLoaded();
        _store.Macros.CollectionChanged += OnMacrosChanged;
        foreach (var definition in _store.Macros)
        {
            definition.PropertyChanged += OnDefinitionPropertyChanged;
        }

        _config.KeyMacroConfig.PropertyChanged += OnConfigPropertyChanged;
        _stopRegistration = _taskStopService.Register(reason => StopAll(reason switch
        {
            TaskStopReason.RuntimeStopped => "截图器停止",
            TaskStopReason.ApplicationShutdown => "程序退出",
            _ => "停止任务"
        }));
        Refresh();
    }

    /// <summary>
    /// 宏是否正在运行
    /// </summary>
    public bool IsRunning(KeyMacroDefinition definition)
    {
        lock (_entriesLock)
        {
            return _entries.TryGetValue(definition, out var entry) && entry.Runner?.IsRunning == true;
        }
    }

    /// <summary>
    /// 宏的快捷键是否已注册
    /// </summary>
    public bool IsRegistered(KeyMacroDefinition definition)
    {
        lock (_entriesLock)
        {
            return _entries.TryGetValue(definition, out var entry) && entry.Registration != null;
        }
    }

    /// <summary>
    /// 宏的快捷键注册失败原因，没有错误时返回 null
    /// </summary>
    public string? GetError(KeyMacroDefinition definition)
    {
        lock (_entriesLock)
        {
            return _entries.TryGetValue(definition, out var entry) ? entry.Error : null;
        }
    }

    /// <summary>
    /// 停止全部正在运行的按键宏，可在任意线程调用
    /// </summary>
    public void StopAll(string reason)
    {
        List<KeyMacroRunner> runners;
        lock (_entriesLock)
        {
            runners = _entries.Values.Select(e => e.Runner).OfType<KeyMacroRunner>().ToList();
        }

        foreach (var runner in runners)
        {
            runner.Stop(reason);
        }
    }

    /// <summary>
    /// 立即按当前设置重建注册（界面线程；自测与“立即生效”用，平时由防抖计时触发）
    /// </summary>
    public void Refresh()
    {
        if (_shutdown || !_initialized)
        {
            return;
        }

        var definitions = _store.Macros.ToList();
        lock (_entriesLock)
        {
            foreach (var stale in _entries.Keys.Except(definitions).ToList())
            {
                _entries[stale].Dispose("宏已删除");
                _entries.Remove(stale);
            }

            foreach (var definition in definitions)
            {
                var signature = GetSignature(definition);
                if (_entries.TryGetValue(definition, out var existing))
                {
                    if (existing.Signature == signature)
                    {
                        continue;
                    }

                    existing.Dispose("设置已变化");
                }

                _entries[definition] = CreateEntry(definition, signature);
            }
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private string GetSignature(KeyMacroDefinition definition)
    {
        return string.Join("|", _config.KeyMacroConfig.Enabled, definition.Name, definition.Enabled, definition.Hotkey,
            definition.EffectiveHotkeyType, definition.TriggerMode);
    }

    private IHotkeyService MonitorHotkeys => _monitorHotkeys ??= new GlobalInputHotkeyService(SharedGlobalInputHook.Instance);

    /// <summary>
    /// 为一个宏创建 Runner 并注册热键。不满足条件时只记录原因（提示文字与 WPF 相同）
    /// </summary>
    private Entry CreateEntry(KeyMacroDefinition definition, string signature)
    {
        var entry = new Entry(signature);
        if (!_config.KeyMacroConfig.Enabled || !definition.Enabled || string.IsNullOrEmpty(definition.Hotkey))
        {
            return entry;
        }

        HotkeyText hotKey;
        try
        {
            hotKey = HotkeyText.Parse(definition.Hotkey);
        }
        catch (FormatException)
        {
            entry.Error = "快捷键格式无效";
            return entry;
        }

        if (hotKey.IsEmpty)
        {
            return entry;
        }

        var type = definition.EffectiveHotkeyType;
        if (type == HotKeyTypeEnum.KeyboardMonitor && hotKey.Modifiers != KeyModifiers.None)
        {
            entry.Error = "键鼠监听不支持组合键";
            return entry;
        }

        if (type == HotKeyTypeEnum.GlobalRegister && hotKey.IsMouseButton)
        {
            entry.Error = "全局热键不支持鼠标侧键";
            return entry;
        }

        if (ConflictsWithHotKeyConfig(hotKey, type, _config.HotKeyConfig))
        {
            entry.Error = "与软件已有的快捷键冲突，请在快捷键页查看";
            return entry;
        }

        var runner = new KeyMacroRunner(definition, CreateParseOptions(definition.TriggerMode, hotKey), IsGameForeground, GetGameArea);
        try
        {
            if (type == HotKeyTypeEnum.GlobalRegister)
            {
                if (hotKey.ToGesture() is not { } gesture)
                {
                    entry.Error = "快捷键格式无效";
                    return entry;
                }

                // X 服务器（XKB 默认键位）把 Ctrl + Alt + F1~F12 当作切换虚拟终端并吞掉，热键永远收不到
                if (gesture.Key is >= GameKey.VK_F1 and <= GameKey.VK_F12
                    && gesture.Modifiers.HasFlag(KeyModifiers.Control) && gesture.Modifiers.HasFlag(KeyModifiers.Alt))
                {
                    entry.Error = "Linux 上 Ctrl + Alt + F1~F12 用于切换虚拟终端，请换用其他组合";
                    return entry;
                }

                entry.Registration = _platform.Hotkeys.Register(gesture, _ => runner.OnPressed(), noRepeat: true);
            }
            else if (hotKey.IsMouseButton)
            {
                entry.Registration = new MouseButtonMonitor(SharedGlobalInputHook.Instance, hotKey.MouseButton, runner.OnKeyDown, runner.OnKeyUp);
            }
            else
            {
                entry.Registration = ((IHotkeyHoldService)MonitorHotkeys).RegisterHold(new HotkeyGesture(hotKey.Key),
                    _ => runner.OnKeyDown(), _ => runner.OnKeyUp());
            }
        }
        catch (Exception e)
        {
            entry.Error = "快捷键注册失败：" + e.Message;
            _logger.LogWarning("按键宏「{Name}」的快捷键 {Hotkey} 注册失败：{Error}", definition.Name, definition.Hotkey, e.Message);
            return entry;
        }

        runner.RunningChanged += OnRunnerRunningChanged;
        entry.Runner = runner;
        _logger.LogInformation("按键宏「{Name}」已注册快捷键 {Hotkey}（{Type}）", definition.Name, definition.Hotkey, type.ToChineseName());
        return entry;
    }

    /// <summary>
    /// 与 WPF HotKeySettingModel.ConflictsWithHotKeyConfig 相同：按快捷键配置预先检查冲突
    /// </summary>
    public static bool ConflictsWithHotKeyConfig(HotkeyText hotKey, HotKeyTypeEnum hotKeyType, HotKeyConfig config)
    {
        var configType = config.GetType();
        foreach (var property in configType.GetProperties().Where(property => property.PropertyType == typeof(string)
                     && property.Name.EndsWith("Hotkey", StringComparison.Ordinal)))
        {
            if (!Enum.TryParse<HotKeyTypeEnum>(configType.GetProperty(property.Name + "Type")?.GetValue(config) as string, out var type))
            {
                continue;
            }

            try
            {
                if (AreConflicting(hotKey, hotKeyType, HotkeyText.Parse(property.GetValue(config) as string ?? ""), type))
                {
                    return true;
                }
            }
            catch (FormatException)
            {
                // 原设置中的非法键值不是一个可注册热键，不因它阻断其他有效配置
            }
        }

        return false;
    }

    /// <summary>
    /// 与 WPF HotKeySettingModel.AreConflicting 相同
    /// </summary>
    public static bool AreConflicting(HotkeyText first, HotKeyTypeEnum firstType, HotkeyText second, HotKeyTypeEnum secondType)
    {
        if (first.IsEmpty || second.IsEmpty) return false;
        if (first.MouseButton != GameKey.None || second.MouseButton != GameKey.None)
            return first.MouseButton == second.MouseButton;
        return first.Key == second.Key && (first.Modifiers == second.Modifiers
                                           || firstType == HotKeyTypeEnum.KeyboardMonitor || secondType == HotKeyTypeEnum.KeyboardMonitor);
    }

    /// <summary>
    /// 游戏是否在前台：有运行环境时看它的窗口；没有时按前台进程名判断（与 WPF SystemControl.IsGameForeground 相同）
    /// </summary>
    private bool IsGameForeground()
    {
        if (_runtimeService.Current?.Window is { } window)
        {
            return window.IsForeground;
        }

        var active = GameSystem.GetActiveProcessName();
        return !string.IsNullOrEmpty(active) && GameSystem.GetGenshinGameProcessNameList(null)
            .Any(name => string.Equals(name, active, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 游戏画面在屏幕上的区域，供绝对坐标指令换算：有运行环境时用它的截图区域；截图器未启动时取不到
    /// </summary>
    private PixelRect? GetGameArea()
    {
        return _runtimeService.Current is { } runtime ? runtime.SystemInfo.CaptureAreaRect : null;
    }

    private void ScheduleRefresh()
    {
        if (_shutdown)
        {
            return;
        }

        _refreshTimer.Change(RefreshDelay, Timeout.InfiniteTimeSpan);
    }

    private void OnMacrosChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var removed in e.OldItems?.OfType<KeyMacroDefinition>() ?? [])
        {
            removed.PropertyChanged -= OnDefinitionPropertyChanged;
        }

        foreach (var added in e.NewItems?.OfType<KeyMacroDefinition>() ?? [])
        {
            added.PropertyChanged -= OnDefinitionPropertyChanged;
            added.PropertyChanged += OnDefinitionPropertyChanged;
        }

        ScheduleRefresh();
    }

    private void OnDefinitionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 脚本变化不需要重新注册，Runner 在每次开始时读取最新脚本
        if (e.PropertyName != nameof(KeyMacroDefinition.Script))
        {
            ScheduleRefresh();
        }
    }

    private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(KeyMacroConfig.Enabled))
        {
            ScheduleRefresh();
        }
    }

    private void OnRunnerRunningChanged(object? sender, EventArgs e)
    {
        _uiContext.Post(_ => StatusChanged?.Invoke(this, EventArgs.Empty), null);
    }

    /// <summary>
    /// 程序退出：停止全部宏、注销热键并写入未保存的修改（界面线程）
    /// </summary>
    public void Dispose()
    {
        if (_shutdown)
        {
            return;
        }

        _shutdown = true;
        _refreshTimer.Dispose();
        _stopRegistration?.Dispose();
        _config.KeyMacroConfig.PropertyChanged -= OnConfigPropertyChanged;
        lock (_entriesLock)
        {
            foreach (var entry in _entries.Values)
            {
                entry.Dispose("程序退出");
            }

            _entries.Clear();
        }

        _monitorHotkeys?.Dispose();
        _store.Flush();
    }

    /// <summary>
    /// 鼠标侧键作快捷键（键鼠监听）：按下 / 松开；本进程注入的事件不算
    /// </summary>
    private sealed class MouseButtonMonitor : IDisposable
    {
        private readonly IGlobalInputHook _hook;
        private readonly GameKey _button;
        private readonly Action _down;
        private readonly Action _up;

        public MouseButtonMonitor(IGlobalInputHook hook, GameKey button, Action down, Action up)
        {
            _hook = hook;
            _button = button;
            _down = down;
            _up = up;
            _hook.MouseDown += OnDown;
            _hook.MouseUp += OnUp;
        }

        private void OnDown(object? sender, GlobalMouseEventArgs e)
        {
            if (e.Button == _button && !e.IsSelfInjected) ThreadPool.QueueUserWorkItem(_ => _down());
        }

        private void OnUp(object? sender, GlobalMouseEventArgs e)
        {
            if (e.Button == _button && !e.IsSelfInjected) ThreadPool.QueueUserWorkItem(_ => _up());
        }

        public void Dispose()
        {
            _hook.MouseDown -= OnDown;
            _hook.MouseUp -= OnUp;
        }
    }

    /// <summary>
    /// 一个宏的注册状态
    /// </summary>
    private sealed class Entry(string signature)
    {
        public string Signature { get; } = signature;

        public IDisposable? Registration { get; set; }

        public KeyMacroRunner? Runner { get; set; }

        public string? Error { get; set; }

        public void Dispose(string reason)
        {
            Runner?.Stop(reason);
            Registration?.Dispose();
            Registration = null;
        }
    }
}
