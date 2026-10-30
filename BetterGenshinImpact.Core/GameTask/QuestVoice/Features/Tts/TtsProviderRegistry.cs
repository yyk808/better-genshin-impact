using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;

/// <summary>
/// 集中注册内置引擎；不动态加载 DLL，不持有活动会话。
/// 依赖操作系统的引擎（Windows SAPI、Linux espeak-ng 等）由宿主启动时通过 <see cref="Register"/> 注册实现，
/// 未注册实现的平台引擎不出现在列表中，按 ID 创建时报配置错误
/// </summary>
public static class TtsProviderRegistry
{
    /// <summary>Windows SAPI 引擎的描述；实现位于 WPF 主程序，由其模块初始化时注册。</summary>
    public static readonly TtsProviderInfo WindowsSapiInfo = new("windows-sapi", "Windows 系统语音（SAPI）", new(PresetVoices: true, Rate: true),
        "使用已安装的SAPI语音，无需账号/联网/模型；先刷新列表并选择声音。");

    private static readonly object Gate = new();

    /// <summary>每个条目提供描述和每轮独立实例的构造函数；平台引擎的构造函数在宿主注册前为 null。</summary>
    private static readonly List<(TtsProviderInfo Info, Func<ITtsProvider>? Create)> Entries =
    [
        (new("gpt-sovits", "GPT-SoVITS", new(true, true, true, false, true),
            "本地或远程 api_v2；完整/流式合成，沿用安全切权与取消排空。"),
            () => new GptSovitsEngine(CreateHttpClient, new ReferenceAudioSelector(), new WavProcessor())),
        (new("huoshan", "火山翻译（在线预设音色）", new(PresetVoices: true),
            "无需账号或模型；发送对白文本到火山翻译，完整接收 MP3 后播放。非公开端点可能变更或限流。"),
            () => new HuoshanTtsProvider(CreateHttpClient)),
        (WindowsSapiInfo, null),
        (new("voicevox", "VOICEVOX", new(ServerConnection: true, PresetVoices: true, Rate: true, Pitch: true),
            "连接用户已有的VOICEVOX引擎；speaker/style、两阶段查询/完整WAV；遵守声音条款及署名。"), () => new VoicevoxTtsProvider(CreateSpeechHttpClient)),
        (new("vits-simple-api", "vits-simple-api", new(ServerConnection: true, PresetVoices: true, Rate: true, Language: true, ApiKey: true),
            "连接已有VITS/BERT-VITS2服务；声音为模型类型:id，完整WAV；不安装模型。"), () => new VitsSimpleApiTtsProvider(CreateSpeechHttpClient)),
        (new("openai-speech", "OpenAI 兼容语音 API", new(ServerConnection: true, PresetVoices: true, Rate: true, Model: true, ApiKey: true, AudioFormat: true),
            "用户指定Base URL（例如含/v1/）、可选Key和模型；内置声音为建议，可手输兼容服务ID。完整接收后播放。"), () => new OpenAiSpeechTtsProvider(CreateSpeechHttpClient)),
        (new("google-translate", "Google Translate TTS（在线）", new(PresetVoices: true, Rate: true),
            "非Cloud官方承诺接口；按语言每100字符分段，MP3解码后顺序合并。需网络可达，速度仅1或0.5。"), () => new GoogleTranslateTtsProvider(CreateSpeechHttpClient))
    ];

    private static IReadOnlyList<TtsProviderInfo>? _providers;

    /// <summary>页面可读列表（只含已有实现的引擎），不暴露注册表的修改权限。</summary>
    public static IReadOnlyList<TtsProviderInfo> Providers
    {
        get
        {
            lock (Gate)
            {
                return _providers ??= Array.AsReadOnly(Entries.Where(x => x.Create is not null).Select(x => x.Info).ToArray());
            }
        }
    }

    /// <summary>
    /// 宿主注册平台引擎的实现。ID 已在列表中（如 windows-sapi）时只补上实现、保持原有顺序；
    /// 新 ID 追加到末尾。重复注册以最后一次为准
    /// </summary>
    public static void Register(TtsProviderInfo info, Func<ITtsProvider> create)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(create);
        lock (Gate)
        {
            var index = Entries.FindIndex(x => x.Info.Id == info.Id);
            if (index >= 0) Entries[index] = (info, create);
            else Entries.Add((info, create));
            _providers = null;
        }
    }

    /// <summary>引擎是否已有可用实现。</summary>
    public static bool IsAvailable(string id)
    {
        lock (Gate) return Entries.Any(x => x.Info.Id == id && x.Create is not null);
    }

    /// <summary>拒绝未知引擎，不能静默切换现有用户。已知但当前平台没有实现的引擎仍返回描述，便于读取旧配置。</summary>
    public static TtsProviderInfo GetInfo(string id)
    {
        lock (Gate)
        {
            return Entries.FirstOrDefault(x => x.Info.Id == id).Info
                ?? throw new VoiceException(VoiceErrorCode.Configuration, "未知 TTS 引擎：" + id);
        }
    }

    /// <summary>由调用者在合成完全结束后释放本轮引擎。</summary>
    public static ITtsProvider Create(string id)
    {
        Func<ITtsProvider>? create;
        lock (Gate)
        {
            GetInfo(id);
            create = Entries.Single(x => x.Info.Id == id).Create;
        }

        return create?.Invoke() ?? throw new VoiceException(VoiceErrorCode.Configuration, "当前平台不支持该 TTS 引擎：" + id);
    }
    /// <summary>新增HTTP引擎不跟随重定向，避免自定义认证头被转发到未配置的地址。</summary>
    private static HttpClient CreateSpeechHttpClient(TimeoutSettings timeouts) => new(new SocketsHttpHandler
        { ConnectTimeout = TimeSpan.FromSeconds(timeouts.ConnectSeconds), AllowAutoRedirect = false });
    /// <summary>所有 HTTP 客户端显式使用连接超时，保留系统代理。</summary>
    private static HttpClient CreateHttpClient(TimeoutSettings timeouts) => new(new SocketsHttpHandler
        { ConnectTimeout = TimeSpan.FromSeconds(timeouts.ConnectSeconds) });
}
