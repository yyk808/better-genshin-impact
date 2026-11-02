namespace BetterGenshinImpact.Core.Mask;

/// <summary>
/// 遮罩窗口状态的通知入口（Linux）：悬浮小窗在遮罩显示或移动后重新置顶自己。
/// Windows 版的 IMaskWindowHost 还负责遮罩窗口的创建与跟随，Linux 的遮罩由 Avalonia 宿主管理，这里只保留小窗用到的部分
/// </summary>
public interface IMaskWindowHost
{
    /// <summary>
    /// 最近一次的遮罩状态
    /// </summary>
    MaskWindowState State { get; }

    /// <summary>
    /// 遮罩可见性或位置变化后在界面线程触发
    /// </summary>
    event EventHandler<MaskWindowState>? StateChanged;
}

/// <summary>
/// 遮罩窗口的状态
/// </summary>
/// <param name="IsVisible">是否可见</param>
/// <param name="Bounds">窗口位置（物理像素）</param>
public readonly record struct MaskWindowState(bool IsVisible, System.Drawing.Rectangle Bounds);

/// <summary>
/// 遮罩状态通知的默认实现：由遮罩窗口所在的宿主调用 <see cref="Publish"/>
/// </summary>
public sealed class MaskWindowStateSource : IMaskWindowHost
{
    /// <inheritdoc />
    public MaskWindowState State { get; private set; }

    /// <inheritdoc />
    public event EventHandler<MaskWindowState>? StateChanged;

    /// <summary>
    /// 在界面线程发布遮罩的新状态
    /// </summary>
    public void Publish(MaskWindowState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }
}
