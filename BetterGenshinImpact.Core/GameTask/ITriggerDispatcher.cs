using System;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 触发器调度器中任务 / 脚本代码会用到的部分。Windows 实现为 TaskTriggerDispatcher
/// </summary>
public interface ITriggerDispatcher
{
    /// <summary>暂停触发器识别（推理基准测试等），Dispose 时恢复</summary>
    IDisposable PauseForInference();

    /// <summary>任务开始：进入任务模式并清空启用名单</summary>
    void BeginTask();

    /// <summary>任务结束：退出任务模式并清空启用名单，重复调用无副作用</summary>
    void EndTask();

    /// <summary>在任务中启用一个触发器，返回租约，名称不支持时返回 null</summary>
    /// <param name="name">触发器名称</param>
    /// <param name="options">脚本传入的参数，AutoPick 为 AutoPickExternalConfig，AutoSkip 为 AutoSkipConfig</param>
    IDisposable? AddTrigger(string name, object? options = null);

    /// <summary>清空启用名单，不销毁任何触发器实例</summary>
    void ClearTriggers();

    void StartTimer();

    void StopTimer();
}

/// <summary>从容器获取 <see cref="ITriggerDispatcher"/>（原 TaskTriggerDispatcher.Instance() 的去宿主版本）</summary>
public static class TriggerDispatcher
{
    public static ITriggerDispatcher Instance() =>
        CoreServices.GetService<ITriggerDispatcher>() ?? throw new InvalidOperationException("调度器未注册");

    public static ITriggerDispatcher? InstanceNullable() => CoreServices.GetService<ITriggerDispatcher>();
}
