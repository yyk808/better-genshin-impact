using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using Avalonia.Controls;
using BetterGenshinImpact.Service.ExternalAccess;
using Microsoft.Extensions.DependencyInjection;

namespace BetterGenshinImpact.Avalonia;

internal sealed partial class UiTests
{
    /// <summary>
    /// 本机外部访问服务（--ui-test external-access）：启动前由外部脚本在 config.json 打开 HTTP API / WebSocket / MCP 并设端口（BGI_TEST_EXTERNAL_PORT）。
    /// 检查服务在根实例上运行、设置页卡片与配置一致、令牌脱敏；HTTP 无令牌 401、带令牌 health / version、非 loopback Host 400；
    /// WebSocket 收到实时日志；MCP（Streamable HTTP，无状态）tools/list 与 tools/call get_bgi_health；重新生成令牌后旧令牌立即失效。
    /// 只连 127.0.0.1，不联网；config.json 由外部脚本备份恢复
    /// </summary>
    private async Task ExternalAccessAsync()
    {
        var host = vm.Host;
        if (host is null) { Check("宿主", false); return; }
        var port = int.TryParse(Environment.GetEnvironmentVariable("BGI_TEST_EXTERNAL_PORT"), out var p) ? p : 18765;
        var cfg = host.Config.ExternalAccessConfig;
        var state = host.Services.GetService<ExternalAccessState>();
        Check("配置已由脚本打开三项并设端口", cfg is { HttpApiEnabled: true, WebSocketEnabled: true, McpEnabled: true } && cfg.Port == port, $"{cfg.HttpApiEnabled}/{cfg.WebSocketEnabled}/{cfg.McpEnabled} {cfg.Port}");
        Check("服务在根实例上运行", await Until(() => state?.Status == ExternalAccessServiceStatus.Running, 10000), $"{state?.StatusText} {state?.Message}");
        Check("访问地址只在 loopback", state?.Address == $"http://127.0.0.1:{port}", state?.Address);
        var token = cfg.AccessToken;
        Check("启动时生成令牌（43 位 Base64Url）", token is { Length: 43 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'), token?.Length);

        vm.Navigate("设置");
        await Settle(800);
        var card = await WaitFor(() => Named<FluentAvalonia.UI.Controls.FASettingsExpander>("ExternalAccessCard"));
        if (card is not null) card.IsExpanded = true;
        await Settle(600);
        Check("设置页有外部访问卡片，开关与端口同配置", card is not null && Named<ToggleSwitch>("ExternalHttpToggle")?.IsChecked == true
            && Named<ToggleSwitch>("ExternalWebSocketToggle")?.IsChecked == true && Named<ToggleSwitch>("ExternalMcpToggle")?.IsChecked == true
            && Named<NumericUpDown>("ExternalPortBox")?.Value == port);
        Check("令牌脱敏显示", Named<TextBlock>("ExternalTokenText")?.Text == $"{token![..4]}…{token[^4..]}", Named<TextBlock>("ExternalTokenText")?.Text);
        Check("状态与地址显示", Named<TextBlock>("ExternalStatusText")?.Text == "运行中" && Named<SelectableTextBlock>("ExternalAddressText")?.Text == state?.Address);
        card?.BringIntoView();
        await Settle(300);
        Shot("external-access-settings.png");

        using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(10) };
        async Task<(HttpStatusCode, string)> Get(string path, string? bearer, string? hostHeader = null)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, path);
            if (bearer is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            if (hostHeader is not null) req.Headers.Host = hostHeader;
            using var res = await http.SendAsync(req);
            return (res.StatusCode, await res.Content.ReadAsStringAsync());
        }

        var (s1, _) = await Get("/api/v1/health", null);
        Check("无令牌 401", s1 == HttpStatusCode.Unauthorized, s1);
        var (s2, b2) = await Get("/api/v1/health", token);
        var health = s2 == HttpStatusCode.OK ? Newtonsoft.Json.Linq.JObject.Parse(b2) : null;
        Check("带令牌 health：healthy、进程号为本进程", health?["status"]?.ToString() == "healthy" && health["processId"]?.ToObject<int>() == Environment.ProcessId, b2);
        var (s3, b3) = await Get("/api/v1/version", token);
        Check("version：BetterGI / v1", s3 == HttpStatusCode.OK && b3.Contains("\"product\":\"BetterGI\"") && b3.Contains("\"apiVersion\":\"v1\""), b3);
        var (s4, _) = await Get("/api/v1/health", token, "evil.example");
        Check("非 loopback Host 400（防 DNS 重绑定）", s4 == HttpStatusCode.BadRequest, s4);

        // WebSocket 实时日志：连接后写一条带标记的日志，应收到
        using (var ws = new ClientWebSocket())
        {
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws/v1/logs"), CancellationToken.None);
            var marker = $"m64-external-{Guid.NewGuid():N}";
            var received = new StringBuilder();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var buf = new byte[64 * 1024];
            var reader = Task.Run(async () =>
            {
                try
                {
                    while (!received.ToString().Contains(marker))
                    {
                        var r = await ws.ReceiveAsync(buf, cts.Token);
                        if (r.MessageType == WebSocketMessageType.Close) break;
                        received.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                    }
                }
                catch (OperationCanceledException)
                {
                }
            });
            await Task.Delay(300);
            log.Information("外部访问自测日志 {Marker}", marker);
            await reader;
            Check("WebSocket 收到实时日志", received.ToString().Contains(marker), received.Length);
            if (ws.State == WebSocketState.Open) await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        }

        async Task<(HttpStatusCode, string)> Mcp(string body, string? bearer)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            req.Headers.Accept.ParseAdd("application/json");
            req.Headers.Accept.ParseAdd("text/event-stream");
            if (bearer is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            using var res = await http.SendAsync(req);
            return (res.StatusCode, await res.Content.ReadAsStringAsync());
        }

        var (m0, _) = await Mcp("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", null);
        Check("MCP 无令牌 401", m0 == HttpStatusCode.Unauthorized, m0);
        var (m1, list) = await Mcp("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""", token);
        Check("MCP tools/list 有两个工具", m1 == HttpStatusCode.OK && list.Contains("get_bgi_health") && list.Contains("get_bgi_version"), list.Length > 300 ? list[..300] : list);
        var (m2, call) = await Mcp("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"get_bgi_health","arguments":{}}}""", token);
        Check("MCP tools/call get_bgi_health", m2 == HttpStatusCode.OK && call.Contains("healthy"), call.Length > 300 ? call[..300] : call);

        var regenerate = Named<Button>("ExternalRegenerateButton");
        await Click(regenerate, "重新生成");
        await Settle(600);
        var newToken = cfg.AccessToken;
        Check("重新生成后令牌改变、脱敏显示更新", newToken != token && Named<TextBlock>("ExternalTokenText")?.Text == $"{newToken[..4]}…{newToken[^4..]}");
        var (s5, _) = await Get("/api/v1/health", token);
        var (s6, _) = await Get("/api/v1/health", newToken);
        Check("旧令牌立即失效、新令牌可用", s5 == HttpStatusCode.Unauthorized && s6 == HttpStatusCode.OK, $"{s5} {s6}");
    }
}
