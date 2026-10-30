using BetterGenshinImpact.Service.Notification.Model;

namespace BetterGenshinImpact.Service.Notification;

/// <summary>
/// 宿主通知服务的入口：配置对象（AllConfig）与通知数据（BaseNotificationData）经它调用，不直接引用服务实现。
/// Windows 主程序中由 NotificationService 实现并注册为同一单例
/// </summary>
public interface INotificationHub
{
    /// <summary>通知配置变化后重建通知器</summary>
    void RefreshNotifiers();

    /// <summary>向所有已启用的通知器发送</summary>
    void NotifyAllNotifiers(BaseNotificationData notificationData);

    /// <summary>取容器中的通知服务单例；未初始化时抛出与原 NotificationService.Instance() 相同的异常</summary>
    static INotificationHub Instance() =>
        CoreServices.GetService<INotificationHub>()
        ?? throw new System.InvalidOperationException("NotificationService 未在容器中初始化");
}
