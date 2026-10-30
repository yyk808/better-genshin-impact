using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Platform;

/// <summary>
/// 只读的注册表键。值的类型与 Microsoft.Win32.RegistryKey.GetValue 一致：
/// REG_SZ / REG_EXPAND_SZ → string（不展开环境变量），REG_DWORD → int，REG_QWORD → long，
/// REG_MULTI_SZ → string[]，REG_BINARY 及其它 → byte[]
/// </summary>
public interface IRegistryKey : IDisposable
{
    /// <summary>相对 HKEY_CURRENT_USER 的路径</summary>
    string Path { get; }

    IReadOnlyList<string> GetValueNames();

    /// <summary>值不存在时为 null；名称不区分大小写</summary>
    object? GetValue(string name);
}

/// <summary>
/// 当前用户注册表（HKEY_CURRENT_USER）的只读访问。Windows 读真实注册表；Linux 读 Wine 前缀的 user.reg
/// </summary>
public interface IUserRegistry
{
    /// <summary>打开子键（如 <c>Software\miHoYo\原神</c>），不存在时返回 null</summary>
    IRegistryKey? OpenSubKey(string path);
}
