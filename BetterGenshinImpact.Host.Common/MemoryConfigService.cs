using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;

namespace BetterGenshinImpact.Host.Common;

/// <summary>只在内存中保存配置的配置服务（宿主暂不写回配置文件）</summary>
public sealed class MemoryConfigService : IConfigService
{
    private AllConfig _config;

    public MemoryConfigService(AllConfig config)
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
