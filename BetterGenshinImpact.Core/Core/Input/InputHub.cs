using System;
using System.Threading;

namespace BetterGenshinImpact.Core.Input;

/// <summary>
/// 模拟输入的统一入口。
/// 业务代码只通过这里取通道，不直接使用 Simulation / PostMessageSimulator 等原始实现。
/// <para>
/// 通道引用只能在单个任务内持有；跨任务存活的对象（静态字段、DI 单例、Trigger 等）必须每次从这里取，
/// 否则后端切换后引用会过期。
/// </para>
/// </summary>
public static class InputHub
{
    private static readonly object AttachLock = new();

    private static IInputBackend? _backend;

    private static Func<IInputBackend>? _defaultBackendFactory;

    /// <summary>
    /// 未 Attach 时使用的默认后端的工厂，首次取 <see cref="Backend"/> 时调用一次。
    /// Windows 主程序在程序集加载时设置为“未绑定窗口的 Win32 后端”（前台可用，后台 warn），与迁移前相同；
    /// 未设置时为 <see cref="UnsupportedInputBackend"/>
    /// </summary>
    public static Func<IInputBackend>? DefaultBackendFactory
    {
        get => Volatile.Read(ref _defaultBackendFactory);
        set => Volatile.Write(ref _defaultBackendFactory, value);
    }

    /// <summary>
    /// 新建一个默认后端（解绑运行环境后换回它）。Windows 主程序下为未绑定窗口的 Win32 后端，与迁移前相同
    /// </summary>
    public static IInputBackend CreateDefaultBackend() =>
        DefaultBackendFactory?.Invoke() ?? new UnsupportedInputBackend("未设置输入后端");

    public static IInputBackend Backend
    {
        get
        {
            var backend = Volatile.Read(ref _backend);
            if (backend != null)
            {
                return backend;
            }

            lock (AttachLock)
            {
                return _backend ??= CreateDefaultBackend();
            }
        }
    }

    public static IInputChannel Foreground => Backend.Foreground;

    /// <summary>
    /// 没有后台通道的后端（Linux 原生）下 <see cref="Background"/> 返回的通道：忽略操作并限频告警
    /// </summary>
    private static readonly IInputChannel NoBackground = new UnsupportedInputChannel("当前输入后端没有后台通道");

    /// <summary>
    /// 后台通道。WebSdk / Gamepad 后端下与 <see cref="Foreground"/> 相同；
    /// 后端没有后台通道时（<see cref="IInputBackend.Background"/> 为 null）返回一个忽略操作并告警的通道，
    /// Win32 后端始终有后台通道，行为不变
    /// </summary>
    public static IInputChannel Background => Backend.Background ?? NoBackground;

    /// <summary>
    /// 替换当前后端并释放旧后端。按键释放由停止流程显式处理，避免绑定新后端时释放全局按键。
    /// 只在没有任务运行时调用。
    /// (a,b)、c、d 之间的切换都通过它完成
    /// </summary>
    public static void Attach(IInputBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        lock (AttachLock)
        {
            var old = _backend;
            if (ReferenceEquals(old, backend))
            {
                return;
            }

            Volatile.Write(ref _backend, backend);
            old?.Dispose();
        }
    }

    /// <summary>
    /// 释放当前后端所有通道中处于按下状态的按键
    /// </summary>
    public static void ReleaseAll() => Backend.ReleaseAll();
}
