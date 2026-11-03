using System.Diagnostics;
using System.Text.RegularExpressions;
using BetterGenshinImpact.GameTask.AutoSkip.Audio;

namespace BetterGenshinImpact.Platform.Linux.Audio;

/// <summary>PulseAudio / PipeWire（pipewire-pulse）里的一个播放流（sink input）</summary>
public sealed record PulseSinkInput(int Index, int? ProcessId, string? ProcessBinary, string? ApplicationName, bool Corked);

/// <summary>
/// 按进程捕获音频（自动剧情“听完语音再选”与 Silero VAD 诊断），对应 Windows 的 WASAPI 进程环回：
/// 用 <c>pactl list sink-inputs</c> 找到游戏进程的播放流（application.process.id = 游戏 pid；Wine 下就是游戏进程。找不到时按程序名匹配），
/// 用 <c>parecord --monitor-stream</c> 只录这一路流，16 kHz 单声道 s16le，交给 Core 的 VAD。
/// PulseAudio 与 PipeWire 的 pulse 兼容层都适用。
/// <para>
/// WASAPI 环回在进程不出声时仍按时钟给出静音包；这里游戏还没有播放流、流已结束或超过 <see cref="SilenceGapMs"/> 没有数据时同样按时钟补静音，
/// 使“没有语音”的判断与 Windows 一致。流消失后每 <see cref="RescanIntervalMs"/> 重新查找（游戏切换场景会重建流）
/// </para>
/// </summary>
public sealed class PulseProcessAudioCapture : IProcessAudioCapture
{
    /// <summary>与 Core 的 SileroVadDetector.SampleRate 相同</summary>
    public const int SampleRate = 16000;

    /// <summary>超过这么久没有收到真实采样时按时钟补静音</summary>
    public const int SilenceGapMs = 300;

    /// <summary>没有录音流时重新查找游戏播放流的间隔</summary>
    public const int RescanIntervalMs = 1000;

    private readonly int _pid;
    private readonly string? _processName;
    private readonly Func<IReadOnlyList<PulseSinkInput>> _listSinkInputs;
    private readonly Func<int, Stream> _openMonitor;
    private readonly Func<long> _clockMs;
    private readonly object _gate = new();
    private readonly List<float> _buffer = [];
    private readonly Thread _thread;
    private readonly CancellationTokenSource _cts = new();
    private readonly long _startMs;
    private long _deliveredSamples;
    private long _lastRealDataMs;
    private Stream? _current;

    /// <param name="pid">游戏进程号</param>
    /// <param name="processName">游戏程序名（如 YuanShen.exe），pid 找不到播放流时用它匹配；null 时读 /proc/pid/cmdline</param>
    /// <param name="listSinkInputs">列出播放流（测试可替换），默认 <see cref="ListSinkInputs"/></param>
    /// <param name="openMonitor">打开某个播放流的 16 kHz 单声道 s16le 录音（测试可替换），默认启动 parecord</param>
    /// <param name="clockMs">毫秒时钟（测试可替换）</param>
    public PulseProcessAudioCapture(int pid, string? processName = null, Func<IReadOnlyList<PulseSinkInput>>? listSinkInputs = null,
        Func<int, Stream>? openMonitor = null, Func<long>? clockMs = null)
    {
        _pid = pid;
        _processName = processName ?? ReadProcessName(pid);
        _listSinkInputs = listSinkInputs ?? ListSinkInputs;
        _openMonitor = openMonitor ?? OpenParecord;
        _clockMs = clockMs ?? (() => Environment.TickCount64);
        if (listSinkInputs is null)
        {
            // 没有 pactl / parecord 或连不上音频服务时直接失败，调用方回退到固定延迟
            EnsureTools();
            _ = ListSinkInputs();
        }

        _startMs = _clockMs();
        _lastRealDataMs = _startMs;
        _thread = new Thread(Run) { IsBackground = true, Name = "bgi-pulse-capture" };
        _thread.Start();
    }

    /// <summary>当前正在录的播放流序号（测试与诊断用），没有时为 null</summary>
    public int? CurrentSinkInput { get; private set; }

    public void ReadAvailableSamples(List<float> destination)
    {
        lock (_gate)
        {
            PadSilence();
            destination.AddRange(_buffer);
            _buffer.Clear();
        }
    }

    public void DiscardAvailableSamples()
    {
        lock (_gate)
        {
            PadSilence();
            _buffer.Clear();
        }
    }

    /// <summary>超过 <see cref="SilenceGapMs"/> 没有真实采样时，把落后于时钟的部分补成静音</summary>
    private void PadSilence()
    {
        var now = _clockMs();
        if (now - _lastRealDataMs < SilenceGapMs) return;
        var expected = (now - _startMs) * SampleRate / 1000;
        var missing = expected - _deliveredSamples;
        if (missing <= 0) return;
        missing = Math.Min(missing, SampleRate * 10L);
        for (long i = 0; i < missing; i++) _buffer.Add(0f);
        _deliveredSamples = expected;
    }

    private void Run()
    {
        var token = _cts.Token;
        var chunk = new byte[SampleRate / 50 * 2];
        while (!token.IsCancellationRequested)
        {
            var target = FindTarget();
            if (target is null)
            {
                token.WaitHandle.WaitOne(RescanIntervalMs);
                continue;
            }

            Stream stream;
            try
            {
                stream = _openMonitor(target.Index);
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                token.WaitHandle.WaitOne(RescanIntervalMs);
                continue;
            }

            lock (_gate)
            {
                _current = stream;
                CurrentSinkInput = target.Index;
            }

            var carry = -1;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var n = stream.Read(chunk, 0, chunk.Length);
                    if (n <= 0) break;
                    lock (_gate)
                    {
                        var i = 0;
                        if (carry >= 0)
                        {
                            Add((short)(carry | (chunk[0] << 8)));
                            carry = -1;
                            i = 1;
                        }

                        for (; i + 1 < n; i += 2) Add((short)(chunk[i] | (chunk[i + 1] << 8)));
                        if (i < n) carry = chunk[i];
                        _lastRealDataMs = _clockMs();
                    }
                }
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
            }
            finally
            {
                lock (_gate)
                {
                    _current = null;
                    CurrentSinkInput = null;
                }

                stream.Dispose();
            }

            token.WaitHandle.WaitOne(200);
        }
    }

    private void Add(short s)
    {
        _buffer.Add(s / 32768f);
        _deliveredSamples++;
    }

    private PulseSinkInput? FindTarget()
    {
        IReadOnlyList<PulseSinkInput> list;
        try
        {
            list = _listSinkInputs();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }

        return SelectStream(list, _pid, _processName);
    }

    /// <summary>选择游戏的播放流：先按 pid，再按程序名；有多路时优先未暂停（corked）的、序号最大的（最新）</summary>
    public static PulseSinkInput? SelectStream(IReadOnlyList<PulseSinkInput> list, int pid, string? processName)
    {
        static PulseSinkInput? Best(IEnumerable<PulseSinkInput> c) => c.OrderBy(s => s.Corked).ThenByDescending(s => s.Index).FirstOrDefault();
        var byPid = Best(list.Where(s => s.ProcessId == pid));
        if (byPid is not null || string.IsNullOrEmpty(processName)) return byPid;
        return Best(list.Where(s => string.Equals(s.ProcessBinary, processName, StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(s.ApplicationName, processName, StringComparison.OrdinalIgnoreCase)));
    }

    private static readonly Regex Header = new(@"^Sink Input #(\d+)\s*$", RegexOptions.Compiled);
    private static readonly Regex Prop = new(@"^\s+([a-z0-9_.]+) = ""(.*)""\s*$", RegexOptions.Compiled);
    private static readonly Regex CorkedLine = new(@"^\s+Corked:\s*(\S+)", RegexOptions.Compiled);

    /// <summary>解析 <c>LC_ALL=C pactl list sink-inputs</c> 的文本输出</summary>
    public static List<PulseSinkInput> ParseSinkInputs(string text)
    {
        var result = new List<PulseSinkInput>();
        int? index = null;
        int? pid = null;
        string? binary = null, app = null;
        var corked = false;

        void Flush()
        {
            if (index is { } i) result.Add(new PulseSinkInput(i, pid, binary, app, corked));
            index = null; pid = null; binary = null; app = null; corked = false;
        }

        foreach (var line in text.Split('\n'))
        {
            var h = Header.Match(line);
            if (h.Success)
            {
                Flush();
                index = int.Parse(h.Groups[1].Value);
                continue;
            }

            if (index is null) continue;
            var c = CorkedLine.Match(line);
            if (c.Success)
            {
                corked = c.Groups[1].Value == "yes";
                continue;
            }

            var p = Prop.Match(line);
            if (!p.Success) continue;
            switch (p.Groups[1].Value)
            {
                case "application.process.id" when int.TryParse(p.Groups[2].Value, out var v): pid = v; break;
                case "application.process.binary": binary = p.Groups[2].Value; break;
                case "application.name": app = p.Groups[2].Value; break;
            }
        }

        Flush();
        return result;
    }

    /// <summary>列出当前所有播放流（pactl，C 语言环境输出）</summary>
    public static IReadOnlyList<PulseSinkInput> ListSinkInputs()
    {
        var psi = new ProcessStartInfo("pactl") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add("list");
        psi.ArgumentList.Add("sink-inputs");
        psi.Environment["LC_ALL"] = "C";
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 pactl");
        var err = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        if (!p.WaitForExit(5000))
        {
            try { p.Kill(); } catch (InvalidOperationException) { }
            throw new IOException("pactl 没有响应");
        }

        if (p.ExitCode != 0) throw new IOException($"pactl 失败（{p.ExitCode}）：{err.Result.Trim()}");
        return ParseSinkInputs(output);
    }

    /// <summary>parecord 只录一路播放流，原始 16 kHz 单声道 s16le 写到 stdout；返回的流 Dispose 时结束 parecord</summary>
    private static Stream OpenParecord(int sinkInput)
    {
        var psi = new ProcessStartInfo("parecord") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "--raw", $"--monitor-stream={sinkInput}", "--format=s16le", $"--rate={SampleRate}", "--channels=1", "--latency-msec=40", "--client-name=BetterGI", "--stream-name=BetterGI 语音检测" })
            psi.ArgumentList.Add(a);
        var p = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 parecord");
        p.ErrorDataReceived += (_, _) => { };
        p.BeginErrorReadLine();
        return new ProcessOutputStream(p);
    }

    private static void EnsureTools()
    {
        foreach (var tool in new[] { "pactl", "parecord" })
        {
            var found = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries)
                .Any(d => File.Exists(Path.Combine(d, tool)));
            if (!found) throw new InvalidOperationException($"找不到 {tool}（PulseAudio 工具，pipewire-pulse 或 pulseaudio-utils 提供），无法读取游戏声音");
        }
    }

    /// <summary>/proc/pid/cmdline 第一个参数的文件名（Wine 下为 YuanShen.exe 等）</summary>
    private static string? ReadProcessName(int pid)
    {
        try
        {
            var arg0 = File.ReadAllText($"/proc/{pid}/cmdline").Split('\0')[0];
            return string.IsNullOrEmpty(arg0) ? null : Path.GetFileName(arg0.Replace('\\', '/'));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        lock (_gate)
        {
            try { _current?.Dispose(); } catch (IOException) { }
        }

        _thread.Join(2000);
        _cts.Dispose();
    }

    /// <summary>子进程的标准输出；Dispose 时结束进程</summary>
    private sealed class ProcessOutputStream(Process process) : Stream
    {
        private readonly Stream _inner = process.StandardOutput.BaseStream;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int _disposed;

        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
                process.WaitForExit(1000);
                process.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
