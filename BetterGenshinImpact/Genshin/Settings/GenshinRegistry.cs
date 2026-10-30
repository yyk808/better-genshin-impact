using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Platform;

namespace BetterGenshinImpact.Genshin.Settings;

/// <summary>
/// 原神设置注册表的静态入口，实现见 Core 的 <see cref="GenshinSettingsStore"/>（注册表来自平台服务：Windows 为 HKCU，Linux 为 Wine 前缀）
/// </summary>
internal class GenshinRegistry
{
    /// <summary>
    /// 自动模式优先根据正在运行的游戏进程判断区服，其次使用配置的游戏路径，最后根据注册表是否存在判断
    /// </summary>
    /// <param name="type"></param>
    /// <param name="gameExecutablePath">配置的原神可执行文件路径，用于自动判断区服</param>
    /// <returns></returns>
    public static IRegistryKey? GetRegistryKey(GenshinRegistryType type = GenshinRegistryType.Auto, string? gameExecutablePath = null)
    {
        return new GenshinSettingsStore(SystemControl.Platform.UserRegistry).OpenGameKey(type, gameExecutablePath);
    }
}
