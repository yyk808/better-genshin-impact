using BetterGenshinImpact.Platform;
using System;
using System.Diagnostics;
using System.IO;

namespace BetterGenshinImpact.Genshin.Settings;

/// <summary>原神设置所在注册表的区服</summary>
public enum GenshinRegistryType
{
    Auto,
    Chinese,
    Global,
    Cloud,
}

/// <summary>
/// 原神保存在注册表中的设置（GENERAL_DATA、分辨率等）。Windows 读 HKCU；Linux 读 Wine 前缀的 user.reg
/// </summary>
public interface IGameSettingsStore
{
    /// <summary>
    /// 打开原神设置所在的注册表键（HKCU\SOFTWARE\miHoYo\原神 或 Genshin Impact），不存在或出错时返回 null
    /// </summary>
    /// <param name="type">区服；Auto 时按运行中的进程、配置的游戏路径、注册表是否存在依次判断</param>
    /// <param name="gameExecutablePath">配置的原神可执行文件路径，用于自动判断区服</param>
    IRegistryKey? OpenGameKey(GenshinRegistryType type = GenshinRegistryType.Auto, string? gameExecutablePath = null);
}

/// <summary>
/// <see cref="IGameSettingsStore"/> 的通用实现（从主程序 GenshinRegistry 迁入，判断规则不变）
/// </summary>
/// <param name="registry">当前用户注册表</param>
/// <param name="isProcessRunning">进程名（不含扩展名）是否在运行；为 null 时用 <see cref="Process.GetProcessesByName(string)"/></param>
public sealed class GenshinSettingsStore(IUserRegistry registry, Func<string, bool>? isProcessRunning = null) : IGameSettingsStore
{
    public const string ChineseRegistryPath = @"SOFTWARE\miHoYo\原神";
    public const string GlobalRegistryPath = @"SOFTWARE\miHoYo\Genshin Impact";

    private readonly Func<string, bool> _isProcessRunning = isProcessRunning ?? IsProcessRunningByName;

    public IRegistryKey? OpenGameKey(GenshinRegistryType type = GenshinRegistryType.Auto, string? gameExecutablePath = null)
    {
        try
        {
            if (type == GenshinRegistryType.Auto)
            {
                type = ResolveRegistryType(gameExecutablePath);
            }

            if (type == GenshinRegistryType.Chinese)
            {
                return registry.OpenSubKey(ChineseRegistryPath);
            }

            if (type == GenshinRegistryType.Global)
            {
                return registry.OpenSubKey(GlobalRegistryPath);
            }

            if (type == GenshinRegistryType.Cloud)
            {
                throw new NotImplementedException();
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.ToString());
        }

        return null;
    }

    /// <summary>自动模式优先根据正在运行的游戏进程判断区服，其次使用配置的游戏路径，最后根据注册表是否存在判断</summary>
    public GenshinRegistryType ResolveRegistryType(string? gameExecutablePath)
    {
        var isChineseRunning = _isProcessRunning("YuanShen");
        var isGlobalRunning = _isProcessRunning("GenshinImpact");

        if (isChineseRunning != isGlobalRunning)
        {
            return isChineseRunning ? GenshinRegistryType.Chinese : GenshinRegistryType.Global;
        }

        // 配置的路径可能是 Windows 路径，在 Linux 上也要能取到文件名
        var executableName = Path.GetFileName(gameExecutablePath?.Replace('\\', '/'));
        if (string.Equals(executableName, "YuanShen.exe", StringComparison.OrdinalIgnoreCase))
        {
            return GenshinRegistryType.Chinese;
        }

        if (string.Equals(executableName, "GenshinImpact.exe", StringComparison.OrdinalIgnoreCase))
        {
            return GenshinRegistryType.Global;
        }

        using var chineseKey = registry.OpenSubKey(ChineseRegistryPath);
        using var globalKey = registry.OpenSubKey(GlobalRegistryPath);
        if (chineseKey is null && globalKey is not null)
        {
            return GenshinRegistryType.Global;
        }

        // 两个注册表同时存在或都不存在时保留原有的国服优先回退行为，用户仍可在键位映射页手动指定
        return GenshinRegistryType.Chinese;
    }

    /// <summary>值名中包含 <paramref name="fragment"/> 的第一个值名（原神的值名带哈希后缀，如 GENERAL_DATA_h2389025596），没有为空字符串</summary>
    public static string FindValueName(IRegistryKey key, string fragment)
    {
        foreach (var name in key.GetValueNames())
        {
            if (name.Contains(fragment))
            {
                return name;
            }
        }

        return string.Empty;
    }

    private static bool IsProcessRunningByName(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}
