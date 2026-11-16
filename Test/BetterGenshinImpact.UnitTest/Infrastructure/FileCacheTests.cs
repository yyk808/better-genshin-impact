using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Infrastructure.Cache;
using BetterGenshinImpact.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace BetterGenshinImpact.UnitTest.Infrastructure;

/// <summary>
/// FileCache 释放必须幂等：同一实例经具体类型、IFileCache、IStorageAreaProvider 三次登记，
/// 容器释放时会调用三次 Dispose。第二次若在已释放的 CancellationTokenSource 上 Cancel，
/// 会在 GameHost.DisposeAsync 中变成 ObjectDisposedException。
/// </summary>
public sealed class FileCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bgi-filecache-test-" + Guid.NewGuid().ToString("N"));

    public FileCacheTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    /// <summary>连续 Dispose 不应抛出 ObjectDisposedException。</summary>
    [Fact]
    public void Dispose_IsIdempotent()
    {
        var cache = new FileCache(_root);
        cache.Dispose();
        var ex = Record.Exception(() =>
        {
            cache.Dispose();
            cache.Dispose();
        });
        Assert.Null(ex);
    }

    /// <summary>按宿主同样的三次登记方式构建并释放服务容器，不应抛出。</summary>
    [Fact]
    public void ServiceProviderDispose_DoesNotThrowWhenRegisteredThreeTimes()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(sp => new FileCache(_root, sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IFileCache>(sp => sp.GetRequiredService<FileCache>());
        services.AddSingleton<IStorageAreaProvider>(sp => sp.GetRequiredService<FileCache>());
        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IFileCache>();
        _ = provider.GetServices<IStorageAreaProvider>().ToList();

        var ex = Record.Exception(provider.Dispose);
        Assert.Null(ex);
    }

    /// <summary>释放后的令牌应对进行中的 factory 表现为已取消，且再次 Dispose 仍安全。</summary>
    [Fact]
    public async Task Dispose_CancelsLifetimeTokenThenIgnoresFurtherDispose()
    {
        var cache = new FileCache(_root);
        using var started = new ManualResetEventSlim(false);
        using var allowFinish = new ManualResetEventSlim(false);
        CancellationToken factoryToken = default;
        var load = cache.GetOrAddBytesAsync(CachePartitions.MapPointImages, "dispose-lifetime", async ct =>
        {
            factoryToken = ct;
            started.Set();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
            }

            allowFinish.Set();
            return null;
        });

        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        cache.Dispose();
        Assert.True(factoryToken.IsCancellationRequested);
        Assert.True(allowFinish.Wait(TimeSpan.FromSeconds(5)));
        var ex = Record.Exception(cache.Dispose);
        Assert.Null(ex);
        await load;
    }
}
