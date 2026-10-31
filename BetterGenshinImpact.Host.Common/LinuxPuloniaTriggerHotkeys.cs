using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Host.Common;

/// <summary>
/// Linux 宿主的计划快捷键触发（对照 WPF PuloniaTaskTriggerHost 的热键部分）：
/// 全局热键经平台热键服务（X11 XGrabKey）注册，键鼠监听方式经共用的全局键鼠监听（XInput2，不拦截按键）。
/// 与软件已有快捷键冲突、键位不合规时不注册，错误由调度循环按计划轮询周期重试。
/// </summary>
public sealed class LinuxPuloniaTriggerHotkeys(IPlatformServices platform, IPuloniaTaskService tasks, AllConfig config,
    ILogger<LinuxPuloniaTriggerHotkeys> logger) : IPuloniaTriggerHotkeys, IDisposable
{
    private readonly object _gate = new();
    private readonly List<IDisposable> _registrations = [];
    private readonly CancellationTokenSource _lifetime = new();
    private GlobalInputHotkeyService? _monitorHotkeys;
    private string _revision = "";
    private bool _disposed;

    public string Error { get; private set; } = "";

    /// <summary>当前已注册的快捷键（界面展示与自测用）</summary>
    public IReadOnlyList<string> Registered { get; private set; } = [];

    public Task RefreshAsync(IReadOnlyList<PuloniaTaskPlan> plans, CancellationToken ct)
    {
        var bindings = plans.SelectMany(plan => plan.Triggers.Where(item => item.Enabled
            && item.Kind == PuloniaTaskTriggerKind.Hotkey).Select(trigger => (plan, trigger))).ToArray();
        var revision = string.Join("|", bindings.Select(item
            => item.plan.Id + "/" + item.trigger.Id + "/" + item.trigger.ActivatedAtUtc.UtcTicks));
        lock (_gate)
        {
            if (_disposed || revision == _revision && Error.Length == 0) return Task.CompletedTask;
            ReleaseLocked();
            var error = "";
            var registered = new List<string>();
            foreach (var (plan, trigger) in bindings)
            {
                try
                {
                    var gesture = Validate(trigger);
                    if (HasApplicationHotkeyConflict(gesture))
                        throw new InvalidOperationException("与软件已有快捷键冲突，请选择其他键。");
                    var planId = plan.Id;
                    var triggerId = trigger.Id;
                    var service = trigger.HotkeyType == HotKeyTypeEnum.KeyboardMonitor ? MonitorHotkeys : platform.Hotkeys;
                    _registrations.Add(service.Register(gesture, _ => Fire(planId, triggerId), noRepeat: true));
                    registered.Add($"{plan.Name}：{trigger.DisplayText}");
                }
                catch (Exception ex)
                {
                    error += $" 热键“{trigger.DisplayText}”注册失败（可能冲突）：{ex.Message}";
                }
            }

            Error = error;
            Registered = registered;
            _revision = revision;
            if (registered.Count > 0) logger.LogInformation("任务计划快捷键：已注册 {List}", string.Join("；", registered));
            if (error.Length > 0) logger.LogWarning("任务计划快捷键：{Error}", error.Trim());
        }

        return Task.CompletedTask;
    }

    private IHotkeyService MonitorHotkeys => _monitorHotkeys ??= new GlobalInputHotkeyService(SharedGlobalInputHook.Instance);

    /// <summary>键位规则与 WPF 相同：全局热键需组合键或功能键（F12 保留），键鼠监听只接受单键</summary>
    public static HotkeyGesture Validate(PuloniaTaskTrigger trigger)
    {
        if (!Enum.IsDefined(trigger.HotkeyType)) throw new FormatException("请选择有效的热键类型并设置快捷键。");
        HotkeyGesture gesture;
        try { gesture = HotkeyGesture.Parse(trigger.Hotkey); }
        catch (Exception) { throw new FormatException("请选择有效的热键类型并设置快捷键。"); }
        if (gesture.Key is GameKey.None or GameKey.VK_SHIFT or GameKey.VK_CONTROL or GameKey.VK_MENU
            or GameKey.VK_LSHIFT or GameKey.VK_RSHIFT or GameKey.VK_LCONTROL or GameKey.VK_RCONTROL
            or GameKey.VK_LMENU or GameKey.VK_RMENU or GameKey.VK_LWIN or GameKey.VK_RWIN)
            throw new FormatException("请设置有效的键盘键，不可只使用修饰键。");
        var isFunction = gesture.Key is >= GameKey.VK_F1 and <= GameKey.VK_F24;
        if (trigger.HotkeyType == HotKeyTypeEnum.KeyboardMonitor)
        {
            if (gesture.Modifiers != KeyModifiers.None) throw new FormatException("键鼠监听只支持键盘单键或鼠标侧键，不支持组合键。");
        }
        else if (gesture.Key == GameKey.VK_F12 || gesture.Modifiers == KeyModifiers.None && !isFunction)
        {
            throw new FormatException("全局热键支持组合键或功能键；普通键需搭配 Ctrl / Alt / Win，F12 为系统保留。");
        }

        // X 服务器（XKB 默认键位）把 Ctrl + Alt + F1~F12 当作切换虚拟终端并吞掉该按键，热键永远收不到
        if (trigger.HotkeyType != HotKeyTypeEnum.KeyboardMonitor && gesture.Key is >= GameKey.VK_F1 and <= GameKey.VK_F12
            && gesture.Modifiers.HasFlag(KeyModifiers.Control) && gesture.Modifiers.HasFlag(KeyModifiers.Alt))
            throw new FormatException("Linux 上 Ctrl + Alt + F1~F12 用于切换虚拟终端，请换用其他组合（如 Ctrl + Shift + F8）。");

        return gesture;
    }

    /// <summary>已有软件快捷键优先（与 WPF 相同按配置检查，即使快捷键页尚未注册）</summary>
    private bool HasApplicationHotkeyConflict(HotkeyGesture gesture)
    {
        var hotkeys = config.HotKeyConfig;
        foreach (var property in hotkeys.GetType().GetProperties().Where(p => p.PropertyType == typeof(string)
                     && p.Name.EndsWith("Hotkey", StringComparison.Ordinal)))
        {
            if (property.GetValue(hotkeys) is not string { Length: > 0 } value) continue;
            try
            {
                if (HotkeyGesture.Parse(value) == gesture) return true;
            }
            catch (Exception)
            {
                // 原设置中的非法键值不阻断其他配置
            }
        }

        return false;
    }

    private void Fire(string planId, string triggerId)
    {
        if (_lifetime.IsCancellationRequested) return;
        logger.LogInformation("任务计划快捷键触发：{Plan}/{Trigger}", planId, triggerId);
        _ = Task.Run(async () =>
        {
            try { await tasks.FireHotkeyAsync(planId, triggerId, _lifetime.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogError(ex, "任务计划快捷键触发失败"); }
        });
    }

    private void ReleaseLocked()
    {
        foreach (var registration in _registrations)
        {
            try { registration.Dispose(); }
            catch (Exception ex) { logger.LogWarning(ex, "释放任务计划快捷键失败"); }
        }

        _registrations.Clear();
        Registered = [];
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            ReleaseLocked();
        }
    }
}
