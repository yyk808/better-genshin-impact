using System.Diagnostics;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;

namespace BetterGenshinImpact.Platform.Linux.Tts;

/// <summary>
/// Linux 系统语音：调用本机的 espeak-ng（--stdout 输出 WAV，在内存中重新封装为 PCM16 WAV）。
/// 无需账号/联网/模型；未安装时不注册到引擎列表（<see cref="TryRegister"/> 返回 false），剧情语音照常静默运行
/// </summary>
public sealed class EspeakNgTtsProvider : ITtsProvider
{
    public const string ProviderId = "espeak-ng";

    /// <summary>引擎描述，追加在内置引擎之后</summary>
    public static readonly TtsProviderInfo ProviderInfo = new(ProviderId, "Linux 系统语音（espeak-ng）", new(PresetVoices: true, Rate: true),
        "调用本机已安装的 espeak-ng，无需账号/联网/模型；声音为 espeak-ng 语言/声音名（如 cmn、en、ja）。音质为合成机器音。");

    private const int MaxPcmBytes = 32 * 1024 * 1024;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly string _executable;

    public EspeakNgTtsProvider(string executable)
    {
        _executable = executable;
    }

    public TtsProviderInfo Info => TtsProviderRegistry.GetInfo(ProviderId);

    /// <summary>引擎独立配置的缺省值：普通话声音、1 倍语速</summary>
    public static BetterGenshinImpact.Core.Config.QuestVoiceEngineConfig DefaultConfig() => new() { VoiceId = "cmn", Language = "cmn" };

    /// <summary>在 PATH 中查找 espeak-ng（或旧版 espeak）；找不到返回 null</summary>
    public static string? FindExecutable()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "/usr/local/bin:/usr/bin:/bin";
        foreach (var name in new[] { "espeak-ng", "espeak" })
        {
            foreach (var dir in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>找到 espeak-ng 时注册到引擎表并返回 true；否则不注册（界面不出现该引擎）</summary>
    public static bool TryRegister()
    {
        if (FindExecutable() is not { } exe)
        {
            return false;
        }

        TtsProviderRegistry.Register(ProviderInfo, () => new EspeakNgTtsProvider(exe));
        return true;
    }

    public void Validate(AppSettings settings)
    {
        var rate = settings.Extra?.Rate ?? 1;
        if (!double.IsFinite(rate) || rate is < 0.5 or > 2)
            throw Configuration("espeak-ng 语速倍率必须为 0.5～2。");
        if (settings.Timeouts.SynthesisSeconds is < 1 or > 600)
            throw Configuration("espeak-ng 合成超时应为 1～600 秒。");
    }

    public async Task<IReadOnlyList<TtsVoiceInfo>> GetVoicesAsync(AppSettings settings, CancellationToken token)
    {
        var (output, _) = await RunAsync(["--voices"], null, token).ConfigureAwait(false);
        var text = System.Text.Encoding.UTF8.GetString(output);
        var result = new List<TtsVoiceInfo>();
        // 列：Pty Language Age/Gender VoiceName File Other Languages
        foreach (var line in text.Split('\n').Skip(1))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 5)
            {
                result.Add(new TtsVoiceInfo(parts[1], $"{parts[3]}（{parts[1]}）", parts[1]));
            }
        }

        return result;
    }

    public async Task<string> ProbeAsync(AppSettings settings, CancellationToken token)
    {
        Validate(settings);
        var (output, _) = await RunAsync(["--version"], null, token).ConfigureAwait(false);
        return "espeak-ng 已就绪：" + System.Text.Encoding.UTF8.GetString(output).Trim();
    }

    public async Task<TtsResult> SynthesizeAsync(AppSettings settings, TtsRequest request, IProgress<TtsPhase>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(settings);
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 2000)
            throw Configuration("espeak-ng 合成台词长度必须为 1～2000 字符。");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        linked.CancelAfter(TimeSpan.FromSeconds(settings.Timeouts.SynthesisSeconds));
        var voice = string.IsNullOrWhiteSpace(settings.Provider?.VoiceId) ? "cmn" : settings.Provider!.VoiceId;
        // espeak-ng 默认 175 词/分钟
        var speed = (int)Math.Round(175 * (settings.Extra?.Rate ?? 1));
        var watch = Stopwatch.StartNew();
        progress?.Report(TtsPhase.Synthesizing);
        try
        {
            // 台词经标准输入传入，不作为命令行参数，避免以 - 开头的文本被当作选项
            var (wav, _) = await RunAsync(["--stdout", "-v", voice, "-s", speed.ToString(System.Globalization.CultureInfo.InvariantCulture)], request.Text, linked.Token)
                .ConfigureAwait(false);
            var (pcm, sampleRate, channels) = ExtractPcm16(wav);
            if (pcm.Length == 0 || pcm.Length > MaxPcmBytes)
                throw new VoiceException(VoiceErrorCode.InvalidAudio, "espeak-ng 返回的音频为空或过大。");
            var audio = new WavProcessor().ValidateCompletedResponse(WrapPcm(pcm, sampleRate, channels));
            return new(audio.Bytes, watch.Elapsed.TotalSeconds, watch.Elapsed.TotalSeconds, audio.DurationSeconds, audio.SampleRate, audio.Channels, ReferenceFallback.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _shutdown.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken.IsCancellationRequested ? cancellationToken : _shutdown.Token);
        }
        catch (OperationCanceledException exception)
        {
            throw new VoiceException(VoiceErrorCode.Timeout, "espeak-ng 合成超时。", false, exception);
        }
    }

    /// <summary>从 espeak-ng 的 WAV 输出中取出 PCM16 数据（输出到管道时首部的长度字段可能是占位值，这里按实际长度截取）</summary>
    internal static (byte[] Pcm, int SampleRate, int Channels) ExtractPcm16(byte[] wav)
    {
        if (wav.Length < 12 || wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F')
            throw new VoiceException(VoiceErrorCode.InvalidAudio, "espeak-ng 输出不是 WAV。");
        int sampleRate = 22050, channels = 1, bits = 16;
        var offset = 12;
        while (offset + 8 <= wav.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(wav, offset, 4);
            var size = BitConverter.ToInt32(wav, offset + 4);
            var body = offset + 8;
            if (id == "fmt " && body + 16 <= wav.Length)
            {
                channels = BitConverter.ToInt16(wav, body + 2);
                sampleRate = BitConverter.ToInt32(wav, body + 4);
                bits = BitConverter.ToInt16(wav, body + 14);
            }
            else if (id == "data")
            {
                if (bits != 16)
                    throw new VoiceException(VoiceErrorCode.InvalidAudio, "espeak-ng 输出不是 16 位 PCM。");
                var length = size <= 0 || body + (long)size > wav.Length ? wav.Length - body : size;
                length -= length % (2 * Math.Max(1, channels));
                return (wav.AsSpan(body, length).ToArray(), sampleRate, channels);
            }

            if (size < 0) break;
            offset = body + size + (size & 1);
        }

        throw new VoiceException(VoiceErrorCode.InvalidAudio, "espeak-ng 输出缺少音频数据。");
    }

    private static byte[] WrapPcm(byte[] pcm, int sampleRate, int channels)
    {
        using var memory = new MemoryStream(pcm.Length + 44);
        using var writer = new BinaryWriter(memory);
        writer.Write("RIFF"u8);
        writer.Write(pcm.Length + 36);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2);
        writer.Write((short)(channels * 2));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(pcm.Length);
        writer.Write(pcm);
        return memory.ToArray();
    }

    private async Task<(byte[] Output, string Error)> RunAsync(string[] args, string? stdin, CancellationToken token)
    {
        var start = new ProcessStartInfo(_executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
        };
        foreach (var a in args) start.ArgumentList.Add(a);
        using var process = Process.Start(start) ?? throw Configuration("无法启动 espeak-ng。");
        try
        {
            if (stdin is not null)
            {
                await process.StandardInput.WriteAsync(stdin.AsMemory(), token).ConfigureAwait(false);
                process.StandardInput.Close();
            }

            using var output = new MemoryStream();
            var copy = process.StandardOutput.BaseStream.CopyToAsync(output, token);
            var error = process.StandardError.ReadToEndAsync(token);
            await Task.WhenAll(copy, error, process.WaitForExitAsync(token)).ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new VoiceException(VoiceErrorCode.Configuration, $"espeak-ng 退出码 {process.ExitCode}：{error.Result.Trim()}");
            return (output.ToArray(), error.Result);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            throw;
        }
    }

    public void Shutdown() => _shutdown.Cancel();

    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
    }

    private static VoiceException Configuration(string message) => new(VoiceErrorCode.Configuration, message);
}
