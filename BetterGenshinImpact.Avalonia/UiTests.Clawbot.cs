using System.Net.Http;
using System.Reflection;
using Avalonia.Controls;
using BetterGenshinImpact.Avalonia.ViewModels;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Host.Common.Notification;
using Microsoft.Extensions.DependencyInjection;

namespace BetterGenshinImpact.Avalonia;

internal sealed partial class UiTests
{
    /// <summary>
    /// 设置页“微信 Clawbot 通知”（--ui-test notify-clawbot，同 WPF）：卡片控件；未启用 / 未登录时的测试提示；
    /// 登录并绑定（二维码链接交给浏览器打开，同 WPF）→ 显示验证码 → 只接受扫码用户发来的验证码 → 一次性保存凭证；测试发送；
    /// 推送会话过期提示；绑定中可取消且不改动凭证。
    /// 全部请求只发往本机假 iLink 服务（127.0.0.1:18046，外部脚本），登录接口的固定主机在测试期间改写到假服务，其他主机一律拒绝
    /// </summary>
    private async Task NotifyClawbotAsync()
    {
        var settings = vm.Settings;
        var n = settings.Config.NotificationConfig;
        var outDir = Environment.GetEnvironmentVariable("FAKE_ILINK_OUT") ?? "/tmp/m64-ilink";
        var others = typeof(BetterGenshinImpact.Service.Notification.NotificationConfig).GetProperties()
            .Where(p => p.PropertyType == typeof(bool) && p.Name.EndsWith("NotificationEnabled") && p.Name is not ("WechatClawbotNotificationEnabled" or "JsNotificationEnabled"))
            .Where(p => (bool)p.GetValue(n)!).Select(p => p.Name).ToList();
        others.AddRange(n.WebhookEnabled ? ["WebhookEnabled"] : []);
        Check("其它通知渠道均未启用", others.Count == 0, string.Join(",", others));
        if (others.Count > 0) return;

        var saved = (n.WechatClawbotNotificationEnabled, n.WechatClawbotBotToken, n.WechatClawbotToUserId, n.WechatClawbotBaseUrl);
        var storeDir = Global.Absolute(Path.Combine("User", "WechatClawbot"));
        var storeExisted = Directory.Exists(storeDir);
        var open0 = SettingsPageViewModel.OpenWechatClawbotQrCode;
        var opened = new List<string>();
        SettingsPageViewModel.OpenWechatClawbotQrCode = opened.Add;
        var providerField = typeof(CoreServices).GetField("_providerFactory", BindingFlags.NonPublic | BindingFlags.Static)!;
        var activeField = typeof(CoreServices).GetField("_isHostActive", BindingFlags.NonPublic | BindingFlags.Static)!;
        var provider0 = (Func<IServiceProvider>?)providerField.GetValue(null);
        var active0 = (Func<bool>?)activeField.GetValue(null);
        var fakeFactory = new LocalOnlyHttpClientFactory();
        CoreServices.SetProvider(() => new FakeHttpProvider(provider0?.Invoke(), fakeFactory), active0);
        using var control = new HttpClient(new LocalOnlyHandler()) { BaseAddress = new Uri("http://127.0.0.1:18046") };
        try
        {
            await control.PostAsync("/test/reset", new StringContent("{}"));
            n.WechatClawbotNotificationEnabled = false;
            n.WechatClawbotBotToken = n.WechatClawbotToUserId = n.WechatClawbotBaseUrl = "";
            await WaitFor(() => vm.Host?.RuntimeService.Current);
            vm.Navigate("设置");
            await Settle(800);
            var tab = All<TabItem>().FirstOrDefault(t => UiText(t.Header) == "通知");
            if (tab is not null) tab.IsSelected = true;
            await Settle(800);
            Named<Button>("WechatClawbotTestButton")?.BringIntoView();
            await Settle(400);
            Shot("avalonia-notify-clawbot.png");
            var bindButton = Named<Button>("WechatClawbotBindButton");
            Check("微信 Clawbot 卡片：开关、BotToken、目标用户 ID、登录并绑定、测试（同 WPF）",
                Named<ToggleSwitch>("WechatClawbotSwitch") is not null && Named<TextBox>("WechatClawbotTokenBox") is not null && Named<TextBox>("WechatClawbotUserBox") is not null
                && bindButton is { Content: "登录并绑定" } && Named<Button>("WechatClawbotTestButton") is { Content: "发送" });

            async Task<string> Test(string what)
            {
                settings.WechatClawbotStatus = "";
                await Click(Named<Button>("WechatClawbotTestButton"), what);
                await WaitFor(() => string.IsNullOrEmpty(settings.WechatClawbotStatus) ? null : "ok");
                await Settle(300);
                return settings.WechatClawbotStatus;
            }

            var s = await Test("测试 Clawbot（未启用）");
            Check("未启用时提示", s.Contains("未启用"), s);
            Named<ToggleSwitch>("WechatClawbotSwitch")!.IsChecked = true;
            await Settle(300);
            s = await Test("测试 Clawbot（未登录）");
            Check("未登录时提示先扫码登录（同 WPF）", s.Contains("BotToken 为空"),
                $"{s} enabled={n.WechatClawbotNotificationEnabled} notifiers={string.Join(",", vm.Host?.Services.GetService<LinuxNotificationService>()?.NotifierNames ?? [])}");

            // 登录并绑定
            await Click(bindButton, "登录并绑定");
            var codeStatus = await WaitFor(() => settings.WechatClawbotStatus.StartsWith("请给 Clawbot 发送验证码 [") ? settings.WechatClawbotStatus : null);
            Check("二维码链接交给浏览器打开（同 WPF）", opened.SequenceEqual(["https://liteapp.weixin.qq.com/q/m64qr?bot_type=3"]), string.Join(",", opened));
            Check("绑定中按钮变为“取消”", UiText(bindButton?.Content) == "取消");
            var code = codeStatus is null ? "" : codeStatus.Split('[', ']')[1];
            Check("显示 4 位验证码", code.Length == 4 && code.All(char.IsDigit), codeStatus);
            bindButton?.BringIntoView();
            await Settle(300);
            Shot("avalonia-notify-clawbot-code.png");
            await control.PostAsync("/test/code", new StringContent($"{{\"code\":\"{code}\"}}"));
            await WaitFor(() => settings.WechatClawbotStatus == "绑定成功" || settings.WechatClawbotStatus.StartsWith("登录/绑定失败") ? "ok" : null);
            Check("绑定成功并一次性保存凭证（只接受扫码用户的验证码）", settings.WechatClawbotStatus == "绑定成功" && n.WechatClawbotBotToken == "m64-bot-token"
                && n.WechatClawbotToUserId == "m64user@im.wechat" && n.WechatClawbotBaseUrl == "http://127.0.0.1:18046", $"{settings.WechatClawbotStatus} {n.WechatClawbotToUserId}");
            Check("会话令牌存入 User/WechatClawbot", Directory.Exists(storeDir) && Directory.EnumerateFiles(storeDir).Any());
            Check("绑定结束按钮恢复“登录并绑定”", UiText(bindButton?.Content) == "登录并绑定");

            // 测试发送：发往登录返回的地址，带 bot_token 与绑定时的 context_token
            s = await Test("测试 Clawbot");
            Check("测试发送成功", s == "测试通知发送成功", s);
            var log = File.Exists(Path.Combine(outDir, "ilink.jsonl")) ? File.ReadAllLines(Path.Combine(outDir, "ilink.jsonl")) : [];
            Check("假服务收到文本消息（目标用户、context_token、Bearer bot_token）", log.Any(l => l.Contains("/ilink/bot/sendmessage") && l.Contains("m64user@im.wechat")
                && l.Contains("m64-ctx") && l.Contains("Bearer m64-bot-token") && l.Contains("[BetterGI]")), $"{log.Length} 条");

            // 推送会话过期
            await control.PostAsync("/test/expire", new StringContent("{}"));
            s = await Test("测试 Clawbot（会话过期）");
            await WaitFor(() => settings.WechatClawbotStatus.Contains("推送权限已过期") ? "ok" : null);
            Check("推送会话过期时提示在微信侧私聊激活（同 WPF）", settings.WechatClawbotStatus.Contains("推送权限已过期"), settings.WechatClawbotStatus);

            // 绑定中取消：不改动已保存的凭证
            await control.PostAsync("/test/reset", new StringContent("{}"));
            await Click(bindButton, "再次登录并绑定");
            await WaitFor(() => settings.WechatClawbotStatus.StartsWith("请给 Clawbot 发送验证码 [") ? "ok" : null);
            await Click(bindButton, "取消");
            await WaitFor(() => settings.WechatClawbotStatus == "已取消登录/绑定" ? "ok" : null);
            Check("取消后提示且凭证不变", settings.WechatClawbotStatus == "已取消登录/绑定" && n.WechatClawbotBotToken == "m64-bot-token" && UiText(bindButton?.Content) == "登录并绑定",
                settings.WechatClawbotStatus);
            Check("测试期间没有访问外部地址", fakeFactory.Blocked.Count == 0, string.Join(",", fakeFactory.Blocked));
        }
        finally
        {
            (n.WechatClawbotNotificationEnabled, n.WechatClawbotBotToken, n.WechatClawbotToUserId, n.WechatClawbotBaseUrl) = saved;
            vm.Host?.Services.GetService<LinuxNotificationService>()?.RefreshNotifiers();
            await Settle(500);
            CoreServices.SetProvider(provider0 ?? (() => null!), active0);
            SettingsPageViewModel.OpenWechatClawbotQrCode = open0;
            if (!storeExisted && Directory.Exists(storeDir)) Directory.Delete(storeDir, recursive: true);
        }
    }

    /// <summary>只允许 127.0.0.1；iLink 登录接口的固定主机改写到本机假服务，其他外部主机直接拒绝</summary>
    private sealed class LocalOnlyHandler(List<string>? blocked = null) : DelegatingHandler(new SocketsHttpHandler())
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            if (uri.Host is "ilinkai.weixin.qq.com" or "novac2c.cdn.weixin.qq.com")
            {
                request.RequestUri = new UriBuilder(uri) { Scheme = "http", Host = "127.0.0.1", Port = 18046 }.Uri;
            }
            else if (uri.Host != "127.0.0.1")
            {
                lock (blocked ?? []) blocked?.Add(uri.Host);
                throw new HttpRequestException("自测禁止访问外部地址：" + uri.Host);
            }

            return base.SendAsync(request, ct);
        }
    }

    private sealed class LocalOnlyHttpClientFactory : IHttpClientFactory
    {
        public List<string> Blocked { get; } = [];
        public HttpClient CreateClient(string name) => new(new LocalOnlyHandler(Blocked)) { Timeout = TimeSpan.FromSeconds(60) };
    }

    private sealed class FakeHttpProvider(IServiceProvider? inner, IHttpClientFactory factory) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IHttpClientFactory) ? factory : inner?.GetService(serviceType);
    }
}
