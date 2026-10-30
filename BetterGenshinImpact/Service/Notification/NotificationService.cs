using System;
using System.Net.Http;
using BetterGenshinImpact.Infrastructure.Network;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Infrastructure.Temp;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using BetterGenshinImpact.Service.Notifier;
using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BetterGenshinImpact.Service.Notification;

/// <summary>
///     通知服务类，管理和分发各种通知
/// </summary>
public class NotificationService : IHostedService, IDisposable, INotificationHub
{
    private readonly NotifierManager _notifierManager;
    private readonly GameRuntimeService _gameRuntimeService;

    /// <summary>
    ///     注入的配置根，通知配置从这里读取
    /// </summary>
    private readonly AllConfig _config;

    private readonly HttpClient _notifyHttpClient;
    private readonly CancellationTokenSource? _webSocketCts;

    /// <summary>
    ///     临时文件服务，Windows 通知的配图写入其中
    /// </summary>
    private readonly ITempFileService _tempFileService;

    /// <summary>
    ///     命名 HttpClient 工厂，各通知渠道的客户端跟随全局网络设置
    /// </summary>
    private readonly IHttpClientFactory _httpClientFactory;

    // 缓存配置对象引用
    private NotificationConfig? _notificationConfig;

    /// <summary>
    ///     构造函数
    /// </summary>
    public NotificationService(NotifierManager notifierManager, GameRuntimeService gameRuntimeService, AllConfig config,
        ITempFileService tempFileService, IHttpClientFactory httpClientFactory)
    {
        _notifierManager = notifierManager ?? throw new ArgumentNullException(nameof(notifierManager));
        _gameRuntimeService = gameRuntimeService;
        _config = config;
        _tempFileService = tempFileService;
        _httpClientFactory = httpClientFactory;
        _notifyHttpClient = httpClientFactory.CreateClient(HttpClientNames.Notification);
        _webSocketCts = new CancellationTokenSource();
    }

    /// <summary>
    ///     释放资源
    /// </summary>
    public void Dispose()
    {
        // 先释放所有通知器（含微信 Clawbot 后台长轮询会话），再释放共享 HttpClient
        _notifierManager.RemoveAllNotifiers();
        // _webSocketCts?.Cancel();
        _webSocketCts?.Dispose();
        _notifyHttpClient?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     服务启动时初始化所有通知器
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _notificationConfig = _config.NotificationConfig;
        InitializeNotifiers();
        return Task.CompletedTask;
    }

    /// <summary>
    ///     服务停止时取消WebSocket连接
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _webSocketCts?.Cancel();
        return Task.CompletedTask;
    }

    /// <summary>
    ///     获取 NotificationService 单例实例（通过 DI 容器解析，保留为静态转发入口）
    /// </summary>
    public static NotificationService Instance()
    {
        return CoreServices.GetService<NotificationService>()
               ?? throw new InvalidOperationException("NotificationService 未在容器中初始化");
    }

    /// <summary>
    ///     初始化所有通知器
    ///     注意：添加新的通知渠道时，需在此处添加相应的初始化方法调用
    /// </summary>
    private void InitializeNotifiers()
    {
        if (_notificationConfig == null) _notificationConfig = _config.NotificationConfig;

        // 网络通知器（与 Linux 宿主共用，见 Core 的 NetworkNotifierFactory），通知并发发送，注册顺序不影响结果
        foreach (var notifier in NetworkNotifierFactory.Create(_notificationConfig, _notifyHttpClient,
                     () => _httpClientFactory.CreateClient(HttpClientNames.Notification), _webSocketCts ?? new CancellationTokenSource()))
        {
            _notifierManager.RegisterNotifier(notifier);
        }

        // 仅 Windows 主程序提供的通知器
        InitializeWindowsUwpNotifier();
        InitializeEmailNotifier();
        InitializeWechatClawbotNotifier();

        // 添加新通知渠道时，在此处添加对应的初始化方法调用
    }

    // =================== 各类通知器初始化方法 ===================
    // 添加新的通知渠道时，参考以下模板创建新的初始化方法

    /// <summary>
    ///     初始化Windows UWP通知器
    /// </summary>
    private void InitializeWindowsUwpNotifier()
    {
        if (_notificationConfig?.WindowsUwpNotificationEnabled == true)
        {
            _notifierManager.RegisterNotifier(new WindowsUwpNotifier(_tempFileService));
        }
    }

    /// <summary>
    ///     初始化邮件通知器
    /// </summary>
    private void InitializeEmailNotifier()
    {
        if (_notificationConfig?.EmailNotificationEnabled == true)
        {
            _notifierManager.RegisterNotifier(new EmailNotifier(
                _notificationConfig.SmtpServer,
                _notificationConfig.SmtpPort,
                _notificationConfig.SmtpUsername,
                _notificationConfig.SmtpPassword,
                _notificationConfig.FromEmail,
                _notificationConfig.FromName,
                _notificationConfig.ToEmail
            ));
        }
    }

    /// <summary>
    ///     初始化微信 Clawbot 通知器
    /// </summary>
    private void InitializeWechatClawbotNotifier()
    {
        if (_notificationConfig?.WechatClawbotNotificationEnabled != true) return;

        // 微信会话状态（context_token / get_updates_buf）跨进程共享同一份 User/WechatClawbot 文件，
        // 仅主实例（Primary）启动长轮询会话，避免桌面分身等多实例用旧游标并发推进导致令牌互相覆盖。
        // 子实例仍注册通知器保留发送能力（从共享存储读取最新 context_token）。
        _notifierManager.RegisterNotifier(new WechatClawbotNotifier(
            _notifyHttpClient,
            _notificationConfig,
            IsPrimaryInstance()
        ));
    }

    /// <summary>
    ///     是否为 BetterGI 主实例（Primary）。桌面分身 / WebView 等子实例不维护微信长轮询会话。
    /// </summary>
    private static bool IsPrimaryInstance()
    {
        try
        {
            var bootstrap = BetterGenshinImpact.Service.Instance.InstanceBootstrap.Current;
            return bootstrap == null || bootstrap.Context.IsRoot;
        }
        catch (System.Exception)
        {
            // 无法判断时默认主实例行为，避免在单实例场景误禁用
            return true;
        }
    }

    /// <summary>
    ///     刷新所有通知器
    ///     配置更改后调用此方法应用新配置
    /// </summary>
    public void RefreshNotifiers()
    {
        // 批量提交期间抑制刷新，待提交完成后统一刷新一次，避免逐字段触发多次通知器重建
        if (_suppressRefresh)
        {
            _refreshPending = true;
            return;
        }

        _notificationConfig = _config.NotificationConfig;
        _notifierManager.RemoveAllNotifiers();
        InitializeNotifiers();
    }

    /// <summary>
    ///     是否处于批量提交抑制刷新状态
    /// </summary>
    private bool _suppressRefresh;

    /// <summary>
    ///     抑制期间是否有待刷新的标记
    /// </summary>
    private bool _refreshPending;

    /// <summary>
    ///     批量更新通知配置（如微信 Clawbot 绑定凭证）时抑制逐字段刷新，
    ///     提交完成后统一刷新一次，保证通知器读到完整的配置集合。
    /// </summary>
    public IDisposable SuppressRefreshNotifiers()
    {
        _suppressRefresh = true;
        return new RefreshSuppression(this);
    }

    private sealed class RefreshSuppression(NotificationService service) : IDisposable
    {
        public void Dispose()
        {
            service._suppressRefresh = false;
            service._refreshPending = false;
            // 绑定是用户主动操作，即使凭证未变化也必须刷新通知器，
            // 以确保会话从独立存储加载最新的 context_token / get_updates_buf。
            service.RefreshNotifiers();
        }
    }

    /// <summary>
    ///     测试指定类型的通知器
    /// </summary>
    public async Task<NotificationTestResult> TestNotifierAsync<T>() where T : INotifier
    {
        var testData = CreateTestNotificationData();
        try
        {
            // 测试发送走管理器入口，纳入在途租约计数，避免发送期间释放通知器资源
            var error = await _notifierManager.SendTestAsync<T>(testData);
            return error == null ? NotificationTestResult.Success() : NotificationTestResult.Error(error);
        }
        catch (NotifierException ex)
        {
            return NotificationTestResult.Error(ex.Message);
        }
        catch (Exception ex)
        {
            return NotificationTestResult.Error($"测试通知时发生未知错误: {ex.Message}");
        }
        finally
        {
            testData.Screenshot?.Dispose();
            testData.Screenshot = null;
        }
    }

    /// <summary>
    ///     创建测试用的通知数据
    /// </summary>
    private BaseNotificationData CreateTestNotificationData()
    {
        var testData = new BaseNotificationData
        {
            Event = NotificationEvent.Test.Code,
            Result = NotificationEventResult.Success,
            Message = "这是一条测试通知信息"
        };

        // 只在截图器已启动时附带截图；运行环境只在这里读取一次，不缓存
        if (_gameRuntimeService.Current is { } runtime)
        {
            try
            {
                using var capture = runtime.CaptureToRectArea();
                testData.Screenshot = capture.CacheImage.Clone();
            }
            catch (Exception ex)
            {
                TaskControl.Logger.LogWarning(ex, "获取测试通知截图失败");
            }
        }

        return testData;
    }

    /// <summary>
    ///     向所有通知器发送通知（异步方法）
    /// </summary>
    public async Task NotifyAllNotifiersAsync(BaseNotificationData notificationData)
    {
        if (notificationData == null) throw new ArgumentNullException(nameof(notificationData));

        if (!ShouldSendNotification(notificationData.Event))
        {
            // 订阅关闭时同样需要释放调用方传入的截图，避免长期泄漏
            notificationData.Screenshot?.Dispose();
            notificationData.Screenshot = null;
            return;
        }

        try
        {
            AddScreenshotIfNeeded(notificationData);
            await _notifierManager.SendNotificationToAllAsync(notificationData);
        }
        catch (Exception ex)
        {
            TaskControl.Logger.LogError(ex, "发送通知时发生错误");
        }
        finally
        {
            // 无论截图由调用方传入还是此处补充，发送完成后统一释放并清空引用
            notificationData.Screenshot?.Dispose();
            notificationData.Screenshot = null;
        }
    }

    /// <summary>
    ///     判断是否应该发送此类型的通知
    /// </summary>
    private bool ShouldSendNotification(string eventCode)
    {
        return NotificationEventSubscriptionHelper.ShouldSendNotification(
            _notificationConfig?.NotificationEventSubscribe,
            eventCode);
    }

    /// <summary>
    ///     如果需要，为通知添加截图
    /// </summary>
    private void AddScreenshotIfNeeded(BaseNotificationData notificationData)
    {
        if (_notificationConfig?.IncludeScreenShot != true || notificationData.Screenshot != null)
        {
            return;
        }

        try
        {
            var mat = TaskControl.CaptureGameImageNoRetry(_gameRuntimeService.Current?.Capture);
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

    /// <summary>
    ///     向所有通知器发送通知（非阻塞方法）
    /// </summary>
    public void NotifyAllNotifiers(BaseNotificationData notificationData)
    {
        if (notificationData == null) throw new ArgumentNullException(nameof(notificationData));

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
}
