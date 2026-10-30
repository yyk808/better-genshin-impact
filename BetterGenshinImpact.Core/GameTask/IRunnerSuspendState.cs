using BetterGenshinImpact.GameTask.AutoPathing.Suspend;
using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask;

/// <summary>任务暂停状态（RunnerContext 中供等待 / 暂停逻辑使用的部分）</summary>
public interface IRunnerSuspendState
{
    /// <summary>是否处于快捷键暂停中</summary>
    bool IsSuspend { get; }

    /// <summary>暂停 / 恢复时需要通知的子任务</summary>
    Dictionary<string, ISuspendable> SuspendableDictionary { get; }

    /// <summary>暂停自动拾取（实时触发器暂停计数 +1；time 大于 0 时到时自动恢复）</summary>
    void StopAutoPick(int time = -1);

    /// <summary>恢复自动拾取（计数 -1；time 大于 0 时延迟恢复）</summary>
    void ResumeAutoPick(int time = 0);
}

/// <summary><see cref="IRunnerSuspendState"/> 的入口，由宿主设置 <see cref="Provider"/>（Windows 为 RunnerContext.Instance）</summary>
public static class RunnerSuspend
{
    public static Func<IRunnerSuspendState>? Provider { get; set; }

    public static IRunnerSuspendState Current =>
        (Provider ?? throw new InvalidOperationException("宿主尚未设置 RunnerSuspend.Provider"))();
}
