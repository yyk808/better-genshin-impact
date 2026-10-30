using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notifier.Interface;

namespace BetterGenshinImpact.Service.Notifier;

/// <summary>
/// 按通知配置创建网络通知器（Webhook、飞书、OneBot、企业微信、WebSocket、Bark、钉钉、Telegram、信息推送、Discord、Server 酱、MeoW、Gotify、QQ）。
/// 这些通知器都在 Core 中、只依赖 HttpClient，Windows 主程序与 Linux 宿主共用；
/// 系统通知（Windows UWP / Linux freedesktop）、邮件、微信 Clawbot 由宿主自行创建
/// </summary>
public static class NetworkNotifierFactory
{
    /// <param name="config">通知配置</param>
    /// <param name="sharedClient">共享的通知 HttpClient（跟随全局网络设置）</param>
    /// <param name="createClient">需要独立 HttpClient 的通知器（Bark、信息推送）用它新建</param>
    /// <param name="webSocketCts">WebSocket 通知器的取消源</param>
    public static List<INotifier> Create(NotificationConfig config, HttpClient sharedClient, Func<HttpClient> createClient,
        CancellationTokenSource webSocketCts)
    {
        var result = new List<INotifier>();
        void add(INotifier notifier) => result.Add(notifier);

        AddWebhook();
        AddFeishu();
        AddOneBot();
        AddWorkWeixin();
        AddWebSocket();
        AddBark();
        AddDingDing();
        AddTelegram();
        AddXxtui();
        AddDiscord();
        AddServerChan();
        AddMeow();
        AddGotify();
        AddQq();

        void AddWebhook()
        {
            if (config.WebhookEnabled == true)
                add(new WebhookNotifier(sharedClient, config));
        }

        void AddFeishu()
        {
            if (config.FeishuNotificationEnabled == true)
                add(new FeishuNotifier(
                    sharedClient,
                    config.FeishuWebhookUrl,
                    config.FeishuAppId,
                    config.FeishuAppSecret
                ));
        }

        void AddOneBot()
        {
            if (config.OneBotNotificationEnabled == true)
                add(new OneBotNotifier(
                    sharedClient,
                    config.OneBotEndpoint,
                    config.OneBotUserId,
                    config.OneBotGroupId,
                    config.OneBotToken
                ));
        }

        void AddWorkWeixin()
        {
            if (config.WorkweixinNotificationEnabled == true)
            {
                add(new WorkWeixinNotifier(
                    sharedClient,
                    config.WorkweixinWebhookUrl
                ));
            }
        }

        void AddWebSocket()
        {
            if (config.WebSocketNotificationEnabled == true)
            {
                var jsonSerializerOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                };

                add(new WebSocketNotifier(
                    config.WebSocketEndpoint,
                    jsonSerializerOptions,
                    webSocketCts
                ));
            }
        }

        void AddBark()
        {
            if (config.BarkNotificationEnabled == true)
            {
                add(new BarkNotifier(
                    createClient(),
                    config.BarkDeviceKeys,
                    config.BarkApiEndpoint,
                    config.BarkGroup,
                    config.BarkSound,
                    config.BarkIcon
                ));
            }
        }

        void AddDingDing()
        {
            if (config.DingDingwebhookNotificationEnabled == true &&
                !string.IsNullOrEmpty(config.DingdingWebhookUrl) &&
                !string.IsNullOrEmpty(config.DingDingSecret))
            {
                add(new DingDingWebhook(
                    sharedClient,
                    config.DingdingWebhookUrl,
                    config.DingDingSecret
                ));
            }
        }

        void AddTelegram()
        {
            if (config.TelegramNotificationEnabled == true)
            {
                // 启用 Telegram 独立代理时由通知器自建带代理的客户端，否则使用跟随全局网络设置的共享客户端
                add(new TelegramNotifier(
                    config.TelegramProxyEnabled ? null : sharedClient,
                    config.TelegramBotToken,
                    config.TelegramChatId,
                    config.TelegramApiBaseUrl,
                    config.TelegramProxyUrl,
                    config.TelegramProxyEnabled
                ));
            }
        }

        void AddXxtui()
        {
            if (config.XxtuiNotificationEnabled != true) return;

            var channels = ParseXxtuiChannels(config.XxtuiChannels);

            add(new XxtuiNotifier(
                createClient(),
                config.XxtuiApiKey,
                config.XxtuiFrom,
                channels
            ));
        }

        void AddDiscord()
        {
            if (config.DiscordWebhookNotificationEnabled != true) return;

            if (DiscordNotificationModes.IsBot(config.DiscordNotificationMode))
            {
                add(new DiscordBotNotifier(
                    sharedClient,
                    config.DiscordBotToken,
                    config.DiscordBotTargets,
                    config.DiscordBotMessageFormat,
                    config.DiscordWebhookImageEncoder
                ));
                return;
            }

            add(new DiscordWebhookNotifier(
                sharedClient,
                config.DiscordWebhookUrl,
                config.DiscordWebhookUsername,
                config.DiscordWebhookAvatarUrl,
                config.DiscordWebhookImageEncoder
            ));
        }

        void AddServerChan()
        {
            if (config.ServerChanNotificationEnabled != true) return;

            add(new ServerChanNotifier(
                sharedClient,
                config.ServerChanSendKey
            ));
        }

        void AddMeow()
        {
            if (config.MeowNotificationEnabled != true) return;

            add(new MeowNotifier(
                sharedClient,
                config.MeowNickname,
                config.MeowTitle
            ));
        }

        void AddGotify()
        {
            if (config.GotifyNotificationEnabled != true) return;

            add(new GotifyNotifier(
                sharedClient,
                config.GotifyUrl,
                config.GotifyAppToken,
                config.GotifyNotifyLevel
            ));
        }

        void AddQq()
        {
            if (config.QqNotificationEnabled != true) return;

            add(new QqNotifier(
                sharedClient,
                config.QqAppId,
                config.QqClientSecret,
                config.QqOpenId,
                config.QqGroupOpenId,
                config.QqMessageFormat
            ));
        }

        return result;
    }

    /// <summary>解析信息推送通知渠道配置（逗号分隔；无有效项时为 WX_MP）</summary>
    public static XxtuiChannel[] ParseXxtuiChannels(string channelsStr)
    {
        if (string.IsNullOrEmpty(channelsStr)) return new[] { XxtuiChannel.WX_MP };

        var channelStrings = channelsStr.Split(',');
        var validChannels = new XxtuiChannel[channelStrings.Length];
        var validCount = 0;

        foreach (var channel in channelStrings)
            if (Enum.TryParse<XxtuiChannel>(channel.Trim(), out var parsedChannel))
                validChannels[validCount++] = parsedChannel;

        if (validCount < channelStrings.Length) Array.Resize(ref validChannels, validCount);

        return validChannels.Length > 0 ? validChannels : new[] { XxtuiChannel.WX_MP };
    }
}
