using System.Text.Json;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Interface;
using Serilog;

namespace BetterGenshinImpact.Host.Common.Config;

/// <summary>
/// Linux 宿主的配置服务：读写 <c>User/config.json</c>，格式与 Windows 主程序相同
/// （同一个 <see cref="ConfigJson.Options"/>、同样的旧字段迁移），两边的配置文件可以互相拷贝使用。
/// 改动经 <see cref="AllConfig.OnAnyChangedAction"/> 登记，<see cref="JsonFileStore"/> 防抖后原子写盘
/// </summary>
public sealed class FileConfigService : IConfigService, IDisposable
{
    public const string DefaultRelativePath = "User/config.json";

    private readonly ILogger _log;
    private readonly Lock _initLock = new();
    private readonly JsonFileStore _store;
    private AllConfig? _config;

    public FileConfigService(string path, ILogger log)
    {
        _log = log;
        _store = new JsonFileStore(path, () => JsonSerializer.Serialize(_config!, ConfigJson.Options), log);
    }

    public string FilePath => _store.FilePath;

    public JsonFileStore Store => _store;

    /// <summary>读取时文件不存在（使用默认配置）</summary>
    public bool CreatedDefault { get; private set; }

    /// <summary>
    /// 取得全局配置实例（首次调用时从文件读取并开始追踪改动）。
    /// <paramref name="beforeTracking"/> 在开始追踪前执行，用于应用命令行覆盖等不应写回文件的修改
    /// </summary>
    public AllConfig Get() => Load(null);

    public AllConfig Load(Action<AllConfig>? beforeTracking)
    {
        lock (_initLock)
        {
            if (_config is null)
            {
                _config = Read();
                beforeTracking?.Invoke(_config);
                ConfigHost.Config = _config;
                _config.OnAnyChangedAction = _store.RequestSave;
                _config.InitEvent();
                if (CreatedDefault)
                {
                    // 首次运行写出默认配置，便于用户查看、编辑或拷贝到 Windows
                    _store.SaveNow();
                }
            }

            return _config;
        }
    }

    public void Save() => _store.SaveNow();

    public void Flush() => _store.Flush();

    public AllConfig Read()
    {
        var path = _store.FilePath;
        try
        {
            if (!File.Exists(path))
            {
                CreatedDefault = true;
                _log.Information("配置文件不存在，使用默认配置：{Path}", path);
                return Migrate(new AllConfig());
            }

            var config = JsonSerializer.Deserialize<AllConfig>(File.ReadAllText(path), ConfigJson.Options);
            _log.Information("已读取配置文件：{Path}", path);
            return Migrate(config ?? new AllConfig());
        }
        catch (Exception e)
        {
            var backup = ConfigBackup.Backup(path, "config");
            _log.Error(e, "配置文件读取失败，使用默认配置（原文件已备份到 {Backup}）", backup);
            return Migrate(new AllConfig());
        }
    }

    public void Write(AllConfig config)
    {
        if (ReferenceEquals(config, _config))
        {
            Save();
            return;
        }

        // 非当前实例：直接序列化写入（与 Windows 主程序语义相同）
        var other = new JsonFileStore(_store.FilePath, () => JsonSerializer.Serialize(config, ConfigJson.Options), _log);
        other.SaveNow();
        other.Dispose();
    }

    /// <summary>与 Windows 主程序 ConfigService.MigrateLegacyConfig 相同的旧字段迁移</summary>
    private static AllConfig Migrate(AllConfig config)
    {
        config.AutoPickConfig.MigrateLegacyConfig();
        config.AutoDomainConfig.MigrateLegacyResinUsage();
        config.AutoStygianOnslaughtConfig.MigrateLegacyResinUsage();
        config.AutoBossConfig.MigrateLegacyResinUsage();
        config.AutoLeyLineOutcropConfig.MigrateLegacyResinUsage();
        return config;
    }

    public void Dispose() => _store.Dispose();
}
