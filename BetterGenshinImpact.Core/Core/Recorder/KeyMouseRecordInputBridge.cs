using System;
using BetterGenshinImpact.Platform;

namespace BetterGenshinImpact.Core.Recorder;

/// <summary>
/// 把全局键鼠监听的事件转交给键鼠录制（<see cref="GlobalKeyMouseRecord"/>），与 Windows 主程序 MouseKeyMonitor 中转交录制的部分相同。
/// 供没有 MouseKeyMonitor 的宿主（Linux）在录制期间使用；Dispose 即取消订阅
/// </summary>
public sealed class KeyMouseRecordInputBridge : IDisposable
{
    private readonly IGlobalInputHook _hook;
    private int _disposed;

    public KeyMouseRecordInputBridge(IGlobalInputHook hook)
    {
        _hook = hook ?? throw new ArgumentNullException(nameof(hook));
        _hook.KeyDown += OnKeyDown;
        _hook.KeyUp += OnKeyUp;
        _hook.MouseDown += OnMouseDown;
        _hook.MouseUp += OnMouseUp;
        _hook.MouseMove += OnMouseMove;
        _hook.MouseWheel += OnMouseWheel;
    }

    private static GlobalKeyMouseRecord Record => GlobalKeyMouseRecord.Instance;

    private void OnKeyDown(object? sender, GlobalKeyEventArgs e) => Record.GlobalHookKeyDown(e, DateTime.UtcNow);

    private void OnKeyUp(object? sender, GlobalKeyEventArgs e) => Record.GlobalHookKeyUp(e, DateTime.UtcNow);

    private void OnMouseDown(object? sender, GlobalMouseEventArgs e) => Record.GlobalHookMouseDown(e, DateTime.UtcNow);

    private void OnMouseUp(object? sender, GlobalMouseEventArgs e) => Record.GlobalHookMouseUp(e, DateTime.UtcNow);

    private void OnMouseMove(object? sender, GlobalMouseEventArgs e) => Record.GlobalHookMouseMoveTo(e, DateTime.UtcNow);

    private void OnMouseWheel(object? sender, GlobalMouseEventArgs e) => Record.GlobalHookMouseWheel(e, DateTime.UtcNow);

    public void Dispose()
    {
        if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _hook.KeyDown -= OnKeyDown;
        _hook.KeyUp -= OnKeyUp;
        _hook.MouseDown -= OnMouseDown;
        _hook.MouseUp -= OnMouseUp;
        _hook.MouseMove -= OnMouseMove;
        _hook.MouseWheel -= OnMouseWheel;
    }
}
