using System.Diagnostics;
using Avalonia.Controls;
using BetterGenshinImpact.GameTask.AutoSkip.Audio;
using BetterGenshinImpact.Platform.Linux.Audio;

namespace BetterGenshinImpact.Avalonia;

internal sealed partial class UiTests
{
    /// <summary>
    /// 语音检测（--ui-test voice-detect，需要 PulseAudio / pipewire-pulse 与 espeak-ng；盒子上是私有 pipewire 的 pulse 层 + null sink）：
    /// 实时触发页“听完语音再选”“Silero VAD 实时诊断”开关可用；打开诊断后面板显示，经 <see cref="PulseProcessAudioCapture"/> 录 paplay 播放的
    /// 本地合成语音（espeak-ng 生成，不联网），判定出现“说话”、播放结束后回到“静音”；录音按钮保存 WAV；关闭后状态复位。
    /// 捕获工厂在自测期间改为指向 paplay 进程（假游戏不出声），结束后还原；配置由外部脚本备份恢复
    /// </summary>
    private async Task VoiceDetectAsync()
    {
        var work = Path.Combine(dir ?? Path.GetTempPath(), "voice");
        Directory.CreateDirectory(work);
        var wav = Path.Combine(work, "speech.wav");
        var espeak = Process.Start(new ProcessStartInfo("espeak-ng", ["-v", "zh", "-s", "150", "-w", wav, "旅行者，这里就是蒙德城。我们先去冒险家协会看看吧，之后再去教堂。"]) { UseShellExecute = false });
        espeak?.WaitForExit(15000);
        Check("espeak-ng 生成本地语音", File.Exists(wav) && new FileInfo(wav).Length > 10000, wav);

        vm.Navigate("实时触发");
        await Settle(600);
        foreach (var ex in All<Expander>()) ex.IsExpanded = true;
        await Settle(600);
        var cfg = vm.Trigger.Config.AutoSkipConfig;
        var saved = (cfg.AutoWaitDialogueOptionVoiceEnabled, cfg.DialogueOptionVoiceVadDiagnosticEnabled);
        var waitSwitch = Named<ToggleSwitch>("WaitVoiceSwitch");
        var diagSwitch = Named<ToggleSwitch>("VadDiagnosticSwitch");
        Check("听完语音再选 / VAD 诊断开关可用", waitSwitch is { IsEnabled: true } && diagSwitch is { IsEnabled: true });
        Check("不再显示 WASAPI 不可用说明", !All<TextBlock>().Any(t => t.Text?.Contains("WASAPI") == true));
        if (waitSwitch is null || diagSwitch is null) return;
        waitSwitch.IsChecked = !saved.Item1;
        await Settle();
        Check("听完语音再选写入配置", cfg.AutoWaitDialogueOptionVoiceEnabled == !saved.Item1);
        waitSwitch.IsChecked = saved.Item1;
        diagSwitch.IsChecked = false;
        await Settle(400);
        Check("诊断关闭时面板隐藏", Named<Border>("VadDiagnosticPanel") is null);

        var state = vm.Trigger.VoiceDiagnosticState;
        var savedFactory = ProcessAudioCapture.Factory;
        Process? player = null;
        try
        {
            // 1.5 秒后开始播放：exec 保持 pid 不变，捕获先建好、先补静音，再录到语音
            player = Process.Start(new ProcessStartInfo("sh", ["-c", $"sleep 1.5; exec paplay '{wav}'"]) { UseShellExecute = false });
            var pid = player!.Id;
            ProcessAudioCapture.Factory = _ => new PulseProcessAudioCapture(pid);
            diagSwitch.IsChecked = true;
            await Settle(600);
            var panel = await WaitFor(() => Named<Border>("VadDiagnosticPanel"));
            Check("打开诊断后显示面板", panel is not null);
            var verdicts = new List<string>();
            var maxP = 0d;
            var speech = await Until(() =>
            {
                verdicts.Add(state.VerdictText);
                maxP = Math.Max(maxP, state.Probability);
                return state.VerdictText == "说话";
            }, 15000);
            Check("播放语音时判定为“说话”", speech, $"最大概率 {maxP:F3}，状态 {state.StatusText}，判定 {string.Join("/", verdicts.Distinct())}");
            Check("面板显示概率与 RMS", Named<TextBlock>("VadProbabilityText")?.Text is { } pt && pt != "--" && state.RmsText != "--", $"{Named<TextBlock>("VadProbabilityText")?.Text} rms={state.RmsText}");
            Named<Button>("VadRecordButton")?.Command?.Execute(null);
            panel?.BringIntoView();
            await Settle(300);
            Shot("trigger-vad-speech.png");
            var ended = await Until(() => player.HasExited, 20000);
            var quiet = await Until(() => state.VerdictText == "静音", 8000);
            Check("播放结束后回到“静音”（按时钟补静音）", ended && quiet, $"{state.VerdictText} {state.StatusText}");
            Named<Button>("VadRecordButton")?.Command?.Execute(null);
            var savedText = "";
            var recorded = await Until(() => (savedText = state.RecordingStatusText).StartsWith("已保存："), 5000);
            var file = recorded ? savedText["已保存：".Length..] : null;
            Check("录音按钮保存 WAV", file is not null && File.Exists(file) && new FileInfo(file).Length > 16000, savedText);
            if (file is not null && File.Exists(file)) File.Delete(file);
        }
        finally
        {
            diagSwitch.IsChecked = false;
            await Settle(600);
            ProcessAudioCapture.Factory = savedFactory;
            try { if (player is { HasExited: false }) player.Kill(); } catch (InvalidOperationException) { }
            player?.Dispose();
        }

        Check("关闭后诊断状态复位", await Until(() => state.StatusText == "诊断未开启", 3000), state.StatusText);
        Check("配置还原", cfg.AutoWaitDialogueOptionVoiceEnabled == saved.Item1 && !cfg.DialogueOptionVoiceVadDiagnosticEnabled);
    }
}
