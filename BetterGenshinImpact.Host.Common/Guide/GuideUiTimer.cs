using BetterGenshinImpact.Platform;

namespace BetterGenshinImpact.Service.Guide;

/// <summary>
/// 界面线程计时器（Linux）：对应 WPF 的 DispatcherTimer，计时在线程池，Tick 经 <see cref="UiHost.Dispatcher"/> 投递到界面线程。
/// 停止后已投递但尚未执行的 Tick 不再触发
/// </summary>
public sealed class GuideUiTimer : IDisposable
{
    private readonly Timer _timer;
    private TimeSpan _interval;
    private int _generation;

    /// <param name="interval">间隔</param>
    /// <param name="tick">界面线程上的回调</param>
    /// <param name="start">创建后立即开始（对应 DispatcherTimer 带回调的构造函数）</param>
    public GuideUiTimer(TimeSpan interval, EventHandler? tick = null, bool start = false)
    {
        _interval = interval;
        if (tick is not null)
        {
            Tick += tick;
        }

        _timer = new Timer(_ => OnElapsed());
        if (start)
        {
            Start();
        }
    }

    /// <summary>
    /// 每次到时在界面线程触发
    /// </summary>
    public event EventHandler? Tick;

    /// <summary>
    /// 是否正在计时
    /// </summary>
    public bool IsEnabled { get; private set; }

    /// <summary>
    /// 间隔；计时中修改时从现在重新计时（与 DispatcherTimer 相同）
    /// </summary>
    public TimeSpan Interval
    {
        get => _interval;
        set
        {
            _interval = value;
            if (IsEnabled)
            {
                Start();
            }
        }
    }

    /// <summary>
    /// 开始（或从现在重新开始）计时
    /// </summary>
    public void Start()
    {
        IsEnabled = true;
        Interlocked.Increment(ref _generation);
        var period = _interval <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : _interval;
        _timer.Change(period, period);
    }

    /// <summary>
    /// 停止计时
    /// </summary>
    public void Stop()
    {
        IsEnabled = false;
        Interlocked.Increment(ref _generation);
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void OnElapsed()
    {
        var generation = Volatile.Read(ref _generation);
        UiHost.Dispatcher.Post(() =>
        {
            if (IsEnabled && generation == Volatile.Read(ref _generation))
            {
                Tick?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
        _timer.Dispose();
    }
}
