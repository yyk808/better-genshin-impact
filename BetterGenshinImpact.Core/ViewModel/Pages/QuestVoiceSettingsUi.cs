using System;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// 自动配音设置模型需要的界面能力：UI 线程投递、防抖计时器、确认框和文件选择。
/// WPF 与 Avalonia 宿主各自实现；未注入时使用 <see cref="HeadlessQuestVoiceSettingsUi"/>（组件测试与无界面场景）。
/// </summary>
public interface IQuestVoiceSettingsUi
{
    /// <summary>以低优先级异步投递到 UI 线程；宿主正在关闭时返回 false 且不执行。</summary>
    bool TryPost(Action action);

    /// <summary>在 UI 线程触发的单次计时器（Start 重新计时，Stop 取消）。</summary>
    IQuestVoiceUiTimer CreateTimer(TimeSpan interval, Action tick);

    /// <summary>破坏性操作确认，默认按钮为“否”。</summary>
    Task<bool> ConfirmAsync(string message, string title);

    /// <summary>选择一个已有文件；filter 采用“说明|*.ext|说明|*.*”格式。取消返回 null。</summary>
    Task<string?> PickOpenFileAsync(string title, string filter, string? initialDirectory);

    /// <summary>选择保存位置；取消返回 null。</summary>
    Task<string?> PickSaveFileAsync(string title, string filter, string fileName);
}

/// <summary>UI 线程计时器。</summary>
public interface IQuestVoiceUiTimer
{
    void Start();
    void Stop();
}

/// <summary>
/// 无界面实现：投递直接执行；计时器在线程池计时后执行回调；确认一律通过（与原先“没有主窗口时不弹框”一致）；文件选择返回 null。
/// </summary>
public sealed class HeadlessQuestVoiceSettingsUi : IQuestVoiceSettingsUi
{
    public bool TryPost(Action action)
    {
        action();
        return true;
    }

    public IQuestVoiceUiTimer CreateTimer(TimeSpan interval, Action tick) => new PoolTimer(interval, tick);

    public Task<bool> ConfirmAsync(string message, string title) => Task.FromResult(true);

    public Task<string?> PickOpenFileAsync(string title, string filter, string? initialDirectory) => Task.FromResult<string?>(null);

    public Task<string?> PickSaveFileAsync(string title, string filter, string fileName) => Task.FromResult<string?>(null);

    private sealed class PoolTimer(TimeSpan interval, Action tick) : IQuestVoiceUiTimer
    {
        private readonly Timer _timer = new(_ => tick());
        public void Start() => _timer.Change(interval, Timeout.InfiniteTimeSpan);
        public void Stop() => _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }
}
