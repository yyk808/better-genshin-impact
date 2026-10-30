namespace BetterGenshinImpact.Host.Common;

/// <summary>宿主启动参数（命令行或界面设置）</summary>
public sealed record GameHostOptions
{
    /// <summary>X 显示（如 :0），为空时用环境变量 DISPLAY</summary>
    public string? Display { get; init; }

    /// <summary>素材、模型、User 目录的根目录（Windows 主程序的输出目录）；为空时用程序所在目录</summary>
    public string? AssetsDirectory { get; init; }

    /// <summary>游戏进程名</summary>
    public IReadOnlyList<string> ProcessNames { get; init; } = ["YuanShen", "GenshinImpact"];

    /// <summary>触发器调度间隔（毫秒）</summary>
    public int IntervalMs { get; init; } = 50;

    /// <summary>自动拾取初始开关</summary>
    public bool Pick { get; init; } = true;

    /// <summary>自动剧情初始开关</summary>
    public bool Skip { get; init; } = true;

    /// <summary>自动开门（自动进入游戏）；为空时沿用配置默认值</summary>
    public bool? AutoEnterGame { get; init; }
}
