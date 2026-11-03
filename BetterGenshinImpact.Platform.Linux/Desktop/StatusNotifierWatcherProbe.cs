using Tmds.DBus.Protocol;

namespace BetterGenshinImpact.Platform.Linux.Desktop;

/// <summary>
/// 系统托盘（StatusNotifierItem）宿主探测：会话总线上有没有 org.kde.StatusNotifierWatcher。
/// KDE、带 AppIndicator 扩展的 GNOME、waybar 等托盘会提供它；niri 本身不提供。
/// 没有宿主时托盘图标不会显示，“关闭到托盘”会让窗口无处找回
/// </summary>
public static class StatusNotifierWatcherProbe
{
    /// <summary>托盘宿主在会话总线上的名字</summary>
    public const string WatcherName = "org.kde.StatusNotifierWatcher";

    /// <summary>
    /// 会话总线上是否有托盘宿主；没有会话总线、连不上或超时都按没有处理
    /// </summary>
    /// <param name="name">要探测的总线名（测试可传别的名字）</param>
    /// <param name="timeout">整体超时，默认 2 秒</param>
    public static async Task<bool> IsAvailableAsync(string name = WatcherName, TimeSpan? timeout = null)
    {
        var address = DBusAddress.Session;
        if (string.IsNullOrEmpty(address)) return false;
        try
        {
            using var conn = new DBusConnection(address);
            var probe = Task.Run(async () =>
            {
                await conn.ConnectAsync();
                var w = conn.GetMessageWriter();
                w.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "NameHasOwner", "s", MessageFlags.None);
                w.WriteString(name);
                return await conn.CallMethodAsync(w.CreateMessage(), static (Message m, object? _) => m.GetBodyReader().ReadBool(), null);
            });
            var done = await Task.WhenAny(probe, Task.Delay(timeout ?? TimeSpan.FromSeconds(2)));
            return done == probe && await probe;
        }
        catch (Exception e) when (e is DBusConnectionException or DBusErrorReplyException or InvalidOperationException or TimeoutException or IOException)
        {
            return false;
        }
    }
}
