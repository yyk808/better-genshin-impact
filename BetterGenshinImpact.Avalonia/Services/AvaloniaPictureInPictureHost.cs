using Avalonia.Threading;
using BetterGenshinImpact.Avalonia.Views;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoSkip;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Service;
using OpenCvSharp;

namespace BetterGenshinImpact.Avalonia.Services;

/// <summary>
/// <see cref="IPictureInPictureHost"/> 的 Avalonia 实现，逻辑与 WPF 的 PictureInPictureService 相同：
/// 截图调度在游戏失焦且开启画中画时每帧调用 <see cref="Update"/>（“截图器供图”时复制一份画面交给窗口，“60帧模式”由窗口自己截图），
/// 游戏回到前台或任务开始时 <see cref="Hide"/>；右键关闭后本次失焦期间不再显示，直到 Hide(resetManual: true)。
/// 云原神网页版不创建独立窗口
/// </summary>
public sealed class AvaloniaPictureInPictureHost : IPictureInPictureHost, IDisposable
{
    private PictureInPictureWindow? _window;
    private volatile bool _manualClosed;

    public bool IsManuallyClosed => _manualClosed;

    /// <summary>当前窗口（用于验证），只在界面线程读取</summary>
    public PictureInPictureWindow? Window => _window;

    public void Update(Mat frame)
    {
        if (_manualClosed || !IsWindowAllowed()) return;
        Mat? copy = null;
        if (ConfigHost.Config!.AutoSkipConfig.PictureInPictureSourceType == nameof(PictureSourceType.TriggerDispatcher))
        {
            copy = frame.Clone();
        }

        Dispatcher.UIThread.Post(() =>
        {
            EnsureWindow();
            if (_window is null)
            {
                copy?.Dispose();
                return;
            }

            _window.ShowPip();
            _window.SetFrame(copy);
        });
    }

    public void Hide(bool resetManual = false)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (resetManual) _manualClosed = false;
            if (_window is { IsVisible: true }) _window.HidePip();
        });
    }

    public void ResetManualClose() => _manualClosed = false;

    private void EnsureWindow()
    {
        if (_window is not null || !IsWindowAllowed()) return;
        var window = new PictureInPictureWindow();
        window.ClosedByUser += () => _manualClosed = true;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window)) _window = null;
        };
        _window = window;
    }

    /// <summary>云原神网页版只在主界面预览，不创建独立画中画窗口（同 WPF）</summary>
    private static bool IsWindowAllowed() => CoreServices.GetService<GameRuntimeService>()?.Kind != GameRuntimeKind.WebPage;

    public void Dispose()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _window?.Close();
            _window = null;
        }
    }
}
