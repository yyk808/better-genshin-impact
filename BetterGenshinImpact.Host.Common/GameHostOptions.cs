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

    /// <summary>触发器调度间隔（毫秒）；为空时沿用配置（默认 50）</summary>
    public int? IntervalMs { get; init; }

    /// <summary>自动拾取开关；为空时沿用配置</summary>
    public bool? Pick { get; init; }

    /// <summary>自动剧情开关；为空时沿用配置</summary>
    public bool? Skip { get; init; }

    /// <summary>
    /// 读取并保存配置文件（User/config.json 与 User/linux-host.json，格式与 Windows 主程序相同）。
    /// 关闭时只用内存中的默认配置（无界面宿主默认关闭，便于得到可重复的测试结果）
    /// </summary>
    public bool PersistConfig { get; init; }

    /// <summary>配置文件路径；为空时为素材目录下的 User/config.json。Linux 宿主设置保存在同目录的 linux-host.json</summary>
    public string? ConfigFile { get; init; }

    /// <summary>自动开门（自动进入游戏）；为空时沿用配置默认值</summary>
    public bool? AutoEnterGame { get; init; }

    /// <summary>截图方式（ShmRoot / GetImageRoot / GetImageWindow / CompositeWindow），为空时用 linux-host.json 中的设置</summary>
    public string? CaptureMode { get; init; }

    /// <summary>遮罩窗口开关；为空时用 linux-host.json 中的设置</summary>
    public bool? MaskWindow { get; init; }

    /// <summary>遮罩上显示全部识别结果（调试）；为空时用 linux-host.json 中的设置</summary>
    public bool? MaskDrawAllRecognition { get; init; }

    /// <summary>
    /// 解析宿主共用的命令行参数（--display、--assets、--process、--interval、--pick / --no-pick、--skip / --no-skip、
    /// --auto-enter / --no-auto-enter、--config 文件、--save-config / --no-save-config、--capture 截图方式、--mask / --no-mask、--mask-recognition）。
    /// 这些开关只在本次运行有效，不写回配置文件；
    /// 不认识的参数返回 null，由宿主自己处理
    /// </summary>
    public GameHostOptions? Apply(string argument, Func<string> nextValue) => argument switch
    {
        "--display" => this with { Display = nextValue() },
        "--assets" => this with { AssetsDirectory = nextValue() },
        "--process" => this with { ProcessNames = nextValue().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
        "--interval" => this with { IntervalMs = int.Parse(nextValue()) },
        "--pick" => this with { Pick = true },
        "--no-pick" => this with { Pick = false },
        "--skip" => this with { Skip = true },
        "--no-skip" => this with { Skip = false },
        "--config" => this with { ConfigFile = nextValue(), PersistConfig = true },
        "--save-config" => this with { PersistConfig = true },
        "--no-save-config" => this with { PersistConfig = false },
        "--capture" => this with { CaptureMode = nextValue() },
        "--mask" => this with { MaskWindow = true },
        "--no-mask" => this with { MaskWindow = false },
        "--mask-recognition" => this with { MaskDrawAllRecognition = true },
        "--auto-enter" => this with { AutoEnterGame = true },
        "--no-auto-enter" => this with { AutoEnterGame = false },
        _ => null,
    };
}
