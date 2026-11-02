using System;
using System.IO;
using System.Threading;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 跨实例的占用文件（统筹设计 4.5、4.6）：账户占用 <c>accounts/&lt;账户 ID&gt;/run.lock</c> 与在线标记 <c>instances/&lt;目录名&gt;/online.lock</c>。
/// 持有者以“允许他人读、不允许他人写”的方式打开文件并写入持有者信息，存续期间保持句柄；进程退出时由系统释放，崩溃不会留下占用。
/// 其他实例尝试以写方式打开失败即视为被占用，读取文件内容得到持有者。不经过 IPC，也不取 <c>.writer.lock</c>。
/// </summary>
/// <remarks>
/// 判断是否被占用时会以写方式短暂打开文件，这一瞬间会让同时取得占用的一方失败，因此取得占用时短时重试几次。
/// </remarks>
public sealed partial class PuloniaTaskStore
{
    /// <summary>账户占用文件名，位于账户进度目录下。</summary>
    private const string AccountRunLockFileName = "run.lock";

    /// <summary>在线标记文件名，位于实例目录下。</summary>
    private const string InstanceOnlineLockFileName = "online.lock";

    // 共享冲突的判断复用 PuloniaTaskStore.cs 中写锁的 IsSharingViolation。

    /// <summary>取得占用时的尝试次数；其他实例判断占用时的短暂打开可能造成一次失败。</summary>
    private const int LockAcquireAttempts = 5;

    /// <summary>两次尝试之间的等待（毫秒）。</summary>
    private const int LockRetryDelayMilliseconds = 60;

    /// <summary>
    /// 尝试取得账户占用：同一账户同一时刻只能被一个运行占用，不论在哪个实例。
    /// </summary>
    /// <param name="accountId">账户 ID</param>
    /// <param name="owner">写入占用文件的持有者信息</param>
    /// <param name="occupant">取得失败时的当前持有者；文件内容读不出时为 null</param>
    /// <returns>持有占用的句柄，释放即解除占用；被其他持有者占用时为 null</returns>
    /// <exception cref="IOException">占用文件无法建立或写入（非共享冲突）</exception>
    public IDisposable? TryAcquireAccountOccupancy(string accountId, PuloniaLockOwner owner, out PuloniaLockOwner? occupant)
    {
        var path = Path.Combine(GetAccountProgressDirectory(RootDirectory, accountId), AccountRunLockFileName);
        var stream = TryAcquireLock(path, owner);
        occupant = stream is null ? TryReadLockOwner(path) : null;
        return stream;
    }

    /// <summary>
    /// 读取账户当前的占用者：占用文件存在、句柄仍被持有时返回文件中的持有者；没有被占用时为 null。
    /// </summary>
    /// <param name="accountId">账户 ID</param>
    /// <returns>占用者；文件内容读不出但确实被占用时返回只含空实例键的占用者</returns>
    public PuloniaLockOwner? GetAccountOccupant(string accountId)
    {
        var path = Path.Combine(GetAccountProgressDirectory(RootDirectory, accountId), AccountRunLockFileName);
        return GetLockOwner(path);
    }

    /// <summary>
    /// 取得本实例的在线标记，实例存续期间保持。
    /// </summary>
    /// <param name="owner">持有者信息，实例键为本实例</param>
    /// <returns>持有在线标记的句柄；同一实例键已有进程持有时为 null</returns>
    /// <exception cref="IOException">在线标记文件无法建立或写入（非共享冲突）</exception>
    public IDisposable? TryAcquireInstancePresence(PuloniaLockOwner owner)
        => TryAcquireLock(Path.Combine(GetInstanceDirectory(owner.InstanceKey), InstanceOnlineLockFileName), owner);

    /// <summary>
    /// 另一个实例是否在线：它的在线标记文件存在且句柄仍被持有。文件不存在视为不在线。
    /// </summary>
    /// <param name="instanceKey">实例键</param>
    public bool IsInstanceOnline(string instanceKey)
        => GetLockOwner(Path.Combine(GetInstanceDirectory(instanceKey), InstanceOnlineLockFileName)) is not null;

    /// <summary>
    /// 以“允许他人读、不允许他人写”的方式打开占用文件，截断后写入持有者信息并刷新到磁盘；
    /// 共享冲突时短时重试，仍冲突则返回 null。
    /// </summary>
    /// <param name="path">占用文件路径</param>
    /// <param name="owner">持有者信息</param>
    private static FileStream? TryAcquireLock(string path, PuloniaLockOwner owner)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = Utf8WithoutBom.GetBytes(PuloniaTaskJson.Write(owner));
        for (var attempt = 1; ; attempt++)
        {
            FileStream stream;
            try
            {
                stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, HolderShare);
            }
            catch (IOException ex) when (IsSharingViolation(ex))
            {
                if (attempt >= LockAcquireAttempts)
                    return null;
                Thread.Sleep(LockRetryDelayMilliseconds * attempt);
                continue;
            }
            try
            {
                stream.SetLength(0);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
                return stream;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
    }

    /// <summary>
    /// 占用文件的持有者：先不加限制地读取内容（不影响持有者，也不影响取得占用的一方），
    /// 内容为空或文件不存在时视为没有占用；有内容时再判断句柄是否仍被持有，只有被持有才返回内容。
    /// </summary>
    /// <param name="path">占用文件路径</param>
    private static PuloniaLockOwner? GetLockOwner(string path)
    {
        if (!File.Exists(path))
            return null;
        var owner = TryReadLockOwner(path);
        if (owner is null && new FileInfo(path).Length == 0)
            return null;
        return IsLockHeld(path) ? owner ?? new PuloniaLockOwner() : null;
    }

    /// <summary>
    /// 以只读、允许他人读写和删除的方式读取持有者信息；文件不存在、为空或内容读不出时为 null。
    /// </summary>
    /// <param name="path">占用文件路径</param>
    private static PuloniaLockOwner? TryReadLockOwner(string path)
    {
        try
        {
            using var stream = OpenOwnerForRead(path);
            using var reader = new StreamReader(stream, Utf8WithoutBom, detectEncodingFromByteOrderMarks: true);
            var json = reader.ReadToEnd();
            return json.Length == 0 ? null : PuloniaTaskJson.Read<PuloniaLockOwner>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException
                                       or System.Text.DecoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>
    /// 持有者打开占用文件的共享方式。Windows：允许他人读、不允许他人写（共享冲突即互斥）。
    /// Linux / macOS：.NET 把 <see cref="FileShare.None"/> 映射为 <c>flock(LOCK_EX|LOCK_NB)</c>，其余共享方式映射为共享锁 <c>LOCK_SH</c>，
    /// 两个持有者都用 <see cref="FileShare.Read"/> 时都能取得、并不互斥，因此改用 <see cref="FileShare.None"/> 取得独占 flock；
    /// 其他实例的判断（<see cref="IsLockHeld"/> 取 <c>LOCK_SH</c>）因此得到共享冲突，进程退出时由系统释放。
    /// </summary>
    private static FileShare HolderShare => OperatingSystem.IsWindows() ? FileShare.Read : FileShare.None;

    /// <summary>
    /// 只读打开占用文件读取持有者信息。Windows 照常以允许他人读写和删除的方式打开；
    /// Linux 上 .NET 按路径打开文件时会取 <c>flock(LOCK_SH)</c>，会与持有者的独占锁冲突，
    /// 因此直接用 <c>open(2)</c> 取得描述符再包装成流（按句柄构造的流不加 flock；flock 是建议性的，不妨碍读取）。
    /// </summary>
    private static FileStream OpenOwnerForRead(string path)
    {
        if (!OperatingSystem.IsLinux())
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var fd = UnixOpen(path, UnixOpenReadOnlyCloseOnExec);
        if (fd < 0)
        {
            var errno = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
            throw errno == 2 ? new FileNotFoundException(null, path) : new IOException($"打开 {path} 失败（errno {errno}）");
        }
        return new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(fd, ownsHandle: true), FileAccess.Read);
    }

    /// <summary>O_RDONLY | O_CLOEXEC（Linux 取值）</summary>
    private const int UnixOpenReadOnlyCloseOnExec = 0x80000;

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int UnixOpen([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string path, int flags);

    /// <summary>
    /// 以写方式短暂打开占用文件判断句柄是否仍被持有：共享冲突即被持有，打开成功立即关闭。
    /// </summary>
    /// <param name="path">占用文件路径</param>
    private static bool IsLockHeld(string path)
    {
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            return false;
        }
        catch (IOException ex) when (IsSharingViolation(ex))
        {
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 文件已删除或无权写入：无法证明被持有，按没有占用处理
            return false;
        }
    }
}
