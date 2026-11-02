using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Threading;

namespace BetterGenshinImpact.Service.KeyMacro;

/// <summary>
/// KeyMacroStore 的 WPF 部分：防抖计时器为 UI 线程的 DispatcherTimer，删除时移到 Windows 回收站
/// </summary>
public sealed partial class KeyMacroStore
{
    /// <summary>
    /// 所属 UI 线程
    /// </summary>
    private readonly Dispatcher _dispatcher;

    /// <summary>
    /// 写入防抖计时器
    /// </summary>
    private readonly DispatcherTimer _saveTimer;

    /// <summary>
    /// 重新载入防抖计时器
    /// </summary>
    private readonly DispatcherTimer _reloadTimer;

    /// <summary>
    /// 创建宏目录存储
    /// </summary>
    public KeyMacroStore(ILogger<KeyMacroStore> logger)
    {
        _logger = logger;
        _dispatcher = Application.Current.Dispatcher;
        _saveTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Flush();
        };
        _reloadTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = ReloadDelay };
        _reloadTimer.Tick += (_, _) =>
        {
            _reloadTimer.Stop();
            Reload();
        };
    }

    private partial void VerifyAccess() => _dispatcher.VerifyAccess();

    private partial void RestartSaveTimer()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private partial void PostScheduleReload() => _dispatcher.BeginInvoke(ScheduleReload);

    /// <summary>
    /// 重新开始载入防抖计时
    /// </summary>
    private void ScheduleReload()
    {
        _reloadTimer.Stop();
        _reloadTimer.Start();
    }

    private static partial void MoveToRecycleBin(string path)
    {
        // Microsoft.VisualBasic.FileIO 与 System.IO 有同名类型（SearchOption），这里写全名
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
    }
}
