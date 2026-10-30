using Avalonia.Threading;
using BetterGenshinImpact.Platform;

namespace BetterGenshinImpact.Avalonia.Services;

/// <summary>UiHost.Dispatcher 的 Avalonia 实现：Dispatcher.UIThread</summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public void Invoke(Action action)
    {
        if (CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Invoke(action);
        }
    }

    public T Invoke<T>(Func<T> func) => CheckAccess() ? func() : Dispatcher.UIThread.Invoke(func);

    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    public Task<T> InvokeAsync<T>(Func<T> func) => Dispatcher.UIThread.InvokeAsync(func).GetTask();
}
