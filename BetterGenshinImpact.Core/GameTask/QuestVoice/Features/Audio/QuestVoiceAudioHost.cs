using System;
using System.IO;
using NAudio.Wave;
using NLayer.NAudioSupport;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Audio;

/// <summary>
/// 剧情语音的平台音频能力入口，由宿主程序在启动时设置：
/// Windows 主程序设为 WASAPI 共享输出与 NAudio 的 Mp3FileReader（ACM 解码），与迁移前完全相同；
/// 其它平台默认用纯托管的 NLayer 解码 MP3，输出为不出声、立即结束的空实现。
/// </summary>
public static class QuestVoiceAudioHost
{
    /// <summary>创建一个新的默认输出设备</summary>
    public static Func<IWavePlayer> OutputFactory { get; set; } = static () => new SilentWavePlayer();

    /// <summary>从完整的 MP3 字节流创建 PCM 读取器</summary>
    public static Func<Stream, WaveStream> Mp3ReaderFactory { get; set; } = CreateManagedMp3Reader;

    /// <summary>
    /// 纯托管 MP3 解码（NLayer），不依赖系统编解码器。
    /// NLayer 输出 32 位浮点，这里转换为 16 位 PCM，与 Windows 上 Mp3FileReader（ACM）的输出格式一致。
    /// </summary>
    public static WaveStream CreateManagedMp3Reader(Stream input)
        => new Wave32To16Stream(new Mp3FileReaderBase(input, static format => new Mp3FrameDecompressor(format)));
}

/// <summary>
/// 不出声的输出：Play 时不读取音频，直接以「正常结束」触发 PlaybackStopped。
/// 用于尚未接入系统音频输出的平台，使配音管线可以照常推进。
/// </summary>
public sealed class SilentWavePlayer : IWavePlayer
{
    public float Volume { get; set; } = 1f;

    public PlaybackState PlaybackState { get; private set; } = PlaybackState.Stopped;

    public WaveFormat OutputWaveFormat { get; private set; } = new();

    public event EventHandler<StoppedEventArgs>? PlaybackStopped;

    public void Init(IWaveProvider waveProvider) => OutputWaveFormat = waveProvider.WaveFormat;

    public void Play()
    {
        PlaybackState = PlaybackState.Stopped;
        PlaybackStopped?.Invoke(this, new StoppedEventArgs());
    }

    public void Stop()
    {
        PlaybackState = PlaybackState.Stopped;
    }

    public void Pause()
    {
        PlaybackState = PlaybackState.Paused;
    }

    public void Dispose()
    {
    }
}
