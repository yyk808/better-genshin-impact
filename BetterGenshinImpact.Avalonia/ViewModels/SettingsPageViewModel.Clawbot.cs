using System.Diagnostics;
using Avalonia.Threading;
using BetterGenshinImpact.Host.Common.Notification;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Service.Notifier;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// 设置页“微信 Clawbot 通知”（对照 WPF NotificationSettingsPageViewModel 的 OnBindWechatClawbot、OnCancelBindWechatClawbot、
/// OnTestWechatClawbotNotification）：扫码登录（二维码链接用浏览器打开，同 WPF）→ 给 Clawbot 发送一次性验证码完成绑定 → 一次性保存凭证；
/// 推送会话过期时在状态中提示
/// </summary>
public sealed partial class SettingsPageViewModel
{
    /// <summary>打开二维码链接（同 WPF 用浏览器打开；界面自测替换为记录链接，不联网）</summary>
    public static Action<string> OpenWechatClawbotQrCode { get; set; } = url =>
        Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { url }, UseShellExecute = false });

    /// <summary>微信 Clawbot 渠道状态（登录 / 绑定进度、测试结果、会话过期提示）</summary>
    [ObservableProperty] private string _wechatClawbotStatus = string.Empty;

    /// <summary>是否正在登录 / 绑定（按钮变为“取消”）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WechatClawbotBindButtonText))]
    private bool _isWechatClawbotBinding;

    public string WechatClawbotBindButtonText => IsWechatClawbotBinding ? "取消" : "登录并绑定";

    private CancellationTokenSource? _wechatClawbotBindCts;
    private bool _wechatClawbotSubscribed;

    /// <summary>订阅推送会话过期事件（后台线程触发，回到界面线程更新状态，同 WPF）</summary>
    private void SubscribeWechatClawbotSessionExpired()
    {
        if (_wechatClawbotSubscribed) return;
        _wechatClawbotSubscribed = true;
        WechatClawbotNotifier.SessionExpired += hint => Dispatcher.UIThread.Post(() => WechatClawbotStatus = hint);
    }

    /// <summary>测试微信 Clawbot 通知（对照 WPF OnTestWechatClawbotNotification）</summary>
    [RelayCommand]
    public async Task TestWechatClawbotNotificationAsync()
    {
        SubscribeWechatClawbotSessionExpired();
        WechatClawbotStatus = string.Empty;
        WechatClawbotStatus = await TestNotifierAsync<WechatClawbotNotifier>("微信 Clawbot");
    }

    /// <summary>登录并绑定 / 取消（同一按钮，同 WPF DataTrigger 切换命令）</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ToggleBindWechatClawbotAsync()
    {
        if (IsWechatClawbotBinding)
        {
            _wechatClawbotBindCts?.Cancel();
            return;
        }

        await BindWechatClawbotAsync();
    }

    /// <summary>扫码登录获取 bot_token，再等待用户发送一次性验证码，成功后一次性提交全部凭证（同 WPF OnBindWechatClawbot）</summary>
    private async Task BindWechatClawbotAsync()
    {
        SubscribeWechatClawbotSessionExpired();
        IsWechatClawbotBinding = true;
        WechatClawbotStatus = "正在获取登录二维码…";
        _wechatClawbotBindCts = new CancellationTokenSource();
        try
        {
            var login = await WechatClawbotHelper.LoginAsync(qrCodeUrl => Dispatcher.UIThread.Invoke(() =>
            {
                WechatClawbotStatus = "请用手机微信扫码登录 Clawbot";
                try
                {
                    OpenWechatClawbotQrCode(qrCodeUrl);
                }
                catch (Exception ex)
                {
                    WechatClawbotStatus = $"请用手机微信扫码登录 Clawbot（二维码链接：{qrCodeUrl}）";
                    _log.Warning("打开微信 Clawbot 二维码链接失败: {Ex}", ex.Message);
                }
            }), _wechatClawbotBindCts.Token);

            WechatClawbotStatus = "登录成功，正在等待绑定消息…";
            var bind = await WechatClawbotHelper.BindAsync(login.BotToken, login.BaseUrl, login.UserId,
                code => Dispatcher.UIThread.Invoke(() => WechatClawbotStatus = $"请给 Clawbot 发送验证码 [{code}] 完成绑定"),
                _wechatClawbotBindCts.Token);

            // 会话令牌 / 游标存入独立存储；全部成功后在抑制刷新的作用域内一次性提交凭证，退出时统一重建一次通知器（同 WPF），
            // 避免逐字段重建时在界面线程上释放刚启动的会话
            await WechatClawbotSessionStore.SaveAsync(login.BotToken, bind.ContextToken, bind.GetUpdatesBuf);
            var n = Config.NotificationConfig;
            using (_host?.Services.GetService<LinuxNotificationService>()?.SuppressRefreshNotifiers())
            {
                n.WechatClawbotBotToken = login.BotToken;
                n.WechatClawbotBaseUrl = login.BaseUrl;
                n.WechatClawbotToUserId = bind.ToUserId;
            }

            WechatClawbotStatus = "绑定成功";
            UiHost.Dialogs.Toast(DialogKind.Success, "微信 Clawbot 绑定成功");
        }
        catch (OperationCanceledException)
        {
            WechatClawbotStatus = "已取消登录/绑定";
        }
        catch (Exception ex)
        {
            WechatClawbotStatus = $"登录/绑定失败：{ex.Message}";
            UiHost.Dialogs.Toast(DialogKind.Error, $"登录/绑定失败：{ex.Message}");
        }
        finally
        {
            IsWechatClawbotBinding = false;
            _wechatClawbotBindCts.Dispose();
            _wechatClawbotBindCts = null;
        }
    }
}
