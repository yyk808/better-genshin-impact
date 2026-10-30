using System.Collections.Concurrent;
using System.Diagnostics;

namespace BetterGenshinImpact.Platform.Linux.X11;

/// <summary>
/// 本进程经 XTest 注入的按键 / 鼠标按钮记录。XInput2 原始事件监听据此识别并忽略自己注入的事件，
/// 避免“快捷键动作发出的键又触发快捷键 / 被录制进去”的回环；其它程序（xdotool 等）经 XTest 发出的事件不受影响
/// </summary>
public static class XTestInjectionLedger
{
    /// <summary>注入后多久内收到的同一事件视为自己注入的</summary>
    private const long WindowMs = 1000;

    private static readonly ConcurrentDictionary<(bool Button, int Code, bool Down), ConcurrentQueue<long>> Pending = new();

    /// <summary>注入前登记（keycode 或 X 按钮号）</summary>
    public static void Note(bool button, int code, bool down)
    {
        var queue = Pending.GetOrAdd((button, code, down), _ => new ConcurrentQueue<long>());
        var now = Stopwatch.GetTimestamp();
        // 没有监听在消费时只保留时间窗内的记录
        while (queue.TryPeek(out var t) && Stopwatch.GetElapsedTime(t, now).TotalMilliseconds > WindowMs) queue.TryDequeue(out _);
        queue.Enqueue(now);
    }

    /// <summary>收到 XTEST 设备的原始事件时调用：是本进程最近注入的则消费一条记录并返回 true</summary>
    public static bool TryConsume(bool button, int code, bool down)
    {
        if (!Pending.TryGetValue((button, code, down), out var queue)) return false;
        var now = Stopwatch.GetTimestamp();
        while (queue.TryPeek(out var t))
        {
            if (Stopwatch.GetElapsedTime(t, now).TotalMilliseconds > WindowMs)
            {
                queue.TryDequeue(out _);
                continue;
            }

            return queue.TryDequeue(out _);
        }

        return false;
    }
}
