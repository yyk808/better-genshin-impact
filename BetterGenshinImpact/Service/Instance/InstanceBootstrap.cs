using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Instance.MessageHandlers;

namespace BetterGenshinImpact.Service.Instance;

public sealed partial class InstanceBootstrap : IDisposable
{
    private NamedPipeServerStream? _firstServer;
    private InitialRootConnection? _firstRootConnection;

    private InstanceBootstrap(
        InstanceContext context,
        NamedPipeServerStream? firstServer,
        InitialRootConnection? firstRootConnection)
    {
        Context = context;
        _firstServer = firstServer;
        _firstRootConnection = firstRootConnection;
    }

    public static InstanceBootstrap Current { get; private set; } = null!;

    public InstanceContext Context { get; }

    internal static void Initialize()
    {
        if (Current is not null)
        {
            return;
        }

        var options = ReadStartupOptions();
        var rootPipeName = InstancePipeNames.ForCurrentUser();
        var currentSessionId = Process.GetCurrentProcess().SessionId;

        if (options.HasExplicitInstanceType)
        {
            var instanceName = options.InstanceType == BetterGiInstanceType.WebView
                ? AcquireWebViewInstanceName(options)
                : null;

            var initialConnection = TryOpenRootConnectionAsync(
                    rootPipeName,
                    options.InstanceType,
                    instanceName,
                    options.RestartFromProcessId,
                    Environment.GetCommandLineArgs(),
                    [TimeSpan.Zero],
                    TimeSpan.FromMilliseconds(200),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            if (initialConnection?.Response.Disposition
                == ConnectionOpenDisposition.ActivationForwarded)
            {
                initialConnection.Client.Dispose();
                Environment.Exit(0);
                throw new InvalidOperationException("当前实例已将启动请求转发给现有 BetterGI。");
            }

            Current = new InstanceBootstrap(
                new InstanceContext(
                    initialConnection?.Response.AssignedType ?? options.InstanceType,
                    rootPipeName,
                    initialConnection?.Response.RootSessionId,
                    instanceName),
                firstServer: null,
                initialConnection);
            return;
        }

        WaitForRestartSource(options.RestartFromProcessId);
        try
        {
            var firstServer = InstancePipeFactory.CreateServer(
                rootPipeName,
                firstPipeInstance: true);
            Current = new InstanceBootstrap(
                new InstanceContext(
                    BetterGiInstanceType.Primary,
                    rootPipeName,
                    currentSessionId),
                firstServer,
                firstRootConnection: null);
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException)
        {
            var initialConnection = TryOpenRootConnectionAsync(
                    rootPipeName,
                    BetterGiInstanceType.Primary,
                    instanceName: null,
                    options.RestartFromProcessId,
                    Environment.GetCommandLineArgs(),
                    [
                        TimeSpan.Zero,
                        TimeSpan.FromMilliseconds(200),
                        TimeSpan.FromMilliseconds(500)
                    ],
                    TimeSpan.FromSeconds(2),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            if (initialConnection is null)
            {
                Trace.TraceError(
                    "无法连接当前用户的 BetterGI 根管道：{0}",
                    rootPipeName);
                Environment.Exit(0xFFFF);
                throw new InvalidOperationException("无法连接当前用户的 BetterGI 根实例。");
            }

            if (initialConnection.Response.Disposition
                == ConnectionOpenDisposition.ActivationForwarded)
            {
                initialConnection.Client.Dispose();
                Environment.Exit(0);
                throw new InvalidOperationException("当前实例已将启动请求转发给现有 BetterGI。");
            }

            Current = new InstanceBootstrap(
                new InstanceContext(
                    initialConnection.Response.AssignedType,
                    rootPipeName,
                    initialConnection.Response.RootSessionId),
                firstServer: null,
                initialConnection);
        }
    }

    /// <summary>启动参数中与实例选择有关的部分</summary>
    internal readonly record struct StartupOptions(
        bool HasExplicitInstanceType,
        BetterGiInstanceType InstanceType,
        int? RestartFromProcessId,
        string? InstanceName);

    /// <summary>读取本进程的启动参数，由界面平台实现</summary>
    private static partial StartupOptions ReadStartupOptions();

    /// <summary>校验网页版实例名并取得同名互斥，由界面平台实现；失败时提示并退出进程</summary>
    private static partial string AcquireWebViewInstanceName(StartupOptions options);

    internal NamedPipeServerStream? TakeFirstServer()
    {
        return Interlocked.Exchange(ref _firstServer, null);
    }

    internal InitialRootConnection? TakeFirstRootConnection()
    {
        return Interlocked.Exchange(ref _firstRootConnection, null);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _firstServer, null)?.Dispose();
        Interlocked.Exchange(ref _firstRootConnection, null)?.Client.Dispose();
    }

    private static void WaitForRestartSource(int? restartFromProcessId)
    {
        if (restartFromProcessId is null || restartFromProcessId == Environment.ProcessId)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(restartFromProcessId.Value);
            if (!process.WaitForExit(milliseconds: 15_000))
            {
                throw new TimeoutException(
                    $"等待旧 BetterGI 进程 {restartFromProcessId.Value} 退出超时。");
            }
        }
        catch (ArgumentException)
        {
            // 旧进程已经退出。
        }
    }

    private static async Task<InitialRootConnection?> TryOpenRootConnectionAsync(
        string pipeName,
        BetterGiInstanceType requestedType,
        string? instanceName,
        int? restartFromProcessId,
        string[] args,
        IReadOnlyList<TimeSpan> retryDelays,
        TimeSpan connectTimeout,
        CancellationToken cancellationToken)
    {
        var request = InstanceIpcEnvelope.Request(
            InstanceOperations.ConnectionOpen,
            new ConnectionOpenRequest
            {
                RequestedType = requestedType,
                InstanceName = instanceName,
                RestartFromProcessId = restartFromProcessId,
                Arguments = args
            });

        foreach (var retryDelay in retryDelays)
        {
            if (retryDelay > TimeSpan.Zero)
            {
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                var client = new NamedPipeClientStream(
                    ".",
                    pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.WriteThrough);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(connectTimeout);
                try
                {
                    await client.ConnectAsync(timeout.Token).ConfigureAwait(false);
                    await InstanceIpcProtocol.WriteJsonAsync(
                        client,
                        request,
                        timeout.Token).ConfigureAwait(false);
                    var frame = await InstanceIpcProtocol.ReadFrameAsync(
                        client,
                        timeout.Token).ConfigureAwait(false);
                    if (frame is null)
                    {
                        client.Dispose();
                        continue;
                    }

                    var response = InstanceIpcProtocol.ReadJson(frame.Value);
                    if (response.Operation != InstanceOperations.Response
                        || response.RequestId != request.RequestId)
                    {
                        client.Dispose();
                        continue;
                    }
                    if (response.Success != true)
                    {
                        client.Dispose();
                        throw new InvalidOperationException(
                            response.ErrorMessage ?? response.ErrorCode ?? "根实例拒绝连接。");
                    }

                    var openResponse =
                        response.Data?.ToObject<ConnectionOpenResponse>(
                            InstanceIpcProtocol.Serializer)
                        ?? throw new InvalidDataException("根实例连接响应缺少数据。");
                    return new InitialRootConnection(client, openResponse);
                }
                catch
                {
                    client.Dispose();
                    throw;
                }
            }
            catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or TimeoutException
                                              or OperationCanceledException)
            {
                Debug.WriteLine(exception);
                Trace.TraceWarning(
                    "连接 BetterGI 根管道失败，命名管道：{0}，原因：{1}",
                    pipeName,
                    exception.GetBaseException().Message);
            }
            catch (Exception exception) when (exception is InvalidOperationException
                                              or InvalidDataException)
            {
                Debug.WriteLine(exception);
                Trace.TraceError(
                    "初始化 BetterGI 实例连接失败，命名管道：{0}，原因：{1}",
                    pipeName,
                    exception.GetBaseException().Message);
                Environment.Exit(0xFFFF);
                return null;
            }
        }

        return null;
    }
}

internal sealed record InitialRootConnection(
    NamedPipeClientStream Client,
    ConnectionOpenResponse Response);

internal static class InstancePipeFactory
{
    /// <summary>Linux：根实例锁（静态持有，进程退出时释放）</summary>
    private static FileStream? _rootLock;

    /// <summary>取根实例锁 <c>$TMPDIR/CoreFxPipe_&lt;管道名&gt;.lock</c>（与 .NET 的套接字文件同目录）；已被其他进程持有时抛出 <see cref="IOException"/></summary>
    private static void AcquireRootLock(string pipeName)
    {
        if (_rootLock is not null)
            return;
        var path = Path.Combine(Path.GetTempPath(), $"CoreFxPipe_{pipeName}.lock");
        // FileShare.None 在 Unix 上即 flock(LOCK_EX | LOCK_NB)，冲突时抛出 IOException
        _rootLock = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    internal static NamedPipeServerStream CreateServer(string pipeName, bool firstPipeInstance)
    {
        if (!OperatingSystem.IsWindows())
        {
            // Unix 域套接字没有 ACL：CurrentUserOnly 让套接字只对本用户可访问并校验对端 uid。
            // .NET 在 Unix 上创建服务端会先删除同名套接字文件再 bind，FirstPipeInstance 拦不住第二个进程（会抢走根实例的套接字），
            // 因此根实例先取套接字旁锁文件的独占 flock（进程存续期间持有，退出或崩溃时由内核释放），取不到即视为根实例已存在
            if (firstPipeInstance)
                AcquireRootLock(pipeName);
            return new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
                inBufferSize: 16 * 1024,
                outBufferSize: 16 * 1024);
        }

        using var identity = WindowsIdentity.GetCurrent();
        var ownerSid = identity.User
                       ?? throw new InvalidOperationException("无法取得当前 Windows 用户 SID。");
        var networkSid = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(ownerSid);
        security.AddAccessRule(new PipeAccessRule(
            networkSid,
            PipeAccessRights.FullControl,
            AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(
            ownerSid,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        var options = PipeOptions.Asynchronous | PipeOptions.WriteThrough;
        if (firstPipeInstance)
        {
            options |= PipeOptions.FirstPipeInstance;
        }

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            options,
            inBufferSize: 16 * 1024,
            outBufferSize: 16 * 1024,
            security);
    }
}
