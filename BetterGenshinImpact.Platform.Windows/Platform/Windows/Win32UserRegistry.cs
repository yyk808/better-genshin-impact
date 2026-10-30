using Microsoft.Win32;
using System.Collections.Generic;

namespace BetterGenshinImpact.Platform.Windows;

/// <summary>HKEY_CURRENT_USER 的只读访问（Microsoft.Win32.Registry）</summary>
public sealed class Win32UserRegistry : IUserRegistry
{
    public static Win32UserRegistry Instance { get; } = new();

    public IRegistryKey? OpenSubKey(string path)
    {
        var key = Registry.CurrentUser.OpenSubKey(path, false);
        return key == null ? null : new Key(path, key);
    }

    private sealed class Key(string path, RegistryKey key) : IRegistryKey
    {
        public string Path { get; } = path;

        public IReadOnlyList<string> GetValueNames() => key.GetValueNames();

        /// <summary>与 RegistryKey.GetValue(name) 相同（REG_EXPAND_SZ 会展开环境变量）</summary>
        public object? GetValue(string name) => key.GetValue(name);

        public void Dispose() => key.Dispose();
    }
}
