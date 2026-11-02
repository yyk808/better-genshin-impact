namespace BetterGenshinImpact.Avalonia.Services;

/// <summary>
/// 遮罩随游戏焦点显示 / 隐藏，判断规则与 WPF MaskWindowHost.DecideVisibleLocked 相同（按优先级，命中即停）：
/// 截图器未运行 → 隐藏；最小化 → 保持；游戏在前台 → 显示；前台属于 BetterGI 自身（主窗口、遮罩、对话框）或游戏进程、
/// 或没有前台窗口 → 保持；其他进程在前台（含另一个 BetterGI 实例）→ 隐藏。
/// Linux 版另加防抖（WPF 没有）：目标状态需稳定一段时间才应用，焦点短暂切走再回来时遮罩不闪烁
/// </summary>
public sealed class MaskFocusPolicy
{
    /// <summary>变为显示前需要稳定的时长（游戏重新获得焦点后尽快显示）</summary>
    public static TimeSpan ShowDelay { get; set; } = TimeSpan.FromMilliseconds(120);

    /// <summary>变为隐藏前需要稳定的时长</summary>
    public static TimeSpan HideDelay { get; set; } = TimeSpan.FromMilliseconds(300);

    private bool? _pending;
    private long _pendingSince;

    /// <summary>当前（已应用的）可见状态；初始为显示，由第一次判断决定</summary>
    public bool Visible { get; private set; } = true;

    /// <summary>WPF 相同的判断：返回 null 表示保持不变</summary>
    public static bool? Decide(bool capturing, bool minimized, bool active, bool foregroundOwnedByBetterGiOrGame)
    {
        if (!capturing) return false;
        if (minimized) return null;
        if (active) return true;
        if (foregroundOwnedByBetterGiOrGame) return null;
        return false;
    }

    /// <summary>
    /// 输入本次判断，返回防抖后的可见状态。<paramref name="immediate"/> 为 true 时直接应用（截图器停止等不需要防抖的情况）
    /// </summary>
    public bool Update(bool? decision, long nowMs, bool immediate = false)
    {
        if (decision is not { } wanted || wanted == Visible)
        {
            // 保持或已是目标状态：取消待应用的变化（焦点切走后又回来）
            _pending = null;
            return Visible;
        }

        if (immediate)
        {
            _pending = null;
            Visible = wanted;
            return Visible;
        }

        if (_pending != wanted)
        {
            _pending = wanted;
            _pendingSince = nowMs;
        }

        if (nowMs - _pendingSince >= (wanted ? ShowDelay : HideDelay).TotalMilliseconds)
        {
            _pending = null;
            Visible = wanted;
        }

        return Visible;
    }

    /// <summary>重置为初始状态（遮罩关闭或游戏窗口变化时）</summary>
    public void Reset()
    {
        _pending = null;
        Visible = true;
    }
}
