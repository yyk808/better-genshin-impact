using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.KeyMacro;

/// <summary>
/// KeyMacroStore 的 Linux 部分：防抖计时到时经界面线程的 <see cref="SynchronizationContext"/> 回到界面线程执行
/// （对应 WPF 的 DispatcherTimer）；删除时移到 freedesktop 回收站（对应 Windows 回收站）
/// </summary>
public sealed partial class KeyMacroStore
{
    /// <summary>
    /// 界面线程
    /// </summary>
    private readonly SynchronizationContext _uiContext;

    /// <summary>
    /// 是否在界面线程上，由界面提供（Avalonia Dispatcher.UIThread.CheckAccess）
    /// </summary>
    private readonly Func<bool> _checkAccess;

    private readonly Timer _saveTimer;
    private readonly Timer _reloadTimer;

    /// <summary>
    /// 创建宏目录存储
    /// </summary>
    /// <param name="logger">日志</param>
    /// <param name="uiContext">界面线程的同步上下文，全部方法都在该线程上调用</param>
    /// <param name="checkAccess">当前是否在界面线程上</param>
    public KeyMacroStore(ILogger<KeyMacroStore> logger, SynchronizationContext uiContext, Func<bool> checkAccess)
    {
        _logger = logger;
        _uiContext = uiContext;
        _checkAccess = checkAccess;
        _saveTimer = new Timer(_ => _uiContext.Post(_ => Flush(), null));
        _reloadTimer = new Timer(_ => _uiContext.Post(_ => Reload(), null));
    }

    private partial void VerifyAccess()
    {
        if (!_checkAccess())
        {
            throw new InvalidOperationException("按键宏目录只能在界面线程上访问");
        }
    }

    private partial void RestartSaveTimer() => _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);

    private partial void PostScheduleReload() => _reloadTimer.Change(ReloadDelay, Timeout.InfiniteTimeSpan);

    /// <summary>
    /// 移到用户回收站（freedesktop.org Trash 规范：$XDG_DATA_HOME/Trash/files 与 info/*.trashinfo），
    /// 文件管理器可以从回收站还原
    /// </summary>
    private static partial void MoveToRecycleBin(string path)
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(dataHome))
        {
            dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        var trash = Path.Combine(dataHome, "Trash");
        var files = Path.Combine(trash, "files");
        var info = Path.Combine(trash, "info");
        Directory.CreateDirectory(files);
        Directory.CreateDirectory(info);
        var fullPath = Path.GetFullPath(path);
        var baseName = Path.GetFileName(fullPath);
        for (var i = 1; ; i++)
        {
            var name = i == 1 ? baseName : $"{Path.GetFileNameWithoutExtension(baseName)}.{i}{Path.GetExtension(baseName)}";
            var infoPath = Path.Combine(info, name + ".trashinfo");
            FileStream stream;
            try
            {
                // 以独占新建 .trashinfo 占住这个名字（规范要求的原子占位）
                stream = new FileStream(infoPath, FileMode.CreateNew, FileAccess.Write);
            }
            catch (IOException) when (File.Exists(infoPath))
            {
                continue;
            }

            using (stream)
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write("[Trash Info]\nPath=" + EscapePath(fullPath) + "\nDeletionDate="
                             + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "\n");
            }

            try
            {
                File.Move(fullPath, Path.Combine(files, name));
            }
            catch
            {
                File.Delete(infoPath);
                throw;
            }

            return;
        }
    }

    /// <summary>
    /// 按 URL 规则转义路径（保留 /）
    /// </summary>
    private static string EscapePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
}
