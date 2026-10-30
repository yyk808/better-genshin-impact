using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;

namespace BetterGenshinImpact;

/// <summary>
/// 宿主服务容器的静态入口，供不经依赖注入创建的识别 / 任务代码使用（原为主程序的 App.GetLogger / App.GetService / App.ServiceProvider）。
/// 宿主（WPF 主程序在程序集加载时、Linux / Avalonia 宿主在启动时）调用 <see cref="SetProvider"/>，
/// 每次访问时才取容器，与原来的懒解析行为一致。未设置时：日志为空实现，GetService 返回 null，
/// <see cref="Provider"/> 是一个空容器（GetRequiredService 抛 InvalidOperationException）
/// </summary>
public static class CoreServices
{
    private static Func<IServiceProvider>? _providerFactory;

    /// <summary>设置服务容器的取法（每次访问调用，便于宿主延迟创建容器）</summary>
    public static void SetProvider(Func<IServiceProvider> providerFactory)
    {
        _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
    }

    /// <summary>是否已设置宿主容器</summary>
    public static bool HasProvider => _providerFactory != null;

    public static IServiceProvider Provider => _providerFactory?.Invoke() ?? EmptyServiceProvider.Instance;

    public static ILogger<T> GetLogger<T>() =>
        Provider.GetService(typeof(ILogger<T>)) as ILogger<T> ?? NullLogger<T>.Instance;

    public static T? GetService<T>() where T : class => Provider.GetService(typeof(T)) as T;

    public static object? GetService(Type type) => Provider.GetService(type);

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static EmptyServiceProvider Instance { get; } = new();

        public object? GetService(Type serviceType) => null;
    }
}
