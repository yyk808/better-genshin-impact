namespace BetterGenshinImpact.Core.Config;

/// <summary>
/// 全局唯一的配置实例（原为 ConfigHost.Config 的存储位置）。任何线程都可以读写；
/// 宿主的配置服务（Windows：ConfigService）负责加载、保存，并在这里发布实例
/// </summary>
public static class ConfigHost
{
    public static AllConfig? Config { get; set; }
}
