using System;
using System.Diagnostics;
using System.Threading;

namespace BetterGenshinImpact.GameTask.KeyMacro;

/// <summary>
/// 高精度、可立即中断的等待。
/// 项目没有调用 timeBeginPeriod，Thread.Sleep 的精度受系统计时器影响（默认约 15.6 ms），
/// 这里改用高精度可等待计时器（Windows 10 1803 起支持），不支持时退回 WaitHandle.WaitOne
/// </summary>
internal static partial class PreciseDelay
{
    /// <summary>
    /// 每段最长等待时间。段间检查停止条件，保证松开热键或切出游戏后能及时停止
    /// </summary>
    private const double SliceMilliseconds = 50;

    /// <summary>
    /// 等待指定毫秒数
    /// </summary>
    /// <param name="milliseconds">等待时长，小于等于 0 时只检查一次停止条件</param>
    /// <param name="ct">取消令牌，取消时立即返回</param>
    /// <param name="shouldContinue">停止条件，返回 false 时立即结束等待</param>
    /// <returns>等待完整结束返回 true；被取消或停止条件不满足时返回 false</returns>
    public static bool Wait(int milliseconds, CancellationToken ct, Func<bool> shouldContinue)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(milliseconds * (double)Stopwatch.Frequency / 1000);
        while (true)
        {
            if (ct.IsCancellationRequested || !shouldContinue())
            {
                return false;
            }

            // 用截止时间计算剩余时长，分段等待不累积误差
            var remaining = (deadline - Stopwatch.GetTimestamp()) * 1000.0 / Stopwatch.Frequency;
            if (remaining <= 0)
            {
                return true;
            }

            if (!WaitSlice(Math.Min(remaining, SliceMilliseconds), ct))
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 关闭当前线程的计时器（执行线程结束前调用）
    /// </summary>
    public static partial void ReleaseThreadTimer();

    /// <summary>
    /// 等待一段时间，被取消时返回 false
    /// </summary>
    private static partial bool WaitSlice(double milliseconds, CancellationToken ct);
}
