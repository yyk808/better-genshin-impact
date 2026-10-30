using System;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.Common.Hotkeys;

/// <summary>
/// 长按类快捷键的执行：按下时触发一次 <see cref="OnKeyDown"/>，并在后台线程中反复执行 <see cref="OnKeyPress"/> 直到抬起；
/// 抬起时触发 <see cref="OnKeyUp"/>。语义与 WPF 的 KeyboardHook（IsHold=true）相同，供没有低级键盘钩子的宿主使用
/// </summary>
public sealed class HoldHotkeyRunner
{
    private readonly object _loopLock = new();
    private volatile bool _pressed;
    private int _loopRunning;

    /// <summary>按住期间反复执行的动作（可为空）</summary>
    public Action? OnKeyPress { get; init; }

    /// <summary>按下时执行一次（可为空）</summary>
    public Action? OnKeyDown { get; init; }

    /// <summary>抬起时执行一次（可为空）</summary>
    public Action? OnKeyUp { get; init; }

    /// <summary>为 false 时忽略按下（例如游戏不在前台）</summary>
    public Func<bool>? CanRun { get; init; }

    /// <summary>动作抛出的异常（不影响后续触发）</summary>
    public event Action<Exception>? Error;

    public bool IsPressed => _pressed;

    public void KeyDown()
    {
        if (_pressed || CanRun?.Invoke() == false)
        {
            return;
        }

        _pressed = true;
        Safe(OnKeyDown);
        if (OnKeyPress is null || Interlocked.Exchange(ref _loopRunning, 1) == 1)
        {
            return;
        }

        Task.Run(() =>
        {
            try
            {
                lock (_loopLock)
                {
                    while (_pressed)
                    {
                        Safe(OnKeyPress);
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _loopRunning, 0);
            }
        });
    }

    public void KeyUp()
    {
        if (!_pressed)
        {
            return;
        }

        _pressed = false;
        Safe(OnKeyUp);
    }

    private void Safe(Action? action)
    {
        if (action is null)
        {
            return;
        }

        try
        {
            action();
        }
        catch (Exception e)
        {
            Error?.Invoke(e);
            Thread.Sleep(10);
        }
    }
}
