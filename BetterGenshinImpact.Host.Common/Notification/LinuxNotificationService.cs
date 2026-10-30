using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Infrastructure.Network;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using BetterGenshinImpact.Service.Notifier;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Host.Common.Notification;

/// <summary>
/// Linux 宿主的通知服务（对应 Windows 主程序的 NotificationService）：
/// 网络通知器由 Core 的 <see cref="NetworkNotifierFactory"/> 按配置创建；“Windows 通知”开关在 Linux 上对应系统通知（<see cref="FreedesktopNotifier"/>）。
/// 事件订阅过滤、补充截图、并发发送与 Windows 版相同。邮件与微信 Clawbot 尚未接入
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

    public void RefreshNotifiers()
    {
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

            foreach (var notifier in notifiers)
            {
                _manager.RegisterNotifier(notifier);
            }

            NotifierNames = notifiers.Select(n => n.Name).ToList();
            if (config.EmailNotificationEnabled || config.WechatClawbotNotificationEnabled)
            {
                _logger.LogInformation("通知：邮件 / 微信 Clawbot 在 Linux 宿主中尚未接入");
            }
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

    /// <summary>向当前启用的全部通知器发送一条测试通知（不经事件订阅过滤），返回各通知器的结果</summary>
    public async Task<string> SendTestAsync()
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
