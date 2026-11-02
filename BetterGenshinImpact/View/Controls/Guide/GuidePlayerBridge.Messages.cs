using System;
using System.IO;
using System.Text;
using BetterGenshinImpact.Service.Guide;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.View.Controls.Guide;

/// <summary>
/// 播放桥与网页引擎无关的部分：注入的页面脚本、页面消息解析、命令名。Windows（WebView2）与 Linux（WebKitGTK）共用
/// </summary>
public sealed partial class GuidePlayerBridge
{
    /// <summary>
    /// 页面脚本资源，按顺序注入：先登记站点适配，再运行播放桥
    /// </summary>
    private static readonly string[] ScriptResources =
    [
        "BetterGenshinImpact.Resources.JavaScript.guide-site-bilibili.js",
        "BetterGenshinImpact.Resources.JavaScript.guide-bridge.js",
    ];

    /// <summary>
    /// 页面脚本内容，进程内只读取一次
    /// </summary>
    private static readonly Lazy<string[]> Scripts = new(() => Array.ConvertAll(ScriptResources, ReadResource));

    /// <inheritdoc />
    public GuidePlayerState? State { get; private set; }

    /// <inheritdoc />
    public event EventHandler<GuidePlayerState>? StateChanged;

    /// <inheritdoc />
    public event EventHandler<string>? SubtitleChanged;

    /// <inheritdoc />
    public event EventHandler<GuidePlayerEvent>? EventRaised;

    /// <summary>
    /// 命令对应的页面脚本名称
    /// </summary>
    private static string CommandName(GuidePlayerCommand command) => command switch
    {
        GuidePlayerCommand.Play => "play",
        GuidePlayerCommand.Pause => "pause",
        GuidePlayerCommand.Toggle => "toggle",
        GuidePlayerCommand.SeekBy => "seekBy",
        GuidePlayerCommand.SeekTo => "seekTo",
        GuidePlayerCommand.SetRate => "setRate",
        GuidePlayerCommand.SetVolume => "setVolume",
        GuidePlayerCommand.PrevEpisode => "prevEpisode",
        GuidePlayerCommand.NextEpisode => "nextEpisode",
        GuidePlayerCommand.SetImmersive => "setImmersive",
        GuidePlayerCommand.SetAutoSubtitle => "setAutoSubtitle",
        GuidePlayerCommand.SetPageSubtitleHidden => "setPageSubtitleHidden",
        GuidePlayerCommand.SetDanmaku => "setDanmaku",
        _ => "requestState",
    };

    /// <summary>
    /// 解析页面发来的消息（JSON）并触发对应事件
    /// </summary>
    private void HandleMessage(string json)
    {
        JObject message;
        try
        {
            message = JObject.Parse(json);
        }
        catch (JsonException)
        {
            return;
        }

        switch (message.Value<string?>("type"))
        {
            case "state":
                State = ParseState(message);
                StateChanged?.Invoke(this, State);
                break;
            case "subtitle":
                var text = message.Value<string?>("text") ?? string.Empty;
                SubtitleChanged?.Invoke(this, text.Length > 500 ? text[..500] : text);
                break;
            case "event":
                if (ParseEvent(message.Value<string?>("name")) is { } kind)
                {
                    EventRaised?.Invoke(this, new GuidePlayerEvent(kind));
                }

                break;
        }
    }

    /// <summary>
    /// 解析状态消息
    /// </summary>
    private static GuidePlayerState ParseState(JObject message)
    {
        var href = Limit(message.Value<string?>("href"), 2048);
        var hasVideo = message.Value<bool?>("hasVideo") ?? false;
        var key = Limit(message.Value<string?>("videoKey"), 512);
        // B 站由站点适配给出标识；其他网站按网址计算，与收藏、观看记录的规则一致
        if (hasVideo && (string.IsNullOrEmpty(key) || !key.StartsWith("bilibili:", StringComparison.Ordinal)))
        {
            key = GuideLinkResolver.GetVideoKey(href);
        }

        return new GuidePlayerState
        {
            Href = href,
            Title = Limit(message.Value<string?>("title"), 300),
            Site = Limit(message.Value<string?>("site"), 32),
            VideoKey = hasVideo || key?.StartsWith("bilibili:", StringComparison.Ordinal) == true ? key : null,
            HasVideo = hasVideo,
            Paused = message.Value<bool?>("paused") ?? true,
            CurrentTime = Number(message, "currentTime", 0, 1e7),
            Duration = Number(message, "duration", 0, 1e7),
            PlaybackRate = Number(message, "playbackRate", 0.05, 16, 1),
            Volume = Number(message, "volume", 0, 1, 1),
            VideoWidth = Number(message, "videoWidth", 0, 20000),
            VideoHeight = Number(message, "videoHeight", 0, 20000),
            Immersive = message.Value<bool?>("immersive") ?? false,
            EpisodeIndex = (int)Number(message, "episodeIndex", 0, 100000),
            EpisodeCount = (int)Number(message, "episodeCount", 0, 100000),
            HasPrevEpisode = message.Value<bool?>("hasPrev") ?? false,
            HasNextEpisode = message.Value<bool?>("hasNext") ?? false,
            Danmaku = message["danmaku"]?.Type == JTokenType.Boolean ? message.Value<bool>("danmaku") : null,
            ReceivedAt = Environment.TickCount64,
        };
    }

    /// <summary>
    /// 读取数值并夹到范围内，缺失或非法时使用默认值
    /// </summary>
    private static double Number(JObject message, string name, double min, double max, double fallback = 0)
    {
        var token = message[name];
        if (token?.Type is not (JTokenType.Float or JTokenType.Integer))
        {
            return fallback;
        }

        var value = token.Value<double>();
        return double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    }

    /// <summary>
    /// 截断过长的字符串
    /// </summary>
    private static string Limit(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length > max ? value[..max] : value;

    /// <summary>
    /// 页面事件名转为事件类型，未知事件忽略
    /// </summary>
    private static GuidePlayerEventKind? ParseEvent(string? name) => name switch
    {
        "ready" => GuidePlayerEventKind.Ready,
        "videoChanged" => GuidePlayerEventKind.VideoChanged,
        "ended" => GuidePlayerEventKind.Ended,
        "playRejected" => GuidePlayerEventKind.PlayRejected,
        "editFocus" => GuidePlayerEventKind.EditFocus,
        "editBlur" => GuidePlayerEventKind.EditBlur,
        "immersiveExited" => GuidePlayerEventKind.ImmersiveExited,
        _ => null,
    };

    /// <summary>
    /// 读取嵌入的页面脚本
    /// </summary>
    private static string ReadResource(string name)
    {
        using var stream = typeof(GuidePlayerBridge).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"找不到嵌入资源 {name}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
