using System;
using System.Diagnostics;
using System.Security.Principal;

namespace BetterGenshinImpact.Service.Instance;

public enum BetterGiInstanceType
{
    Primary,
    ChildSession,
    WebView
}

public sealed class InstanceContext
{
    internal InstanceContext(
        BetterGiInstanceType instanceType,
        string rootPipeName,
        int? rootSessionId,
        string? instanceName = null)
    {
        InstanceType = instanceType;
        InstanceName = instanceType == BetterGiInstanceType.WebView ? instanceName : null;
        RootPipeName = rootPipeName;
        RootSessionId = rootSessionId;
        ProcessId = Environment.ProcessId;
        WindowsSessionId = Process.GetCurrentProcess().SessionId;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public BetterGiInstanceType InstanceType { get; }

    /// <summary>
    /// 网页版实例的实例名（已校验、已去除首尾空白），其他实例为 null
    /// </summary>
    public string? InstanceName { get; }

    public bool IsWebView => InstanceType == BetterGiInstanceType.WebView;

    public string RootPipeName { get; }

    public int? RootSessionId { get; private set; }

    public int ProcessId { get; }

    public int WindowsSessionId { get; }

    public DateTimeOffset StartedAt { get; }

    public bool IsRoot => InstanceType == BetterGiInstanceType.Primary;

    internal void SetRootSessionId(int rootSessionId)
    {
        RootSessionId = rootSessionId;
    }

    public InstanceEndpoint ToEndpoint()
    {
        return new InstanceEndpoint
        {
            InstanceType = InstanceType,
            ProcessId = ProcessId,
            WindowsSessionId = WindowsSessionId,
            StartedAt = StartedAt,
            InstanceName = InstanceName
        };
    }
}

public sealed class InstanceEndpoint
{
    public BetterGiInstanceType InstanceType { get; init; }

    public int ProcessId { get; init; }

    public int WindowsSessionId { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// 网页版实例的实例名，其他实例为 null。只用于识别和展示，同名互斥由实例名互斥体保证
    /// </summary>
    public string? InstanceName { get; init; }
}

internal static class InstancePipeNames
{
    private const string Prefix = "BetterGI.v2.user-";

    internal static string ForCurrentUser()
    {
        // Linux：按用户 uid 区分（.NET 命名管道在 Unix 上是 /tmp 下的 Unix 域套接字，以 CurrentUserOnly 限定本用户）
        if (!OperatingSystem.IsWindows())
            return $"{Prefix}uid-{UnixGetUid()}.root";
        using var identity = WindowsIdentity.GetCurrent();
        var userSid = identity.User
                      ?? throw new InvalidOperationException("无法取得当前 Windows 用户 SID。");
        return ForUserSid(userSid.Value);
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "getuid")]
    private static extern uint UnixGetUid();

    internal static string ForUserSid(string userSid)
    {
        return $"{Prefix}{userSid}.root";
    }
}
