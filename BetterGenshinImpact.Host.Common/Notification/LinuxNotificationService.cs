using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Infrastructure.Network;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using BetterGenshinImpact.Service.Notifier;
using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Host.Common.Notification;

/// <summary>
/// Linux 宿主的通知服务（对应 Windows 主程序的 NotificationService）：
/// 网络通知器由 Core 的 <see cref="NetworkNotifierFactory"/> 按配置创建；“Windows 通知”开关在 Linux 上对应系统通知（<see cref="FreedesktopNotifier"/>）。
/// 事件订阅过滤、补充截图、并发发送与 Windows 版相同；邮件与微信 Clawbot 由本服务创建（同 Windows 版）
/// </summary>
public sealed class LinuxNotificationService : INotificationHub, IDisposable
{
    private readonly NotifierManager _manager;
    private readonly AllConfig _config;
    private readonly GameRuntimeService _runtimeService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HttpClient _sharedClient;
    private readonly CancellationTokenSource _webSocketCts = new();
    private readonly ILogger _logger;
    private readonly object _lock = new();

    public LinuxNotificationService(NotifierManager manager, AllConfig config, GameRuntimeService runtimeService,
        IHttpClientFactory httpClientFactory, ILogger<LinuxNotificationService> logger)
    {
        _manager = manager;
        _config = config;
        _runtimeService = runtimeService;
        _httpClientFactory = httpClientFactory;
        _sharedClient = httpClientFactory.CreateClient(HttpClientNames.Notification);
        _logger = logger;
        RefreshNotifiers();
    }

    /// <summary>当前注册的通知器名称</summary>
    public IReadOnlyList<string> NotifierNames { get; private set; } = [];

    private bool _suppressRefresh;

    /// <summary>
    /// 批量更新通知配置（如微信 Clawbot 绑定凭证）时抑制逐字段刷新，退出作用域时统一刷新一次
    /// （同 Windows 版 NotificationService.SuppressRefreshNotifiers）
    /// </summary>
    public IDisposable SuppressRefreshNotifiers()
    {
        _suppressRefresh = true;
        return new RefreshSuppression(this);
    }

    private sealed class RefreshSuppression(LinuxNotificationService service) : IDisposable
    {
        public void Dispose()
        {
            service._suppressRefresh = false;
            // 绑定是用户主动操作，即使凭证未变化也刷新，使会话从独立存储加载最新的 context_token / get_updates_buf
            service.RefreshNotifiers();
        }
    }

    public void RefreshNotifiers()
    {
        if (_suppressRefresh)
        {
            return;
        }

        lock (_lock)
        {
            _manager.RemoveAllNotifiers();
            var config = _config.NotificationConfig;
            var notifiers = NetworkNotifierFactory.Create(config, _sharedClient,
                () => _httpClientFactory.CreateClient(HttpClientNames.Notification), _webSocketCts);
            if (config.WindowsUwpNotificationEnabled)
            {
                notifiers.Add(new FreedesktopNotifier { LogFailure = m => _logger.LogDebug("{Message}", m) });
            }

            // 邮件与 Windows 版 NotificationService.InitializeEmailNotifier 相同
            if (config.EmailNotificationEnabled)
            {
                notifiers.Add(new EmailNotifier(config.SmtpServer, config.SmtpPort, config.SmtpUsername, config.SmtpPassword,
                    config.FromEmail, config.FromName, config.ToEmail));
            }

            foreach (var notifier in notifiers)
            {
                _manager.RegisterNotifier(notifier);
            }

            // 微信 Clawbot 与 Windows 版 InitializeWechatClawbotNotifier 相同：会话状态跨进程共享 User/WechatClawbot，
            // 仅主实例启动长轮询会话，子实例只保留发送能力
            if (config.WechatClawbotNotificationEnabled)
            {
                var clawbot = new WechatClawbotNotifier(_sharedClient, config, IsPrimaryInstance());
                notifiers.Add(clawbot);
                _manager.RegisterNotifier(clawbot);
            }

            NotifierNames = notifiers.Select(n => n.Name).ToList();
        }
    }

    /// <summary>是否为主实例（同 Windows 版 NotificationService.IsPrimaryInstance）；无法判断时按主实例处理</summary>
    private static bool IsPrimaryInstance()
    {
        try
        {
            var bootstrap = BetterGenshinImpact.Service.Instance.InstanceBootstrap.Current;
            return bootstrap == null || bootstrap.Context.IsRoot;
        }
        catch (Exception)
        {
            return true;
        }
    }

    public void NotifyAllNotifiers(BaseNotificationData notificationData)
    {
        ArgumentNullException.ThrowIfNull(notificationData);
        Task.Run(async () =>
        {
            try
            {
                await NotifyAllNotifiersAsync(notificationData);
            }
            catch (Exception ex)
            {
                TaskControl.Logger.LogError(ex, "后台发送通知时发生错误");
            }
        });
    }

    /// <summary>与 Windows 版 NotificationService.NotifyAllNotifiersAsync 相同：按订阅过滤、按需补截图、发送后释放截图</summary>
    public async Task NotifyAllNotifiersAsync(BaseNotificationData notificationData)
    {
        var config = _config.NotificationConfig;
        if (!NotificationEventSubscriptionHelper.ShouldSendNotification(config.NotificationEventSubscribe, notificationData.Event))
        {
            notificationData.Screenshot?.Dispose();
            notificationData.Screenshot = null;
            return;
        }

        try
        {
            if (config.IncludeScreenShot && notificationData.Screenshot == null)
            {
                try
                {
                    var mat = TaskControl.CaptureGameImageNoRetry(_runtimeService.Current?.Capture);
                    if (mat != null)
                    {
                        using var imageRegion = new ImageRegion(mat, 0, 0);
                        notificationData.Screenshot = imageRegion.CacheImage.Clone();
                    }
                }
                catch (Exception ex)
                {
                    TaskControl.Logger.LogDebug(ex, "补充通知截图失败");
                }
            }

            await _manager.SendNotificationToAllAsync(notificationData);
        }
        catch (Exception ex)
        {
            TaskControl.Logger.LogError(ex, "发送通知时发生错误");
        }
        finally
        {
            notificationData.Screenshot?.Dispose();
            notificationData.Screenshot = null;
        }
    }

    /// <summary>与 Windows 版 CreateTestNotificationData 相同：截图器已启动时附带截图</summary>
    private BaseNotificationData CreateTestData()
    {
        var data = new BaseNotificationData
        {
            Event = NotificationEvent.Test.Code,
            Result = NotificationEventResult.Success,
            Message = "这是一条测试通知信息"
        };
        if (_runtimeService.Current is { } runtime)
        {
            try
            {
                using var capture = runtime.CaptureToRectArea();
                data.Screenshot = capture.CacheImage.Clone();
            }
            catch (Exception ex)
            {
                TaskControl.Logger.LogWarning(ex, "获取测试通知截图失败");
            }
        }

        return data;
    }

    /// <summary>
    /// 测试单个通知渠道（与 Windows 版 NotificationService.TestNotifierAsync 相同）：按当前配置重建通知器后只向该类通知器发送，
    /// 返回是否成功与提示（未启用、发送失败原因）
    /// </summary>
    public async Task<(bool Success, string Message)> TestNotifierAsync<T>() where T : INotifier
    {
        RefreshNotifiers();
        var data = CreateTestData();
        try
        {
            var error = await _manager.SendTestAsync<T>(data);
            return error is null ? (true, "测试通知发送成功") : (false, error);
        }
        catch (NotifierException ex)
        {
            return (false, ex.Message);
        }
        catch (Exception ex)
        {
            return (false, $"测试通知时发生未知错误: {ex.Message}");
        }
        finally
        {
            data.Screenshot?.Dispose();
            data.Screenshot = null;
        }
    }

    /// <summary>向当前启用的全部通知器发送一条测试通知（不经事件订阅过滤），返回各通知器的结果</summary>
    public async Task<string> SendTestAsync()
    {
        var data = CreateTestData();
        try
        {
            var names = NotifierNames;
            if (names.Count == 0)
            {
                return "没有启用的通知渠道";
            }

            await _manager.SendNotificationToAllAsync(data);
            return "已发送到：" + string.Join("、", names) + "（发送失败的渠道见日志）";
        }
        finally
        {
            data.Screenshot?.Dispose();
            data.Screenshot = null;
        }
    }

    public void Dispose()
    {
        _manager.RemoveAllNotifiers();
        _webSocketCts.Cancel();
        _webSocketCts.Dispose();
        _sharedClient.Dispose();
    }
}
