using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Audio;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Tts;
using NAudio.Wave;
using Xunit;

namespace BetterGenshinImpact.Core.UnitTest.GameTaskTests.QuestVoiceTests;

public class QuestVoiceAudioHostTests
{
    /// <summary>0.3 秒 440Hz 单声道 22050Hz、32kbps 的 MP3（由 ffmpeg 生成）</summary>
    private const string SineMp3Base64 = "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";

    [Fact]
    public void ManagedMp3Reader_DecodesToPcm16()
    {
        using var input = new MemoryStream(Convert.FromBase64String(SineMp3Base64), writable: false);
        using var reader = QuestVoiceAudioHost.CreateManagedMp3Reader(input);

        Assert.Equal(WaveFormatEncoding.Pcm, reader.WaveFormat.Encoding);
        Assert.Equal(16, reader.WaveFormat.BitsPerSample);
        Assert.Equal(22050, reader.WaveFormat.SampleRate);
        Assert.Equal(1, reader.WaveFormat.Channels);

        var buffer = new byte[4096];
        long total = 0;
        var peak = 0;
        int count;
        while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += count;
            for (var i = 0; i + 1 < count; i += 2)
            {
                peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(buffer, i)));
            }
        }

        // 0.3 秒约 6615 个采样（13230 字节），MP3 帧对齐会多出一些
        Assert.InRange(total, 12000, 20000);
        // 正弦波解码后应有明显振幅，而不是静音
        Assert.True(peak > 1000, $"peak={peak}");
    }

    [Fact]
    public void TtsAudioDecoder_DecodesMp3WithDefaultHost()
    {
        // Core 单测中没有 Windows 宿主初始化，走默认的 NLayer 解码
        var wav = new TtsAudioDecoder().Decode(Convert.FromBase64String(SineMp3Base64), TtsAudioEncoding.Mp3, CancellationToken.None);
        Assert.NotNull(wav);
    }

    [Fact]
    public async Task SilentWavePlayer_CompletesPlaybackImmediately()
    {
        using var player = new WasapiAudioPlayer(static () => new SilentWavePlayer());
        var wav = new MemoryStream();
        using (var writer = new WaveFileWriter(wav, new WaveFormat(16000, 16, 1)))
        {
            writer.Write(new byte[3200], 0, 3200);
        }

        await player.PlayAsync(wav.ToArray(), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    }
}
