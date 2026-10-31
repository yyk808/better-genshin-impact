using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using BetterGenshinImpact.Avalonia.ViewModels;
using BetterGenshinImpact.Avalonia.Views;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using Newtonsoft.Json.Linq;
using Serilog;

namespace BetterGenshinImpact.Avalonia;

/// <summary>
/// 宿主内界面自测（--ui-test script-settings | taskplan）：在真实窗口中按名称/文字找到控件，
/// 像用户一样点击按钮、选择下拉、勾选、输入，然后直接读取磁盘上的配置文件核对保存结果。
/// 每项检查在日志中输出“UI 自测 PASS/FAIL”，结束时汇总
/// </summary>
internal sealed class UiTests(MainWindow window, MainWindowViewModel vm, ILogger log, string? dir)
{
    private int _pass;
    private int _fail;

    public async Task RunAsync(string name)
    {
        try
        {
            switch (name)
            {
                case "script-settings": await ScriptSettingsAsync(); break;
                case "taskplan": await TaskPlanAsync(); break;
                case "html-settings": await HtmlSettingsAsync(); break;
                case "html-mask": await HtmlMaskAsync(); break;
                case "quest-voice": await QuestVoiceAsync(); break;
                case "log-file": await LogFileAsync(); break;
                case "quest-voice-settings": await QuestVoiceSettingsAsync(); break;
                default: log.Warning("未知的界面自测 {Name}", name); break;
            }
        }
        catch (Exception e)
        {
            _fail++;
            log.Error(e, "UI 自测异常");
        }

        log.Information("UI 自测 {Name} 结束：PASS {Pass} FAIL {Fail}", name, _pass, _fail);
    }

    // —— 工具 ——

    private void Check(string what, bool ok, object? detail = null)
    {
        if (ok) _pass++;
        else _fail++;
        log.Information("UI 自测 {Result} {What} {Detail}", ok ? "PASS" : "FAIL", what, detail);
    }

    private IEnumerable<T> All<T>() where T : Control => window.GetVisualDescendants().OfType<T>();

    private T? Named<T>(string name) where T : Control => All<T>().FirstOrDefault(c => c.Name == name && c.IsEffectivelyVisible);

    private Button? ButtonByText(string text) => All<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.Content as string == text);

    private static readonly MethodInfo OnClickMethod = typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>与鼠标点击相同：引发 Click 事件并执行按钮命令（禁用的按钮不响应）</summary>
    private async Task Click(Button? button, string what)
    {
        if (button is null || !button.IsEffectivelyEnabled)
        {
            Check($"点击 {what}", false, button is null ? "找不到按钮" : "按钮不可用");
            return;
        }

        OnClickMethod.Invoke(button, null);
        await Settle();
    }

    /// <summary>等待控件出现（界面切换后布局可能稍晚完成），最多 5 秒</summary>
    private static async Task<T?> WaitFor<T>(Func<T?> find) where T : class
    {
        for (var i = 0; i < 50; i++)
        {
            if (find() is { } found) return found;
            await Task.Delay(100);
        }

        return null;
    }

    private static async Task Settle(int ms = 400) => await Task.Delay(ms);

    private void Shot(string file)
    {
        if (dir is null) return;
        Directory.CreateDirectory(dir);
        AvaloniaHostProgram.SaveScreenshotPublic(window, Path.Combine(dir, file), log);
    }

    // —— 日志文件 ——

    /// <summary>
    /// 日志文件（--ui-test log-file）：启动前的日志也写入 log 目录的当天文件；在设置页关闭“保存日志文件”后不再写入，重新打开后恢复。
    /// 开关写入 linux-host.json，自测前后由外部脚本备份恢复
    /// </summary>
    private async Task LogFileAsync()
    {
        static string Read(string? path)
        {
            if (path is null) return "";
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return new StreamReader(stream).ReadToEnd();
        }

        var file = BetterGenshinImpact.Host.Common.HostLogging.FileLog.LatestFile();
        Check("日志文件存在", file is not null && File.Exists(file), file);
        Check("启动阶段的日志已写入", Read(file).Contains("Linux 宿主设置") || Read(file).Contains("配置文件"));

        vm.Navigate("设置");
        await Settle(800);
        var toggle = await WaitFor(() => Named<ToggleSwitch>("LogFileToggle"));
        Check("设置页有日志文件开关", toggle is not null);
        if (toggle is null) return;
        var marker1 = "LOGFILE-MARKER-ON-" + Guid.NewGuid().ToString("N")[..8];
        log.Information(marker1);
        toggle.IsChecked = false;
        await Settle();
        var marker2 = "LOGFILE-MARKER-OFF-" + Guid.NewGuid().ToString("N")[..8];
        log.Information(marker2);
        toggle.IsChecked = true;
        await Settle();
        var marker3 = "LOGFILE-MARKER-AGAIN-" + Guid.NewGuid().ToString("N")[..8];
        log.Information(marker3);
        await Settle(1500);
        var text = Read(BetterGenshinImpact.Host.Common.HostLogging.FileLog.LatestFile());
        Check("开启时写入", text.Contains(marker1));
        Check("关闭后不写入", !text.Contains(marker2));
        Check("重新开启后写入", text.Contains(marker3));
        Shot("settings-logfile.png");
    }

    // —— 自动配音设置窗口 ——

    private static IEnumerable<T> AllIn<T>(Visual root) where T : Control => root.GetVisualDescendants().OfType<T>();

    private static T? NamedIn<T>(Visual root, string name) where T : Control =>
        AllIn<T>(root).FirstOrDefault(c => c.Name == name && c.IsEffectivelyVisible);

    private static JObject ReadJson(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return JObject.Parse(new StreamReader(stream).ReadToEnd());
    }

    /// <summary>
    /// 自动配音设置窗口（--ui-test quest-voice-settings）：从实时触发页卡片打开设置窗口，核对引擎列表（Linux 不列出 Windows SAPI、
    /// espeak-ng 以通用设置追加）、设为默认引擎、修改语速与 VITS 地址（写入 config.json）；把 espeak-ng 声音添加为声线，
    /// 在“角色”页新增角色并分配该声线，等待自动保存后读取磁盘上的角色目录核对；删除角色（确认框由自测替代）、导出草稿；
    /// 识别 / 高级页修改采样间隔与超时。角色目录指向输出目录中的副本，不改动程序目录下的 routing.json；config.json 由外部脚本备份恢复
    /// </summary>
    private async Task QuestVoiceSettingsAsync()
    {
        var work = Path.Combine(dir ?? Path.GetTempPath(), "qv-settings");
        Directory.CreateDirectory(work);
        var catalog = Path.Combine(work, "routing.json");
        foreach (var f in Directory.GetFiles(work)) File.Delete(f);
        var seed = Path.Combine(Global.StartUpPath, "User", "QuestVoice", "routing.json");
        if (File.Exists(seed)) File.Copy(seed, catalog, true);
        var configPath = Path.Combine(Global.StartUpPath, "User", "config.json");

        vm.Navigate("实时触发");
        await Settle(800);
        var qv = vm.Trigger.QuestVoice;
        Check("卡片绑定 Core 服务", qv is not null);
        if (qv is null) return;
        qv.Config.RoutingCatalogPath = catalog;
        var card = await WaitFor(() => Named<Expander>("QuestVoiceCard"));
        if (card is null) return;
        card.IsExpanded = true;
        card.BringIntoView();
        await Settle(600);
        var open = await WaitFor(() => Named<Button>("QuestVoiceSettingsButton"));
        Check("卡片有“设置…”按钮", open is not null);
        if (open is null) return;
        Services.AvaloniaQuestVoiceSettingsUi.TestConfirm = message =>
        {
            log.Information("确认框：{Message}", message);
            return true;
        };
        var exportPath = Path.Combine(work, "routing.export.json");
        Services.AvaloniaQuestVoiceSettingsUi.TestPickFile = title => title.Contains("导出") ? exportPath : null;
        try
        {
            OnClickMethod.Invoke(open, null);
            var win = await WaitFor(() => QuestVoiceSettingsWindow.Current);
            Check("设置窗口打开", win is not null);
            if (win is null) return;
            await Settle(1500);
            var settings = (BetterGenshinImpact.ViewModel.Pages.QuestVoiceSettingsViewModel)win.DataContext!;
            Check("角色目录已加载", settings.Management.IsLoaded, settings.Management.SaveStatus);
            Check("设置期间配音暂停（编辑可用）", settings.IsConfigEditable);

            // —— 引擎与声线 ——
            var ids = settings.Engines.Select(e => e.Provider.Id).ToList();
            log.Information("引擎列表：{Engines}", string.Join(", ", ids));
            Check("引擎列表以 GPT-SoVITS 开头", ids.FirstOrDefault() == "gpt-sovits");
            Check("Linux 不列出 Windows SAPI", !ids.Contains("windows-sapi"));
            var espeak = settings.Engines.FirstOrDefault(e => e.Provider.Id == "espeak-ng");
            Check("espeak-ng 以通用设置追加", espeak?.Settings is BetterGenshinImpact.ViewModel.Pages.QuestVoiceSettings.GenericEngineSettingsViewModel);
            Shot(win, "qvs-engine-gpt.png");

            var list = NamedIn<ListBox>(win, "EngineList");
            if (espeak is not null && list is not null)
            {
                list.SelectedItem = espeak;
                await Settle(2500);
                var engine = (BetterGenshinImpact.ViewModel.Pages.QuestVoiceSettings.EngineSettingsViewModel)espeak.Settings;
                log.Information("espeak-ng 声音 {Count} 项：{Status}", engine.Voices.Count, engine.StatusMessage);
                Check("进入 espeak-ng 页自动读取声音列表", engine.Voices.Count > 0, engine.StatusMessage);
                var slider = NamedIn<Slider>(win, "RateSlider");
                Check("语速滑块显示", slider is not null);
                if (slider is not null) slider.Value = 1.3;
                await Click(NamedIn<Button>(win, "SetDefaultEngine"), "设为默认引擎");
                Check("默认引擎改为 espeak-ng", settings.Config.ProviderId == "espeak-ng" && espeak.IsDefault, settings.Config.ProviderId);
                var name = NamedIn<TextBox>(win, "GenericVoiceId");
                Check("声音 ID 输入框显示当前声音", name?.Text == engine.VoiceId, name?.Text);
                engine.VoiceDisplayName = "自测声线";
                await Click(NamedIn<Button>(win, "RegisterVoice"), "添加为声线");
                await Settle(1200);
                Check("添加为声线", settings.Management.Profiles.Any(p => p.ProviderId == "espeak-ng" && p.Name == "自测声线"), engine.StatusMessage);
                Shot(win, "qvs-engine-espeak.png");
            }

            var vits = settings.Engines.FirstOrDefault(e => e.Provider.Id == "vits-simple-api");
            if (vits is not null && list is not null)
            {
                list.SelectedItem = vits;
                await Settle(800);
                var url = NamedIn<TextBox>(win, "LocalUrl");
                Check("VITS 页显示服务地址", url is not null);
                if (url is not null)
                {
                    url.Text = "ftp://bad";
                    await Settle();
                    var engine = (BetterGenshinImpact.ViewModel.Pages.QuestVoiceSettings.EngineSettingsViewModel)vits.Settings;
                    Check("非法地址提示且不写入", engine.UrlValidationMessage.Length > 0 && engine.EngineConfig?.LocalUrl != "ftp://bad");
                    url.Text = "http://127.0.0.1:23999/";
                    await Settle();
                    Check("合法地址写入引擎配置", engine.EngineConfig?.LocalUrl == "http://127.0.0.1:23999/" && engine.UrlValidationMessage.Length == 0);
                }
                Shot(win, "qvs-engine-vits.png");
            }

            // —— 角色 ——
            var tabs = NamedIn<TabControl>(win, "Tabs")!;
            tabs.SelectedIndex = 1;
            await Settle(800);
            var routing = NamedIn<ToggleSwitch>(win, "RoutingEnabled");
            if (routing is not null) routing.IsChecked = true;
            await Click(NamedIn<Button>(win, "AddSpeaker"), "添加角色");
            var m = settings.Management;
            var added = m.SelectedSpeaker;
            Check("新增角色并选中", added is not null, added?.Id);
            var names = await WaitFor(() => NamedIn<TextBox>(win, "SpeakerNames"));
            if (names is not null) names.Text = "自测角色";
            var voiceBox = NamedIn<ComboBox>(win, "SpeakerVoice");
            var profile = m.Profiles.FirstOrDefault(p => p.Name == "自测声线");
            if (voiceBox is not null && profile is not null)
                voiceBox.SelectedItem = m.VoiceChoices.FirstOrDefault(c => c.Id == profile.OriginalId);
            await Settle(400);
            Check("角色分配声线", added is not null && profile is not null && added.VoiceId == profile.OriginalId, added?.VoiceId);
            var player = NamedIn<TextBox>(win, "PlayerNames");
            if (player is not null) player.Text = "自测旅行者";
            NamedIn<ListBox>(win, "SpeakerList")?.BringIntoView();
            await Settle(2000);
            Shot(win, "qvs-roles.png");
            var json = ReadJson(catalog);
            var speaker = json["Speakers"]?.FirstOrDefault(x => x["Names"]?.Any(n => (string?)n == "自测角色") == true);
            Check("角色目录自动保存：新角色", speaker is not null, m.SaveStatus);
            Check("角色目录自动保存：声线引用", speaker is not null && (string?)speaker["VoiceId"] == profile?.OriginalId);
            Check("角色目录自动保存：espeak-ng 声线",
                json["Voices"]?.Any(x => (string?)x["ProviderId"] == "espeak-ng" && (string?)x["Name"] == "自测声线") == true);
            Check("角色目录自动保存：旅行者昵称", json["PlayerNames"]?.Any(x => (string?)x == "自测旅行者") == true);
            Check("备份文件已保留", Directory.GetFiles(work).Any(f => f != catalog && Path.GetFileName(f).StartsWith("routing.json")),
                string.Join(", ", Directory.GetFiles(work).Select(Path.GetFileName)));

            // 删除新角色（确认框由自测替代）
            var speakerList = NamedIn<ListBox>(win, "SpeakerList");
            if (speakerList is not null && added is not null) speakerList.SelectedItem = added;
            await Click(NamedIn<Button>(win, "DeleteSpeaker"), "删除角色");
            await Settle(2000);
            Check("删除角色后自动保存", ReadJson(catalog)["Speakers"]?.Any(x => x["Names"]?.Any(n => (string?)n == "自测角色") == true) == false, m.SaveStatus);

            // —— 识别 / 高级 ——
            tabs.SelectedIndex = 2;
            await Settle(600);
            var sample = NamedIn<NumericUpDown>(win, "SampleInterval");
            if (sample is not null) sample.Value = 200;
            Shot(win, "qvs-ocr.png");
            tabs.SelectedIndex = 3;
            await Settle(600);
            var connect = NamedIn<NumericUpDown>(win, "ConnectTimeout");
            if (connect is not null) connect.Value = 15;
            await Click(NamedIn<Button>(win, "ValidateCatalog"), "校验目录");
            Check("校验目录通过", m.EditStatus.Contains("校验通过"), m.EditStatus);
            await Click(NamedIn<Button>(win, "ExportCatalog"), "导出");
            await Settle(800);
            Check("导出草稿", File.Exists(exportPath), m.EditStatus);
            Shot(win, "qvs-advanced.png");
            Check("采样间隔与超时写入配置", settings.Config.SampleIntervalMilliseconds == 200 && settings.Config.ConnectTimeoutSeconds == 15);

            win.Close();
            await Settle(1500);
            Check("关闭窗口后无未保存草稿", !qv.HasCommandError, qv.CommandError);
            // 配置文件自动保存（AllConfig 防抖写盘）
            await Task.Delay(3000);
            var config = ReadJson(configPath)["questVoiceConfig"];
            Check("config.json：默认引擎", (string?)config?["providerId"] == "espeak-ng", (string?)config?["providerId"]);
            Check("config.json：采样间隔 / 超时", (int?)config?["sampleIntervalMilliseconds"] == 200 && (int?)config?["connectTimeoutSeconds"] == 15);
            Check("config.json：角色声线开关", (bool?)config?["routingEnabled"] == true);
            Check("config.json：espeak-ng 语速", Math.Abs(((double?)config?["extraProviders"]?["espeak-ng"]?["rate"] ?? 0) - 1.3) < 0.01,
                (string?)config?["extraProviders"]?["espeak-ng"]?["rate"]);
            Check("config.json：VITS 地址", (string?)config?["extraProviders"]?["vits-simple-api"]?["localUrl"] == "http://127.0.0.1:23999/");
            Check("config.json：角色目录指向自测副本", (string?)config?["routingCatalogPath"] == catalog);
        }
        finally
        {
            Services.AvaloniaQuestVoiceSettingsUi.TestConfirm = null;
            Services.AvaloniaQuestVoiceSettingsUi.TestPickFile = null;
            QuestVoiceSettingsWindow.Current?.Close();
        }
    }

    private void Shot(Window target, string file)
    {
        if (dir is null) return;
        Directory.CreateDirectory(dir);
        AvaloniaHostProgram.SaveScreenshotPublic(target, Path.Combine(dir, file), log);
    }

    // —— 自动配音卡片 ——

    /// <summary>
    /// 自动配音（--ui-test quest-voice）：实时触发页的卡片显示、展开、校验配置；打开开关后服务随截图器启动、关闭后停止。
    /// 开关写入 config.json，自测前后由外部脚本备份恢复
    /// </summary>
    private async Task QuestVoiceAsync()
    {
        vm.Navigate("实时触发");
        await Settle(800);
        var card = await WaitFor(() => Named<Expander>("QuestVoiceCard"));
        Check("自动配音卡片显示", card is not null);
        var qv = vm.Trigger.QuestVoice;
        Check("卡片绑定 Core 服务", qv is not null);
        if (card is null || qv is null) return;
        if (ButtonByText("收起") is { } collapseLog)
        {
            // 收起日志面板，截图与原版对照
            await Click(collapseLog, "收起日志");
        }

        card.IsExpanded = true;
        card.BringIntoView();
        await Settle(800);
        Shot("questvoice-stopped.png");
        Check("初始状态已停止", !qv.Status.IsRunning, qv.Status.OverallStatus);

        await Click(All<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.Content as string == "校验配置"), "校验配置");
        await Task.Delay(3000);
        log.Information("校验结果：{Validation}", qv.Status.Validation);
        Check("校验配置有结果", qv.Status.Validation != "尚未校验", qv.Status.Validation);

        var toggle = card.GetVisualDescendants().OfType<ToggleSwitch>().FirstOrDefault();
        Check("找到开关", toggle is not null);
        if (toggle is null) return;
        toggle.IsChecked = true;
        var changed = false;
        for (var i = 0; i < 100 && !changed; i++)
        {
            await Task.Delay(100);
            changed = qv.Status.IsRunning || qv.Status.IsBusy || qv.Status.HasError || qv.Status.Session != "已停止";
        }

        await Task.Delay(3000);
        log.Information("开启后状态：{Overall} / {Session} / {Game} / {Error} / {Summary}", qv.Status.OverallStatus, qv.Status.Session, qv.Status.Game, qv.Status.Error, qv.Status.StageSummary);
        Check("打开开关后服务响应", changed && qv.Config.Enabled, qv.Status.Session);
        Shot("questvoice-enabled.png");

        toggle.IsChecked = false;
        var stopped = false;
        for (var i = 0; i < 100 && !stopped; i++)
        {
            await Task.Delay(100);
            stopped = !qv.Status.IsRunning && !qv.Status.IsBusy;
        }

        Check("关闭开关后停止", stopped && !qv.Config.Enabled, qv.Status.Session);
        await Settle(800);
        Shot("questvoice-disabled.png");
    }

    // —— HTML 遮罩 ——

    private const string MaskTestPage = """
        <!doctype html><html><head><meta charset="utf-8"><style>html,body{background:transparent;margin:0;height:100%}</style></head><body>
        <div id="red" style="position:absolute;left:100px;top:100px;width:300px;height:200px;background:rgb(255,0,0)"></div>
        <div style="position:absolute;left:500px;top:100px;width:300px;height:200px;background:rgba(0,0,255,0.5)"></div>
        <script>
        var post = function (url, data) { window.chrome.webview.postMessage(JSON.stringify({ url: url, data: data })); };
        window.htmlMask.onMessage = function (msg) {
            if (msg.url === '/ping') { post('/pong', { n: msg.data.n }); return; }
            if (msg.url === '/ask') return { answer: 42 };
        };
        document.addEventListener('mousedown', function (e) { post('/clicked', { x: e.clientX, y: e.clientY }); });
        window.htmlMask.request('/hello', { a: 1 }).then(function (r) { post('/resolved', r.data); });
        fetch('sub/data.json').then(function (r) { return r.json(); }).then(function (j) { post('/fetch-local', { ok: true, value: j.value }); }, function (e) { post('/fetch-local', { ok: false, error: String(e) }); });
        fetch('../outside.txt').then(function (r) { post('/fetch-outside', { status: r.status }); }, function (e) { post('/fetch-outside', { status: -1, error: String(e) }); });
        fetch('https://example.com/').then(function (r) { post('/fetch-remote', { status: r.status }); }, function (e) { post('/fetch-remote', { status: -1, error: String(e) }); });
        var w = window.open('https://example.com/'); post('/open', { opened: !!w });
        </script></body></html>
        """;

    /// <summary>
    /// HTML 遮罩（--ui-test html-mask）：在临时目录生成测试页面，经 Core 的 HtmlMask 打开遮罩，核对消息收发、请求应答、
    /// 资源访问限制、点击穿透切换；遮罩打开期间抓一帧截图器画面，核对画面里没有遮罩内容（红框位置不是红色）。
    /// 日志中的“HTMLMASK-PHASE”行供外部脚本截屏、模拟点击
    /// </summary>
    private async Task HtmlMaskAsync()
    {
        var root = Path.Combine(BetterGenshinImpact.Infrastructure.Storage.AppPaths.Temp, "html-mask-selftest");
        var workDir = Path.Combine(root, "mask");
        Directory.CreateDirectory(Path.Combine(workDir, "sub"));
        await File.WriteAllTextAsync(Path.Combine(workDir, "mask.html"), MaskTestPage);
        await File.WriteAllTextAsync(Path.Combine(workDir, "sub", "data.json"), "{\"value\":7}");
        await File.WriteAllTextAsync(Path.Combine(root, "outside.txt"), "secret");

        using var mask = new BetterGenshinImpact.Core.Script.Dependence.HtmlMask(workDir);
        var id = mask.Show("mask.html", "selftest");
        Check("打开遮罩", mask.Exists(id), id);
        var host = BetterGenshinImpact.Core.Script.Dependence.HtmlMaskWindowHost.Current;
        Check("默认点击穿透", host.GetClickThrough(id));

        var received = new Dictionary<string, JObject>();
        async Task<JObject?> WaitMessage(string url, int ms = 10000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                if (received.TryGetValue(url, out var found)) return found;
                if (mask.Poll(id) is { } raw)
                {
                    var msg = JObject.Parse(raw);
                    log.Information("遮罩消息 {Message}", raw);
                    received[msg.Value<string>("url") ?? ""] = msg;
                    continue;
                }

                await Task.Delay(50);
            }

            return received.GetValueOrDefault(url);
        }

        var hello = await WaitMessage("/hello");
        Check("页面请求 /hello（带 requestId）", hello?.Value<string>("requestId") is { Length: > 0 }, hello?.ToString(Newtonsoft.Json.Formatting.None));
        if (hello?.Value<string>("requestId") is { } requestId)
        {
            mask.Respond(id, requestId, "{\"ok\":true}");
            var resolved = await WaitMessage("/resolved");
            Check("脚本应答后页面 Promise 完成", resolved?["data"]?.Value<bool>("ok") == true, resolved?.ToString(Newtonsoft.Json.Formatting.None));
        }

        mask.Send(id, "/ping", "{\"n\":5}");
        var pong = await WaitMessage("/pong");
        Check("脚本发送 → 页面回复", pong?["data"]?.Value<int>("n") == 5, pong?.ToString(Newtonsoft.Json.Formatting.None));
        var answer = await mask.Request(id, "/ask", "{}", 5000);
        Check("脚本请求 → 页面返回值", answer?.Contains("42") == true, answer);
        var local = await WaitMessage("/fetch-local");
        Check("页面读取脚本目录内文件", local?["data"]?.Value<int>("value") == 7, local?.ToString(Newtonsoft.Json.Formatting.None));
        var outside = await WaitMessage("/fetch-outside");
        Check("页面不能读取脚本目录外文件", outside?["data"]?.Value<int>("status") is not 200, outside?.ToString(Newtonsoft.Json.Formatting.None));
        var remote = await WaitMessage("/fetch-remote");
        Check("未授权网络请求被拦截", remote?["data"]?.Value<int>("status") == -1, remote?.ToString(Newtonsoft.Json.Formatting.None));
        var opened = await WaitMessage("/open");
        Check("不能打开新窗口", opened?["data"]?.Value<bool>("opened") == false, opened?.ToString(Newtonsoft.Json.Formatting.None));

        // 截图器画面不含遮罩内容：红框中心（相对游戏画面 250,200）
        await Task.Delay(1000);
        if (vm.Host?.RuntimeService.Current?.Window is { } game)
        {
            using var frame = BetterGenshinImpact.GameTask.GameFrameGrabber.Grab(game.Handle, vm.Host.Config);
            if (frame is null)
            {
                Check("截图器画面", false, "抓帧失败");
            }
            else
            {
                var px = frame.At<OpenCvSharp.Vec3b>(200, 250);
                log.Information("截图器画面 {W}x{H} (250,200) BGR={B},{G},{R}；截图方式 {Mode}", frame.Width, frame.Height, px.Item0, px.Item1, px.Item2,
                    BetterGenshinImpact.Host.Common.GameHost.EffectiveCaptureMode(vm.Host.LinuxConfig));
                Check("截图器画面不含遮罩红框", !(px.Item2 > 200 && px.Item1 < 50 && px.Item0 < 50), $"BGR {px.Item0},{px.Item1},{px.Item2}");
            }
        }
        else
        {
            Check("截图器已启动", false);
        }

        log.Information("HTMLMASK-PHASE clickthrough");
        await Task.Delay(8000);
        Check("穿透时页面收不到点击", !received.ContainsKey("/clicked") && await WaitMessage("/clicked", 200) is null);

        mask.SetClickThrough(id, false);
        Check("关闭点击穿透", !mask.GetClickThrough(id));
        log.Information("HTMLMASK-PHASE interactive");
        var clicked = await WaitMessage("/clicked", 12000);
        Check("可交互时页面收到点击", clicked is not null, clicked?.ToString(Newtonsoft.Json.Formatting.None));

        mask.ToggleClickThrough(id);
        Check("切换回点击穿透", mask.GetClickThrough(id));
        log.Information("HTMLMASK-PHASE closing");
        Check("关闭遮罩", mask.Close(id) && !mask.Exists(id));
        Directory.Delete(root, true);
    }

    // —— HTML 设置页 ——

    /// <summary>
    /// 新建自测专用配置组（不动已有配置组），加入只提供 settings_html 的演示脚本 m36html，点击“JS设置”打开网页设置窗口；
    /// 演示页面加载后自动保存一次，核对配置组文件中的设置值，最后删除该配置组
    /// </summary>
    private async Task HtmlSettingsAsync()
    {
        const string groupName = "M36网页设置自测";
        vm.Navigate("全自动");
        vm.Auto.SelectedTab = 0;
        var page = vm.ScriptControl;
        page.Reload();
        await Settle(1000);
        if (page.ScriptGroups.FirstOrDefault(g => g.Name == groupName) is { } old)
        {
            page.DeleteScriptGroupCommand.Execute(old);
        }

        await Settle();
        page.NewGroupName = groupName;
        await Click(await WaitFor(() => ButtonByText("新增")), "新增配置组");
        var path = ScriptGroupStore.FilePath(groupName);
        Check("新增配置组写入文件", File.Exists(path), path);
        var group = page.SelectedScriptGroup;
        if (group?.Name != groupName)
        {
            Check("选中自测配置组", false, group?.Name);
            return;
        }

        page.SelectedJsScript = "m36html";
        await Settle();
        await Click(All<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.Content as string == "添加"), "添加 JS 脚本");
        Check("配置组内项目", group.Projects.Count == 1 && group.Projects[0].Type == "Javascript", group.Projects.Count);
        await Click(All<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.Content as string == "JS设置"), "JS设置");
        var htmlWindow = await WaitFor(() => window.OwnedWindows.OfType<HtmlSettingsWindow>().FirstOrDefault());
        Check("HTML 设置窗口打开", htmlWindow is not null, htmlWindow?.Title);

        // 演示页面加载后约 0.8 秒自动保存；等待配置组文件中出现页面写入的值
        string? saved = null;
        for (var i = 0; i < 100 && saved is null; i++)
        {
            await Task.Delay(100);
            var json = File.Exists(path) ? File.ReadAllText(path) : "";
            if (json.Contains("M36 网页队伍")) saved = json;
        }

        Check("页面保存写入配置组文件", saved is not null);
        if (saved is not null)
        {
            var settings = JObject.Parse(saved).Descendants().OfType<JProperty>()
                .FirstOrDefault(x => x.Name.Equals("JsScriptSettingsObject", StringComparison.OrdinalIgnoreCase))?.Value as JObject;
            Check("设置值：文本", settings?["teamName"]?.ToString() == "M36 网页队伍", settings?.ToString(Newtonsoft.Json.Formatting.None));
            Check("设置值：数字与数组", settings?["count"]?.ToObject<int>() == 3 && settings?["regions"]?.Count() == 2);
        }

        Check("窗口标题显示保存时间", htmlWindow?.Title?.Contains("已保存") == true, htmlWindow?.Title);
        log.Information("UI 自测 HTML 设置窗口已显示，等待截图");
        await Task.Delay(int.TryParse(Environment.GetEnvironmentVariable("BGI_UITEST_HOLD_MS"), out var hold) ? hold : 1500);
        htmlWindow?.Close();
        await Settle();
        page.DeleteScriptGroupCommand.Execute(page.ScriptGroups.FirstOrDefault(g => g.Name == groupName));
        await Settle();
        Check("删除自测配置组", !File.Exists(path));
    }

    // —— 脚本设置 ——

    private async Task ScriptSettingsAsync()
    {
        const string groupName = "M35设置自测";
        vm.Navigate("全自动");
        vm.Auto.SelectedTab = 0;
        var page = vm.ScriptControl;
        page.Reload();
        await Settle(1000);
        if (page.ScriptGroups.FirstOrDefault(g => g.Name == groupName) is { } old)
        {
            page.DeleteScriptGroupCommand.Execute(old);
        }

        await Settle();
        page.NewGroupName = groupName;
        await Click(await WaitFor(() => ButtonByText("新增")), "新增配置组");
        var path = ScriptGroupStore.FilePath(groupName);
        Check("新增配置组写入文件", File.Exists(path), path);

        page.SelectedJsScript = "m35demo";
        await Settle();
        var addButtons = All<Button>().Where(b => b.IsEffectivelyVisible && b.Content as string == "添加").ToList();
        await Click(addButtons.FirstOrDefault(), "添加 JS 脚本");
        page.NewShellCommand = "echo m35";
        await Click(addButtons.Skip(2).FirstOrDefault(), "添加 Shell");
        var group = page.SelectedScriptGroup!;
        Check("配置组内项目", group.Projects.Count == 2, string.Join(",", group.Projects.Select(p => p.Type)));

        // 1. JS 自定义设置：默认值写入，修改后保存
        var js = group.Projects.First(p => p.Type == "Javascript");
        await Click(All<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.Content as string == "JS设置"), "JS设置");
        Check("JS 设置面板打开", page.Editor?.Kind == ScriptSettingsKind.JsSettings);
        Shot("avalonia-js-settings-default.png");
        Check("默认值：输入框", Named<TextBox>("js:teamName")?.Text == "默认队伍", Named<TextBox>("js:teamName")?.Text);
        Check("默认值：下拉", Named<ComboBox>("js:mode")?.SelectedItem as string == "标准");
        Check("默认值：勾选", Named<CheckBox>("js:skipBoss")?.IsChecked == true);
        Check("默认值：多选", Named<CheckBox>("js:regions:蒙德")?.IsChecked == true && Named<CheckBox>("js:regions:璃月")?.IsChecked == false);
        Check("默认值：级联", Named<ComboBox>("js:material:1")?.SelectedItem as string == "矿石" && Named<ComboBox>("js:material:2")?.SelectedItem as string == "白铁块");
        Named<TextBox>("js:teamName")!.Text = "M35 队伍";
        Named<ComboBox>("js:mode")!.SelectedItem = "完整";
        Named<CheckBox>("js:skipBoss")!.IsChecked = false;
        Named<CheckBox>("js:regions:稻妻")!.IsChecked = true;
        Named<CheckBox>("js:regions:蒙德")!.IsChecked = false;
        Named<ComboBox>("js:material:1")!.SelectedItem = "特产";
        await Settle();
        Named<ComboBox>("js:material:2")!.SelectedItem = "琉璃袋";
        await Settle();
        Shot("avalonia-js-settings.png");
        await Click(Named<Button>("CloseEditorButton"), "保存并关闭");
        var saved = JObject.Parse(File.ReadAllText(path));
        var settings = saved["projects"]?.FirstOrDefault(p => p.Value<string>("type") == "Javascript")?["jsScriptSettingsObject"] as JObject;
        Check("存盘：JS 输入框", settings?.Value<string>("teamName") == "M35 队伍", settings?.ToString(Newtonsoft.Json.Formatting.None));
        Check("存盘：JS 下拉", settings?.Value<string>("mode") == "完整");
        Check("存盘：JS 勾选", settings?["skipBoss"]?.Type == JTokenType.Boolean && settings.Value<bool>("skipBoss") == false);
        Check("存盘：JS 多选", settings?["regions"] is JArray r && r.Values<string>().SequenceEqual(["稻妻"]));
        Check("存盘：JS 级联", settings?.Value<string>("material") == "琉璃袋");

        // 重新打开：读回已保存的值
        page.Reload();
        await Settle();
        page.SelectedScriptGroup = page.ScriptGroups.First(g => g.Name == groupName);
        await Settle();
        page.OpenJsSettings(page.SelectedScriptGroup.Projects.First(p => p.Type == "Javascript"));
        await Settle();
        Check("重新打开：值保持", Named<TextBox>("js:teamName")?.Text == "M35 队伍" && Named<CheckBox>("js:regions:稻妻")?.IsChecked == true
                              && Named<ComboBox>("js:material:2")?.SelectedItem as string == "琉璃袋");
        await Click(Named<Button>("CloseEditorButton"), "保存并关闭");

        // 2. 项目通用设置
        var jsRow = page.SelectedScriptGroup.Projects.First(p => p.Type == "Javascript");
        await Click(All<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.Content as string == "设置" && b.CommandParameter == jsRow), "通用设置");
        Check("通用设置面板打开", page.Editor?.Kind == ScriptSettingsKind.Project);
        var schedule = Named<ComboBox>("p:Schedule")!;
        schedule.SelectedIndex = ScriptGroupProjectExtensions.ScheduleDescriptions.Keys.ToList().IndexOf("Monday");
        Named<NumericUpDown>("p:RunNum")!.Value = 3;
        Named<ComboBox>("p:AllowJsHTTP")!.SelectedIndex = 0; // 启用
        await Settle();
        Shot("avalonia-project-settings.png");
        await Click(Named<Button>("CloseEditorButton"), "保存并关闭");
        saved = JObject.Parse(File.ReadAllText(path));
        var proj = saved["projects"]!.First(p => p.Value<string>("type") == "Javascript");
        Check("存盘：执行周期", proj.Value<string>("schedule") == "Monday", proj.Value<string>("schedule"));
        Check("存盘：执行次数", proj.Value<int>("runNum") == 3);
        Check("存盘：HTTP 权限 Hash", !string.IsNullOrEmpty(proj.Value<string>("allowJsHTTPHash")), proj.Value<string>("allowJsHTTPHash"));

        // 3. 配置组设置
        await Click(ButtonByText("配置组设置"), "配置组设置");
        Check("配置组设置面板打开", page.Editor?.Kind == ScriptSettingsKind.GroupConfig);
        Shot("avalonia-group-config.png");
        Named<ToggleSwitch>("f:PathingConfig.Enabled")!.IsChecked = true;
        Named<TextBox>("f:PathingConfig.PartyName")!.Text = "M35 队伍";
        Named<ComboBox>("f:PathingConfig.RecoverTiming")!.SelectedIndex = 1; // 只在传送点
        Named<ComboBox>("f:PathingConfig.MainAvatarIndex")!.SelectedIndex = 2; // "2"
        var timeout = Named<TextBox>("f:PathingConfig.AutoFightConfig.Timeout")!;
        timeout.Text = "abc";
        await Settle(150);
        Check("无效数字不写入", page.SelectedScriptGroup.Config.PathingConfig.AutoFightConfig.Timeout != 0, page.SelectedScriptGroup.Config.PathingConfig.AutoFightConfig.Timeout);
        timeout.Text = "150";
        Named<ComboBox>("f:PathingConfig.AutoFightConfig.OnlyPickEliteDropsMode")!.SelectedIndex = 2;
        Named<ToggleSwitch>("f:EnableShellConfig")!.IsChecked = true;
        Named<TextBox>("f:ShellConfig.Timeout")!.Text = "120";
        await Settle();
        // 显示条件：赶路角色选玛薇卡后出现跳飞设置
        Check("跳飞设置默认隐藏", Named<ToggleSwitch>("f:PathingConfig.MwkJumpFlyEnabled") is null);
        Check("仅任务计划节点的项隐藏", Named<ToggleSwitch>("f:PathingConfig.AutoSkipEnabled") is null);
        Named<ComboBox>("f:PathingConfig.HurryOnAvatar")!.SelectedItem = "玛薇卡";
        await Settle();
        Check("选玛薇卡后显示跳飞设置", Named<ToggleSwitch>("f:PathingConfig.MwkJumpFlyEnabled") is not null);
        Named<ComboBox>("f:PathingConfig.HurryOnAvatar")!.BringIntoView();
        await Settle();
        Shot("avalonia-group-config-hurry.png");
        Named<ToggleSwitch>("f:PathingConfig.AutoFightConfig.SwimmingEnabled")?.BringIntoView();
        await Settle();
        Shot("avalonia-group-config-fight.png");
        await Click(Named<Button>("CloseEditorButton"), "保存并关闭");
        var reloaded = ScriptGroup.FromJson(File.ReadAllText(path)).Config;
        Check("存盘：地图追踪配置启用", reloaded.PathingConfig.Enabled);
        Check("存盘：队伍名称", reloaded.PathingConfig.PartyName == "M35 队伍", reloaded.PathingConfig.PartyName);
        Check("存盘：回复时机", reloaded.PathingConfig.RecoverTiming == RecoverTiming.OnlyTeleport, reloaded.PathingConfig.RecoverTiming);
        Check("存盘：行走位", reloaded.PathingConfig.MainAvatarIndex == "2", reloaded.PathingConfig.MainAvatarIndex);
        Check("存盘：战斗超时", reloaded.PathingConfig.AutoFightConfig.Timeout == 150, reloaded.PathingConfig.AutoFightConfig.Timeout);
        Check("存盘：精英掉落模式", reloaded.PathingConfig.AutoFightConfig.OnlyPickEliteDropsMode == "DisableAutoPickupForNonElite");
        Check("存盘：赶路角色", reloaded.PathingConfig.HurryOnAvatar == "玛薇卡");
        Check("存盘：Shell 配置", reloaded.EnableShellConfig && reloaded.ShellConfig.Timeout == 120, reloaded.ShellConfig.Timeout);
        Check("字段目录可解析", ScriptGroupConfigFields.All.All(f => ScriptGroupConfigFields.Resolve(new ScriptGroupConfig(), f.Path, out _, out _)),
            string.Join(",", ScriptGroupConfigFields.All.Where(f => !ScriptGroupConfigFields.Resolve(new ScriptGroupConfig(), f.Path, out _, out _)).Select(f => f.Path)));
        page.Reload();
    }

    // —— 任务计划 ——

    private static JObject ReadPlan(string id) => JObject.Parse(File.ReadAllText(Global.Absolute($@"User\Pulonia\plans\{id}.json")));

    private static JArray Children(JObject plan) => (JArray)plan["root_task"]!["children"]!;

    private async Task TaskPlanAsync()
    {
        const string planName = "M35 界面自测";
        vm.Navigate("任务计划");
        var page = vm.TaskPlan;
        await page.ReloadAsync();
        await Settle(1000);
        foreach (var old in page.Plans.Where(p => p.Name == planName).ToList())
        {
            page.SelectedPlan = old;
            await Settle(300);
            if (page.SelectedPlan == old)
            {
                await Click(await WaitFor(() => ButtonByText("删除计划")), "删除旧计划");
            }
        }

        page.NewPlanName = planName;
        await Click(await WaitFor(() => All<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && ToolTip.GetTip(b) as string == "新建计划")), "新建计划");
        var id = page.SelectedPlan?.Plan.Id ?? "";
        var created = page.SelectedPlan?.Name == planName && File.Exists(Global.Absolute($@"User\Pulonia\plans\{id}.json"));
        Check("新建计划写入文件", created, id);
        if (!created)
        {
            // 没有新建成功时停止，避免改动已有计划
            return;
        }

        // 添加：Shell、录制回放（第一个资源）、分组
        page.SelectedAddOption = page.AddOptions.First(o => o.Definition.TaskType == "shell");
        await Settle();
        await Click(ButtonByText("＋ 添加"), "添加 Shell");
        page.SelectedNode = null;
        page.SelectedAddOption = page.AddOptions.First(o => o.Definition.TaskType == "keymouse");
        await Settle(800);
        await Click(ButtonByText("＋ 添加"), "添加录制回放");
        page.SelectedNode = null;
        await Click(ButtonByText("分组"), "添加分组");
        var kids = Children(ReadPlan(id));
        Check("存盘：添加 3 个任务", kids.Select(k => k.Value<string>("task_type")).SequenceEqual(["shell", "keymouse", "group"]),
            string.Join(",", kids.Select(k => k.Value<string>("task_type"))));
        Shot("avalonia-taskplan-edit.png");

        // 排序：选中第一项点“↓”
        page.SelectedNode = page.Nodes[0];
        await Settle();
        await Click(All<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.Content as string == "↓" && ToolTip.GetTip(b) as string == "下移"), "下移");
        kids = Children(ReadPlan(id));
        Check("存盘：下移", kids.Select(k => k.Value<string>("task_type")).SequenceEqual(["keymouse", "shell", "group"]),
            string.Join(",", kids.Select(k => k.Value<string>("task_type"))));
        Check("移动后保持选中", page.SelectedNode?.Task.TaskType == "shell", page.SelectedNode?.Name);
        await Click(All<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.Content as string == "↑" && ToolTip.GetTip(b) as string == "上移"), "上移");
        kids = Children(ReadPlan(id));
        Check("存盘：上移", kids.Select(k => k.Value<string>("task_type")).SequenceEqual(["shell", "keymouse", "group"]),
            string.Join(",", kids.Select(k => k.Value<string>("task_type"))));

        // 启用：取消勾选第二行
        var list = All<ListBox>().First(l => ReferenceEquals(l.ItemsSource, page.Nodes));
        var rowCheck = list.GetVisualDescendants().OfType<CheckBox>().Skip(1).FirstOrDefault();
        if (rowCheck is not null) rowCheck.IsChecked = false;
        await Settle();
        kids = Children(ReadPlan(id));
        Check("存盘：停用第二项", kids[1].Value<bool>("is_enabled") == false && kids[0].Value<bool>("is_enabled"));

        // 任务参数：Shell 改名、改 file_name 与 arguments
        page.SelectedNode = page.Nodes.First(n => n.Task.TaskType == "shell");
        await Settle();
        Check("任务设置面板", page.NodeEditor is { HasFields: true }, string.Join(",", page.NodeEditor?.Fields.Select(f => f.Name) ?? []));
        Named<TextBox>("NodeNameBox")!.Text = "M35 Shell";
        var fileBox = FieldBox("file_name");
        if (fileBox is not null) fileBox.Text = "/bin/echo";
        var argsBox = FieldBox("arguments");
        if (argsBox is not null) argsBox.Text = "hello\nm35";
        await Settle();
        Shot("avalonia-taskplan-params.png");
        await Click(Named<Button>("ApplyNodeButton"), "应用");
        var shell = Children(ReadPlan(id)).First(k => k.Value<string>("task_type") == "shell");
        Check("存盘：任务名称", shell.Value<string>("name") == "M35 Shell", shell.Value<string>("name"));
        Check("存盘：参数 file_name", shell["parameters"]?.Value<string>("file_name") == "/bin/echo", shell["parameters"]?.ToString(Newtonsoft.Json.Formatting.None));
        Check("存盘：参数 arguments", shell["parameters"]?["arguments"] is JArray a && a.Values<string>().SequenceEqual(["hello", "m35"]));
        // 无效参数：整数字段填文字，不写入
        var intField = page.NodeEditor?.Fields.FirstOrDefault(f => f.Field.Kind == Pulonia.Services.PuloniaParameterFieldKind.Integer);
        if (intField is not null)
        {
            var before = ReadPlan(id).ToString();
            FieldBox(intField.Name)!.Text = "abc";
            await Settle();
            await Click(Named<Button>("ApplyNodeButton"), "应用（无效）");
            Check("无效参数被拒绝且不存盘", page.StatusMessage.StartsWith("设置无效") && ReadPlan(id).ToString() == before, page.StatusMessage);
        }

        // 分组执行次数
        page.SelectedNode = page.Nodes.First(n => n.Task.TaskType == "group");
        await Settle();
        var beforeRepeat = ReadPlan(id).ToString();
        Named<TextBox>("NodeRepeatBox")!.Text = "0";
        await Click(Named<Button>("ApplyNodeButton"), "应用（无效次数）");
        Check("无效执行次数被拒绝且不存盘", page.StatusMessage.StartsWith("设置无效") && ReadPlan(id).ToString() == beforeRepeat, page.StatusMessage);
        Named<TextBox>("NodeRepeatBox")!.Text = "3";
        await Click(Named<Button>("ApplyNodeButton"), "应用（分组）");
        Check("存盘：分组执行次数", Children(ReadPlan(id)).First(k => k.Value<string>("task_type") == "group").Value<int?>("repeat_count") == 3);

        // 删除：删除分组
        page.SelectedNode = page.Nodes.First(n => n.Task.TaskType == "group");
        await Settle();
        await Click(All<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.Content as string == "删除" && b.Command == page.RemoveTaskCommand), "删除任务");
        kids = Children(ReadPlan(id));
        Check("存盘：删除分组", kids.Count == 2 && kids.All(k => k.Value<string>("task_type") != "group"));

        // 触发方式：新增定时、无效表达式、启停、删除
        page.SelectedTab = 2;
        await Settle();
        await Click(Named<Button>("AddScheduleTriggerButton"), "＋ 定时触发");
        Named<TextBox>("TriggerCronBox")!.Text = "99 5 * * *";
        await Settle();
        Check("无效 Cron 不能保存", page.TriggerEditor is { IsValid: false } && Named<Button>("SaveTriggerButton")?.IsEffectivelyEnabled == false, page.TriggerEditor?.PreviewText);
        Named<TextBox>("TriggerCronBox")!.Text = "30 5 * * 1-5";
        Named<CheckBox>("TriggerEnabledBox")!.IsChecked = true;
        await Settle();
        Shot("avalonia-taskplan-trigger.png");
        await Click(Named<Button>("SaveTriggerButton"), "保存触发器");
        var triggers = (JArray)ReadPlan(id)["triggers"]!;
        Check("存盘：定时触发", triggers.Count == 1 && triggers[0].Value<string>("cron") == "30 5 * * 1-5" && triggers[0].Value<bool>("enabled"),
            triggers.ToString(Newtonsoft.Json.Formatting.None));
        var triggerList = All<ListBox>().First(l => ReferenceEquals(l.ItemsSource, page.Triggers));
        var toggle = triggerList.GetVisualDescendants().OfType<ToggleSwitch>().FirstOrDefault();
        if (toggle is not null) toggle.IsChecked = false;
        await Settle();
        triggers = (JArray)ReadPlan(id)["triggers"]!;
        Check("存盘：停用触发", triggers.Count == 1 && !triggers[0].Value<bool>("enabled"));
        // 编辑已有触发：改时间
        page.SelectedTrigger = page.Triggers.FirstOrDefault();
        await Settle();
        Named<TextBox>("TriggerCronBox")!.Text = "0 6 * * *";
        await Settle();
        await Click(Named<Button>("SaveTriggerButton"), "保存触发器（编辑）");
        triggers = (JArray)ReadPlan(id)["triggers"]!;
        Check("存盘：编辑触发", triggers.Count == 1 && triggers[0].Value<string>("cron") == "0 6 * * *");
        Shot("avalonia-taskplan-trigger-list.png");
        page.SelectedTrigger = page.Triggers.FirstOrDefault();
        await Click(All<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.Content as string == "删除" && b.Command == page.DeleteTriggerCommand), "删除触发");
        Check("存盘：删除触发", ((JArray)ReadPlan(id)["triggers"]!).Count == 0);

        // 删除计划
        page.SelectedTab = 0;
        await Settle();
        if (page.SelectedPlan?.Plan.Id == id)
        {
            await Click(ButtonByText("删除计划"), "删除计划");
        }

        Check("删除计划文件", !File.Exists(Global.Absolute($@"User\Pulonia\plans\{id}.json")));
    }

    private TextBox? FieldBox(string name)
        => All<StackPanel>().FirstOrDefault(p => p.Tag as string == name && p.IsEffectivelyVisible)?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
}
