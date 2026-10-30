using BetterGenshinImpact.Platform;

namespace BetterGenshinImpact.Core.Mask;

/// <summary>
/// 游戏窗口的状态。描述的是游戏窗口，不是遮罩窗口，所以不加 MaskWindow 前缀
/// </summary>
/// <param name="IsCapturing">截图器是否在运行</param>
/// <param name="IsActive">游戏窗口是否在前台</param>
/// <param name="IsMinimized">游戏窗口是否最小化</param>
/// <param name="IsForegroundOwnedByBetterGiOrGame">前台窗口属于 BetterGI 自身或游戏进程（或者没有前台窗口）。按进程 ID 判断</param>
/// <param name="Bounds">游戏捕获区域（屏幕物理像素），为空表示本次不更新位置</param>
public readonly record struct GameWindowState(
    bool IsCapturing,
    bool IsActive,
    bool IsMinimized,
    bool IsForegroundOwnedByBetterGiOrGame,
    PixelRect Bounds);

/// <summary>接收触发器调度器每帧上报的游戏窗口状态（遮罩宿主实现；无界面宿主可忽略）</summary>
public interface IGameWindowStateSink
{
    /// <summary>任意线程调用，每帧可调，立即返回</summary>
    void ReportGameWindow(GameWindowState state);
}

/// <summary>不显示遮罩时使用的空实现</summary>
public sealed class NullGameWindowStateSink : IGameWindowStateSink
{
    public static readonly NullGameWindowStateSink Instance = new();

    public void ReportGameWindow(GameWindowState state)
    {
    }
}
