using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using BetterGenshinImpact.Platform.Linux.Audio;

namespace BetterGenshinImpact.Core.UnitTest.Platform;

/// <summary>
/// Linux 按进程捕获音频（pactl 找流 + parecord --monitor-stream）：解析、选流、PCM 转换、按时钟补静音、流结束后重新查找。
/// 不启动 pactl / parecord，用假列表和假数据流
/// </summary>
public class PulseProcessAudioCaptureTests
{
    private const string Sample = """
        Sink Input #41
        	Driver: protocol-native.c
        	Owner Module: 9
        	Client: 40
        	Sink: 0
        	Corked: no
        	Mute: no
        	Properties:
        		application.name = "YuanShen.exe"
        		application.process.id = "4242"
        		application.process.binary = "YuanShen.exe"
        		media.name = "audio stream"

        Sink Input #57
        	Corked: yes
        	Properties:
        		application.name = "Firefox"
        		application.process.id = "777"
        		application.process.binary = "firefox"

        Sink Input #60
        	Corked: no
        	Properties:
        		application.name = "Firefox"
        		application.process.id = "777"
        		application.process.binary = "firefox"
        """;

    [Fact]
    public void Parse_ReadsIndexPidBinaryCorked()
    {
        var list = PulseProcessAudioCapture.ParseSinkInputs(Sample.Replace("\r", ""));

        Assert.Equal(3, list.Count);
        Assert.Equal(new PulseSinkInput(41, 4242, "YuanShen.exe", "YuanShen.exe", false), list[0]);
        Assert.Equal(new PulseSinkInput(57, 777, "firefox", "Firefox", true), list[1]);
        Assert.Equal(60, list[2].Index);
    }

    [Fact]
    public void Select_ByPid_ThenByName_PrefersUncorkedNewest()
    {
        var list = PulseProcessAudioCapture.ParseSinkInputs(Sample.Replace("\r", ""));

        Assert.Equal(41, PulseProcessAudioCapture.SelectStream(list, 4242, null)?.Index);
        Assert.Equal(60, PulseProcessAudioCapture.SelectStream(list, 777, null)?.Index);
        // pid 对不上（如容器里的 pid 命名空间）时按程序名
        Assert.Equal(41, PulseProcessAudioCapture.SelectStream(list, 1, "yuanshen.exe")?.Index);
        Assert.Null(PulseProcessAudioCapture.SelectStream(list, 1, null));
        Assert.Null(PulseProcessAudioCapture.SelectStream([], 4242, "YuanShen.exe"));
    }

    private static bool WaitUntil(Func<bool> condition, int ms = 3000)
    {
        var end = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < end)
        {
            if (condition()) return true;
            Thread.Sleep(20);
        }

        return condition();
    }

    [Fact]
    public void Stream_ConvertsS16ToFloat_AndRescansAfterEnd()
    {
        var opened = new List<int>();
        var pcm = new byte[] { 0x00, 0x40, 0x00, 0xC0, 0xFF, 0x7F, 0x00, 0x80 }; // 16384, -16384, 32767, -32768
        long now = 0;
        using var capture = new PulseProcessAudioCapture(4242, "YuanShen.exe",
            () => [new PulseSinkInput(41, 4242, "YuanShen.exe", "YuanShen.exe", false)],
            idx =>
            {
                lock (opened) opened.Add(idx);
                return new MemoryStream(pcm);
            },
            () => Interlocked.Read(ref now));

        var got = new List<float>();
        Assert.True(WaitUntil(() =>
        {
            capture.ReadAvailableSamples(got);
            return got.Count >= 4;
        }));
        Assert.Equal([0.5f, -0.5f, 32767 / 32768f, -1f], got.GetRange(0, 4));
        Assert.True(WaitUntil(() => { lock (opened) return opened.Count >= 2; }), "流结束后应重新查找并再次打开");
        Assert.All(opened, i => Assert.Equal(41, i));
    }

    [Fact]
    public void NoStream_PadsSilenceByClock_LikeWasapiLoopback()
    {
        long now = 1000;
        using var capture = new PulseProcessAudioCapture(4242, null, () => [], _ => throw new IOException("不应打开"), () => Interlocked.Read(ref now));

        var got = new List<float>();
        capture.ReadAvailableSamples(got);
        Assert.Empty(got);

        Interlocked.Exchange(ref now, 1500); // 0.5 秒
        capture.ReadAvailableSamples(got);
        Assert.Equal(8000, got.Count);
        Assert.All(got, s => Assert.Equal(0f, s));

        Interlocked.Exchange(ref now, 1600);
        capture.DiscardAvailableSamples();
        got.Clear();
        Interlocked.Exchange(ref now, 1700);
        capture.ReadAvailableSamples(got);
        Assert.Equal(1600, got.Count);
        Assert.Null(capture.CurrentSinkInput);
    }

    [Fact]
    public void Dispose_StopsQuickly()
    {
        long now = 0;
        var capture = new PulseProcessAudioCapture(4242, null, () => [], _ => throw new IOException(), () => now);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        capture.Dispose();
        Assert.True(sw.ElapsedMilliseconds < 1500, $"{sw.ElapsedMilliseconds} ms");
    }
}
