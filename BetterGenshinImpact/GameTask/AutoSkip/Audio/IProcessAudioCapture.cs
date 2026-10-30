using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoSkip.Audio;

/// <summary>按进程捕获音频（单声道 float 采样）。Windows 实现为 WASAPI 进程回环捕获</summary>
public interface IProcessAudioCapture : IDisposable
{
    /// <summary>把目前已捕获的采样追加到 <paramref name="destination"/></summary>
    void ReadAvailableSamples(List<float> destination);

    /// <summary>丢弃目前已捕获但未读取的采样</summary>
    void DiscardAvailableSamples();
}

/// <summary>进程音频捕获的创建入口，由宿主设置 <see cref="Factory"/></summary>
public static class ProcessAudioCapture
{
    /// <summary>按目标进程 ID 创建捕获，未设置时表示当前平台不支持</summary>
    public static Func<int, IProcessAudioCapture>? Factory { get; set; }

    public static IProcessAudioCapture Create(int targetProcessId) =>
        (Factory ?? throw new PlatformNotSupportedException("当前平台不支持按进程捕获音频"))(targetProcessId);
}
