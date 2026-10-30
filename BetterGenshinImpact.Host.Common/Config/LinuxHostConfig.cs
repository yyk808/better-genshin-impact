using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using BetterGenshinImpact.Service;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;

namespace BetterGenshinImpact.Host.Common.Config;

/// <summary>
/// Linux 宿主专用的设置（<c>User/linux-host.json</c>）。单独成文件，不改动与 Windows 共用的 config.json 的结构：
/// 截图方式（X11）、同时启动游戏的命令（Wine / Proton 等）
/// </summary>
public sealed partial class LinuxHostConfig : ObservableObject
{
    /// <summary>X11 截图方式：ShmRoot / GetImageRoot / GetImageWindow</summary>
    [ObservableProperty] private string _captureMode = "ShmRoot";

    /// <summary>启动截图器时，如果找不到游戏窗口，先启动游戏（对应 Windows 的“同时启动原神”）</summary>
    [ObservableProperty] private bool _launchGameEnabled;

    /// <summary>
    /// 启动游戏的命令行，经 <c>/bin/sh -c</c> 执行；<c>{game}</c> 替换为加引号的游戏路径。
    /// 例：<c>wine {game}</c>、<c>WINEPREFIX=~/.wine-genshin wine {game}</c>、<c>umu-run {game}</c>、<c>steam -applaunch 1234</c>
    /// </summary>
    [ObservableProperty] private string _launchCommand = "wine {game}";

    /// <summary>游戏可执行文件路径（YuanShen.exe / GenshinImpact.exe），替换命令中的 {game}</summary>
    [ObservableProperty] private string _gamePath = "";

    /// <summary>工作目录；为空时用游戏所在目录</summary>
    [ObservableProperty] private string _workingDirectory = "";

    /// <summary>附加环境变量，每行一个 KEY=VALUE（如 WINEPREFIX、PROTONPATH、GAMEID）</summary>
    [ObservableProperty] private string _environment = "";

    /// <summary>启动后等待游戏窗口出现的最长时间（秒）</summary>
    [ObservableProperty] private int _launchTimeoutSeconds = 90;

    /// <summary>预置的启动命令（界面下拉框用）</summary>
    [JsonIgnore]
    public static IReadOnlyList<LaunchPreset> Presets { get; } =
    [
        new("Wine", "wine {game}", "系统 Wine；前缀用环境变量 WINEPREFIX 指定"),
        new("Proton（umu-run）", "umu-run {game}", "umu-launcher 运行 Proton；可设置 PROTONPATH、GAMEID、WINEPREFIX"),
        new("Steam", "steam -applaunch <AppID>", "游戏已添加到 Steam（非 Steam 游戏的 AppID 可在 Steam 快捷方式属性中查看）"),
        new("Lutris", "lutris lutris:rungame/<游戏标识>", "游戏由 Lutris 管理"),
    ];
}

public sealed record LaunchPreset(string Name, string Command, string Description);

/// <summary>读写 <see cref="LinuxHostConfig"/>，改动防抖保存</summary>
public sealed class LinuxHostConfigStore : IDisposable
{
    public const string DefaultRelativePath = "User/linux-host.json";

    private readonly JsonFileStore _store;

    public LinuxHostConfigStore(string path, ILogger log)
    {
        LinuxHostConfig? config = null;
        try
        {
            if (File.Exists(path))
            {
                config = JsonSerializer.Deserialize<LinuxHostConfig>(File.ReadAllText(path), ConfigJson.Options);
            }
        }
        catch (Exception e)
        {
            var backup = ConfigBackup.Backup(path, "linux-host");
            log.Error(e, "Linux 宿主设置读取失败，使用默认设置（原文件已备份到 {Backup}）", backup);
        }

        Config = config ?? new LinuxHostConfig();
        _store = new JsonFileStore(path, () => JsonSerializer.Serialize(Config, ConfigJson.Options), log);
        Config.PropertyChanged += OnChanged;
        if (config is null)
        {
            // 首次运行或读取失败：写出默认设置，便于用户查看和编辑
            _store.SaveNow();
        }
    }

    public LinuxHostConfig Config { get; }

    public JsonFileStore Store => _store;

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => _store.RequestSave();

    public void Dispose()
    {
        Config.PropertyChanged -= OnChanged;
        _store.Dispose();
    }
}
