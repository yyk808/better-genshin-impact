using BetterGenshinImpact.GameTask.QuestVoice.Features.Audio;
using NAudio.Wave;

namespace BetterGenshinImpact.Platform.Linux.Audio;

/// <summary>
/// 经 PulseAudio（或 PipeWire 的 pulse 兼容层）播放的 NAudio 输出，替代 Windows 上的 WasapiOut。
/// Play 不阻塞：在后台线程按约 20 ms 一块读取并写入（写入在缓冲满时阻塞），读完后等待播放完毕（drain）再触发 PlaybackStopped；
/// Stop 只置标志，由播放线程丢弃未播放的数据（flush）后触发 PlaybackStopped，与 WasapiOut 的语义一致。
/// 所有 libpulse-simple 调用都在播放线程上进行
/// </summary>
public sealed class PulseAudioWavePlayer : IWavePlayer
{
    private readonly string _appName;
    private IWaveProvider? _source;
    private Thread? _thread;
    private volatile bool _stopRequested;
    private volatile bool _paused;
    private readonly ManualResetEventSlim _resume = new(true);

    public PulseAudioWavePlayer(string appName = "BetterGI")
    {
        _appName = appName;
    }

    public float Volume { get; set; } = 1f;

    public PlaybackState PlaybackState { get; private set; } = PlaybackState.Stopped;

    public WaveFormat OutputWaveFormat => _source?.WaveFormat ?? new WaveFormat();

    public event EventHandler<StoppedEventArgs>? PlaybackStopped;

    /// <summary>把 WAVE 格式映射到 PulseAudio 采样格式；不支持时返回 null</summary>
    internal static PulseSimple.SampleFormat? MapFormat(WaveFormat format) => (format.Encoding, format.BitsPerSample) switch
    {
        (WaveFormatEncoding.Pcm, 8) => PulseSimple.SampleFormat.U8,
        (WaveFormatEncoding.Pcm, 16) => PulseSimple.SampleFormat.S16Le,
        (WaveFormatEncoding.Pcm, 32) => PulseSimple.SampleFormat.S32Le,
        (WaveFormatEncoding.IeeeFloat, 32) => PulseSimple.SampleFormat.Float32Le,
        (WaveFormatEncoding.Extensible, 16) => PulseSimple.SampleFormat.S16Le,
        _ => null,
    };

    public void Init(IWaveProvider waveProvider)
    {
        if (MapFormat(waveProvider.WaveFormat) is null)
        {
            throw new ArgumentException($"PulseAudio 输出不支持的音频格式：{waveProvider.WaveFormat}");
        }

        _source = waveProvider;
    }

    public void Play()
    {
        if (_source is null)
        {
            throw new InvalidOperationException("播放前需要先调用 Init");
        }

        if (PlaybackState == PlaybackState.Paused)
        {
            _paused = false;
            _resume.Set();
            PlaybackState = PlaybackState.Playing;
            return;
        }

        if (_thread is { IsAlive: true })
        {
            return;
        }

        // 先在调用线程上打开连接，失败时直接抛出（调用方据此报告“播放设备不可用”）
        var format = _source.WaveFormat;
        var spec = new PulseSimple.SampleSpec { Format = MapFormat(format)!.Value, Rate = (uint)format.SampleRate, Channels = (byte)format.Channels };
        // 服务器缺省的目标缓冲约 2 秒，短句会等到 drain 才开始出声、Stop 后也有明显拖尾；这里把目标缓冲设为约 100 ms
        var attr = new PulseSimple.BufferAttr
        {
            MaxLength = uint.MaxValue,
            TLength = (uint)(format.AverageBytesPerSecond / 10 / format.BlockAlign * format.BlockAlign),
            PreBuf = uint.MaxValue,
            MinReq = uint.MaxValue,
            FragSize = uint.MaxValue,
        };
        var handle = PulseSimple.New(null, _appName, PulseSimple.StreamPlayback, null, "BetterGI 播放", spec, 0, attr, out var error);
        if (handle == 0)
        {
            throw new InvalidOperationException("无法连接 PulseAudio：" + PulseSimple.StrError(error));
        }

        _stopRequested = false;
        _paused = false;
        _resume.Set();
        PlaybackState = PlaybackState.Playing;
        _thread = new Thread(() => Run(handle, _source, format)) { IsBackground = true, Name = "PulseAudio 播放" };
        _thread.Start();
    }

    private unsafe void Run(nint handle, IWaveProvider source, WaveFormat format)
    {
        Exception? failure = null;
        try
        {
            // 约 20 ms 一块，Stop 最多延迟一块的时间生效
            var block = Math.Max(format.BlockAlign, format.AverageBytesPerSecond / 50 / format.BlockAlign * format.BlockAlign);
            var buffer = new byte[block];
            while (!_stopRequested)
            {
                if (_paused)
                {
                    _resume.Wait(100);
                    continue;
                }

                var read = source.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }

                ApplyVolume(buffer, read, format);
                fixed (byte* p = buffer)
                {
                    if (PulseSimple.Write(handle, p, (nuint)read, out var error) < 0)
                    {
                        throw new IOException("PulseAudio 写入失败：" + PulseSimple.StrError(error));
                    }
                }
            }

            if (_stopRequested)
            {
                PulseSimple.Flush(handle, out _);
            }
            else if (PulseSimple.Drain(handle, out var drainError) < 0)
            {
                throw new IOException("PulseAudio 播放结束等待失败：" + PulseSimple.StrError(drainError));
            }
        }
        catch (Exception e)
        {
            failure = e;
        }
        finally
        {
            PulseSimple.Free(handle);
            PlaybackState = PlaybackState.Stopped;
            PlaybackStopped?.Invoke(this, new StoppedEventArgs(failure));
        }
    }

    private void ApplyVolume(byte[] buffer, int count, WaveFormat format)
    {
        var volume = Math.Clamp(Volume, 0f, 1f);
        if (volume >= 0.999f)
        {
            return;
        }

        if (format.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, count));
            for (var i = 0; i < samples.Length; i++) samples[i] *= volume;
        }
        else if (format.BitsPerSample == 16)
        {
            var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(buffer.AsSpan(0, count));
            for (var i = 0; i < samples.Length; i++) samples[i] = (short)(samples[i] * volume);
        }
    }

    public void Stop()
    {
        _stopRequested = true;
        _resume.Set();
    }

    public void Pause()
    {
        if (PlaybackState == PlaybackState.Playing)
        {
            _paused = true;
            _resume.Reset();
            PlaybackState = PlaybackState.Paused;
        }
    }

    public void Dispose()
    {
        Stop();
        if (_thread is { IsAlive: true } t && t != Thread.CurrentThread)
        {
            t.Join(TimeSpan.FromSeconds(2));
        }

        _resume.Dispose();
    }
}

/// <summary>Linux 音频输出的探测与选择</summary>
public static class LinuxAudioOutput
{
    /// <summary>探测 PulseAudio / PipeWire 是否可用：尝试打开一个播放流后立即关闭。返回 null 表示可用，否则为原因</summary>
    public static string? Probe()
    {
        try
        {
            var spec = new PulseSimple.SampleSpec { Format = PulseSimple.SampleFormat.S16Le, Rate = 16000, Channels = 1 };
            var handle = PulseSimple.New(null, "BetterGI", PulseSimple.StreamPlayback, null, "BetterGI 检测", spec, 0, 0, out var error);
            if (handle == 0)
            {
                return "无法连接音频服务（PulseAudio / PipeWire）：" + PulseSimple.StrError(error);
            }

            PulseSimple.Free(handle);
            return null;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return "找不到 libpulse-simple.so.0（请安装 libpulse0；PipeWire 用户需要 pipewire-pulse）";
        }
    }

    /// <summary>
    /// 选择剧情语音等使用的输出：PulseAudio 可用时使用，否则保持静音输出（配音流程照常推进）。
    /// 返回状态文字，供环境页显示
    /// </summary>
    public static (bool Available, string Status) Install()
    {
        if (Probe() is { } reason)
        {
            QuestVoiceAudioHost.OutputFactory = static () => new SilentWavePlayer();
            QuestVoiceAudioHost.OutputName = "静音输出";
            AudioStatus = (false, reason + "，音频播放已静音");
        }
        else
        {
            QuestVoiceAudioHost.OutputFactory = static () => new PulseAudioWavePlayer();
            QuestVoiceAudioHost.OutputName = "PulseAudio";
            AudioStatus = (true, "PulseAudio（或 PipeWire 兼容层）");
        }

        return AudioStatus;
    }

    /// <summary>最近一次 Install 的结果</summary>
    public static (bool Available, string Status) AudioStatus { get; private set; } = (false, "尚未检测");
}
