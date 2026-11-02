using System.Diagnostics;

namespace BetterGenshinImpact.GameTask.KeyMacro;

/// <summary>
/// PreciseDelay 的 Linux 部分。Linux 的 nanosleep / futex 超时本身是高精度计时（hrtimer，误差通常在 0.1 ms 以内），
/// 不需要 Windows 那样的高精度可等待计时器：较长的一段用取消令牌的 WaitOne 等到截止前 1 ms，最后 1 ms 内短暂自旋补齐
/// </summary>
internal static partial class PreciseDelay
{
    /// <summary>
    /// 截止前改为自旋的时长
    /// </summary>
    private const double SpinMilliseconds = 1;

    public static partial void ReleaseThreadTimer()
    {
        // 没有按线程持有的计时器
    }

    private static partial bool WaitSlice(double milliseconds, CancellationToken ct)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(milliseconds * Stopwatch.Frequency / 1000);
        var coarse = milliseconds - SpinMilliseconds;
        if (coarse >= 1 && ct.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(coarse)))
        {
            return false;
        }

        var spinner = new SpinWait();
        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (ct.IsCancellationRequested)
            {
                return false;
            }

            spinner.SpinOnce(-1);
        }

        return !ct.IsCancellationRequested;
    }
}
