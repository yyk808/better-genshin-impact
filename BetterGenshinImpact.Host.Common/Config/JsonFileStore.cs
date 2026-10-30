using System.Text;
using BetterGenshinImpact.Platform;
using Serilog;

namespace BetterGenshinImpact.Host.Common.Config;

/// <summary>
/// 防抖、原子写入的 JSON 文件保存器（与 Windows 主程序 ConfigService 的保存链路相同）：
/// <list type="number">
/// <item>任意线程登记改动（<see cref="RequestSave"/>），防抖窗口内的多次改动合并为一次写盘；</item>
/// <item>快照（序列化）经 <see cref="UiHost.Dispatcher"/> 在 UI 线程生成，避免与 UI 线程上的集合修改竞争；
/// UI 线程繁忙超时则在当前线程生成；</item>
/// <item>写盘在后台线程完成，先写临时文件再原子替换（同目录 rename），版本号保证旧快照不会覆盖新快照。</item>
/// </list>
/// </summary>
public sealed class JsonFileStore : IDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly TimeSpan UiSnapshotTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StaleTempFileAge = TimeSpan.FromMinutes(1);
    private const int MaxSnapshotFailures = 3;

    private readonly Func<string> _serialize;
    private readonly TimeSpan _delay;
    private readonly ILogger _log;
    private readonly Lock _snapshotLock = new();
    private readonly Lock _fileLock = new();
    private readonly Lock _scheduleLock = new();
    private readonly Timer _timer;
    private bool _scheduled;
    private long _changeSeq;
    private long _persistedSeq;
    private int _snapshotFailures;
    private bool _disposed;

    public JsonFileStore(string path, Func<string> serialize, ILogger log, TimeSpan? delay = null)
    {
        FilePath = Path.GetFullPath(path);
        _serialize = serialize;
        _log = log;
        _delay = delay ?? TimeSpan.FromMilliseconds(500);
        _timer = new Timer(_ => _ = SaveScheduledAsync(), null, Timeout.Infinite, Timeout.Infinite);
        CleanupStaleTempFiles();
    }

    public string FilePath { get; }

    /// <summary>写盘次数（诊断 / 测试用）</summary>
    public int WriteCount { get; private set; }

    public bool HasPendingChanges => Interlocked.Read(ref _changeSeq) != Interlocked.Read(ref _persistedSeq);

    /// <summary>写盘失败（宿主可提示用户）</summary>
    public event Action<Exception>? WriteFailed;

    /// <summary>登记一次改动，防抖窗口结束后写盘；任意线程可调用</summary>
    public void RequestSave()
    {
        if (_disposed)
        {
            return;
        }

        Interlocked.Increment(ref _changeSeq);
        lock (_scheduleLock)
        {
            if (_scheduled)
            {
                return;
            }

            _scheduled = true;
            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>立即同步写盘（即使没有登记的改动，例如直接修改了集合）</summary>
    public void SaveNow()
    {
        Interlocked.Increment(ref _changeSeq);
        WriteSnapshot(CaptureOnUiThread());
    }

    /// <summary>有未保存的改动时立即同步写盘（退出时调用）</summary>
    public void Flush()
    {
        if (HasPendingChanges)
        {
            WriteSnapshot(CaptureOnUiThread());
        }
    }

    private async Task SaveScheduledAsync()
    {
        lock (_scheduleLock)
        {
            _scheduled = false;
        }

        try
        {
            if (!HasPendingChanges)
            {
                return;
            }

            Snapshot snapshot;
            try
            {
                snapshot = await CaptureOnUiThreadAsync().ConfigureAwait(false);
                Interlocked.Exchange(ref _snapshotFailures, 0);
            }
            catch (Exception e)
            {
                // 多半是其他线程正在修改集合，稍后重试
                var failures = Interlocked.Increment(ref _snapshotFailures);
                _log.Warning(e, "{File} 序列化失败（第 {Count} 次）", Path.GetFileName(FilePath), failures);
                if (failures < MaxSnapshotFailures)
                {
                    lock (_scheduleLock)
                    {
                        _scheduled = true;
                        _timer.Change(_delay, Timeout.InfiniteTimeSpan);
                    }
                }
                else
                {
                    Interlocked.Exchange(ref _snapshotFailures, 0);
                }

                return;
            }

            WriteSnapshot(snapshot);
        }
        catch (Exception e)
        {
            // 定时器回调中的异常不能抛出
            _log.Error(e, "{File} 保存失败", Path.GetFileName(FilePath));
        }
    }

    private Snapshot Capture()
    {
        lock (_snapshotLock)
        {
            // 版本号必须在序列化之前读取：序列化期间发生的改动会使版本号更大，从而继续被视为未保存
            var seq = Interlocked.Read(ref _changeSeq);
            return new Snapshot(seq, _serialize());
        }
    }

    private Snapshot CaptureOnUiThread()
    {
        var dispatcher = UiHost.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            return Capture();
        }

        try
        {
            var task = dispatcher.InvokeAsync(Capture);
            if (task.Wait(UiSnapshotTimeout))
            {
                return task.Result;
            }
        }
        catch (AggregateException e) when (e.InnerException is not null)
        {
            _log.Debug(e.InnerException, "UI 线程上生成快照失败，在当前线程重试");
        }

        return Capture();
    }

    private async Task<Snapshot> CaptureOnUiThreadAsync()
    {
        var dispatcher = UiHost.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            return Capture();
        }

        var task = dispatcher.InvokeAsync(Capture);
        try
        {
            return await task.WaitAsync(UiSnapshotTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // UI 线程长时间繁忙（或已经停止）：在当前线程生成
            return Capture();
        }
    }

    private void WriteSnapshot(Snapshot snapshot)
    {
        lock (_fileLock)
        {
            if (snapshot.Seq <= Interlocked.Read(ref _persistedSeq))
            {
                // 更新的快照已经落盘
                return;
            }

            try
            {
                WriteAtomically(snapshot.Json);
                Interlocked.Exchange(ref _persistedSeq, snapshot.Seq);
                WriteCount++;
            }
            catch (Exception e)
            {
                // 未更新 _persistedSeq，下次改动或退出时会再次尝试
                _log.Error(e, "{File} 写入失败", FilePath);
                WriteFailed?.Invoke(e);
            }
        }
    }

    private void WriteAtomically(string json)
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        // 带上进程号和 GUID，避免多个实例互相踩到同一个临时文件
        var tempFile = Path.Combine(directory, $"{Path.GetFileName(FilePath)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(tempFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = Utf8NoBom.GetBytes(json);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            // 同目录 rename，替换是原子的
            File.Move(tempFile, FilePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
            catch
            {
                // 清理失败不影响主流程
            }
        }
    }

    private void CleanupStaleTempFiles()
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath)!;
            if (!Directory.Exists(directory))
            {
                return;
            }

            var threshold = DateTime.UtcNow - StaleTempFileAge;
            foreach (var tempFile in Directory.EnumerateFiles(directory, $"{Path.GetFileName(FilePath)}.*.tmp"))
            {
                if (File.GetLastWriteTimeUtc(tempFile) < threshold)
                {
                    File.Delete(tempFile);
                }
            }
        }
        catch (Exception e)
        {
            _log.Debug(e, "清理残留临时文件失败");
        }
    }

    /// <summary>停止防抖定时器并写入未保存的改动</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
        try
        {
            Flush();
        }
        catch (Exception e)
        {
            _log.Error(e, "{File} 退出时保存失败", FilePath);
        }
    }

    private sealed record Snapshot(long Seq, string Json);
}

/// <summary>读取失败时把原文件复制到同目录的 backup 子目录（与 Windows 主程序相同）</summary>
internal static class ConfigBackup
{
    public static string? Backup(string filePath, string prefix)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            var backupDirectory = Path.Combine(Path.GetDirectoryName(filePath)!, "backup");
            Directory.CreateDirectory(backupDirectory);
            var backup = Path.Combine(backupDirectory, $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.json.bak");
            File.Copy(filePath, backup, false);
            return backup;
        }
        catch
        {
            return null;
        }
    }
}
