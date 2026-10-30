using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;

namespace BetterGenshinImpact.Host.Headless;

/// <summary>只在内存中保存配置的配置服务（无界面宿主不写回配置文件）</summary>
public sealed class HeadlessConfigService : IConfigService
{
    private AllConfig _config;

    public HeadlessConfigService(AllConfig config)
    {
        _config = config;
        ConfigHost.Config = config;
    }

    public AllConfig Get() => _config;

    public void Save()
    {
    }

    public void Flush()
    {
    }

    public AllConfig Read() => _config;

    public void Write(AllConfig config)
    {
        _config = config;
        ConfigHost.Config = config;
    }
}
