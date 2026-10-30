using BetterGenshinImpact.GameTask;

namespace BetterGenshinImpact.Core.Monitor;

/// <summary>进程内共用的全局键鼠监听（原 MouseKeyMonitor.GlobalHook 的创建逻辑）</summary>
public static class SharedGlobalInputHook
{
    private static IGlobalInputHook? _instance;
    private static readonly object Lock = new();

    /// <summary>
    /// 全局键鼠监听（由平台服务创建；Windows 为低级键鼠钩子，首次访问须在 UI 线程上）
    /// </summary>
    public static IGlobalInputHook Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (Lock)
                {
                    if (_instance == null)
                    {
                        _instance = GameSystem.Platform.CreateGlobalInputHook();
                    }
                }
            }
            return _instance;
        }
    }

    /// <summary>已创建的实例，未创建时为 null（不会触发创建）</summary>
    public static IGlobalInputHook? Current => _instance;

    /// <summary>丢弃当前实例（调用方负责先 Dispose），下次访问 <see cref="Instance"/> 时重新创建</summary>
    public static void Reset()
    {
        lock (Lock)
        {
            _instance = null;
        }
    }
}
