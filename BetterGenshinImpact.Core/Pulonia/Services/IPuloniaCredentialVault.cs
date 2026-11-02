namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 账号登录凭据的保存位置，只由账号存储在目录保存成功后调用。
/// 生产使用 Windows 凭据管理器（Windows）或 Secret Service（Linux）；测试替换为内存实现，避免接触用户真实凭据。
/// </summary>
public interface IPuloniaCredentialVault
{
    /// <summary>读取凭据；不存在时返回 null（账户切换的密码登录使用）。凭据库不可用时抛出带说明的异常</summary>
    /// <param name="target">凭据目标名</param>
    PuloniaCredential? Read(string target) => null;

    /// <summary>写入或覆盖同名凭据，失败时抛出异常。</summary>
    /// <param name="target">凭据目标名</param>
    /// <param name="userName">登录账号</param>
    /// <param name="password">登录密码</param>
    void Write(string target, string userName, string password);

    /// <summary>尽力删除凭据；凭据不存在或删除失败都不抛出异常，遗留条目不再被账号目录引用。</summary>
    /// <param name="target">凭据目标名</param>
    void Delete(string target);
}

/// <summary>一条登录凭据</summary>
/// <param name="UserName">登录账号</param>
/// <param name="Password">登录密码</param>
public sealed record PuloniaCredential(string UserName, string Password)
{
    /// <summary>不在日志、异常或调试输出中显示密码</summary>
    public override string ToString() => $"PuloniaCredential {{ UserName = {UserName} }}";
}

/// <summary>账户自动切换的运行环境检查（平台相关）</summary>
public interface IPuloniaAccountSwitchEnvironment
{
    /// <summary>当前租约绑定的运行环境能否自动切换账户：只支持 PC 官服本机窗口与本机键鼠输入；不能切换时返回原因</summary>
    /// <param name="runtime">调用方租约持有的运行环境</param>
    string? GetUnsupportedReason(BetterGenshinImpact.GameTask.Runtime.GameRuntime runtime);
}
