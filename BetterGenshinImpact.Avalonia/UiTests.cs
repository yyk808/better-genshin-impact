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
                case "domain-editor": await DomainEditorAsync(); break;
                case "dedicated-editors": await DedicatedEditorsAsync(); break;
                case "boss-editors": await BossEditorsAsync(); break;
                case "resin-plan-editor": await ResinPlanEditorAsync(); break;
                case "plan-hotkey": await PlanHotkeyAsync(); break;
                case "plan-reference": await PlanReferenceAsync(); break;
                case "plan-cooldown": await PlanCooldownAsync(); break;
                case "plan-profiles-cd": await PlanProfilesCooldownAsync(); break;
                case "plan-node-cd": await PlanNodeCooldownAsync(); break;
                case "dev-hotkeys": await DevHotkeysAsync(); break;
                case "notify-channels": await NotifyChannelsAsync(); break;
                case "solo-domain": await SoloDomainAsync(); break;
                case "history": await HistoryAsync(); break;
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

    /// <summary>
    /// 等界面稳定。设置卡片与 WPF 版相同默认收起，自测要操作卡片内的控件，先展开当前页面的全部设置卡片
    /// </summary>
    private async Task Settle(int ms = 400)
    {
        foreach (var expander in All<FluentAvalonia.UI.Controls.FASettingsExpander>().Where(e => e.IsEffectivelyVisible && !e.IsExpanded))
        {
            expander.IsExpanded = true;
        }

        await Task.Delay(ms);
    }

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
            var addGpt = NamedIn<Button>(win, "AddGptProfile");
            Check("GPT-SoVITS 声线“新增”可用", addGpt?.IsEffectivelyEnabled == true);
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

    // —— 专属参数编辑器：自动秘境（任务计划参数编辑） ——

    private async Task DomainEditorAsync()
    {
        const string planName = "M38 秘境编辑器自测";
        vm.Navigate("任务计划");
        var page = vm.TaskPlan;
        await page.ReloadAsync();
        await Settle(1000);
        // 截图时收起日志面板，留出编辑区
        if (ButtonByText("收起") is { } collapse) await Click(collapse, "收起日志");
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
            return;
        }

        page.SelectedAddOption = page.AddOptions.FirstOrDefault(o => o.Definition.TaskType == "solo.auto_domain");
        Check("添加选项含自动秘境", page.SelectedAddOption is not null);
        await Settle(500);
        await Click(ButtonByText("＋ 添加"), "添加自动秘境");
        await Settle(500);
        var kids = Children(ReadPlan(id));
        Check("存盘：添加自动秘境", kids.Count == 1 && kids[0].Value<string>("task_type") == "solo.auto_domain",
            string.Join(",", kids.Select(k => k.Value<string>("task_type"))));
        page.SelectedNode = page.Nodes.FirstOrDefault(n => n.Task.TaskType == "solo.auto_domain");
        await Settle(800);
        var editor = page.NodeEditor;
        Check("显示专属编辑器", editor?.Dedicated is ViewModel.SoloTaskSettings.AutoDomainSettingsViewModel
                                && All<Views.SoloTaskSettings.AutoDomainSettingsEditor>().Any(e => e.IsEffectivelyVisible), editor?.Dedicated?.GetType().Name);
        var edited = editor?.Dedicated?.EditedParameterNames ?? [];
        Check("通用字段不重复专属字段", editor is not null && editor.Fields.All(f => !edited.Contains(f.Name)),
            string.Join(",", editor?.Fields.Select(f => f.Name) ?? []));

        // 指定秘境
        Named<TextBox>("AdPartyName")!.Text = "M38 队伍";
        Named<RadioButton>("AdSourceManual")!.IsChecked = true;
        await Settle();
        var domainBox = Named<ComboBox>("AdDomainName");
        Check("秘境下拉有选项", domainBox?.ItemCount > 0, domainBox?.ItemCount);
        var domain = domainBox?.ItemCount > 1 ? domainBox.Items[1] as string : null;
        if (domainBox is not null && domain is not null) domainBox.SelectedItem = domain;
        Named<ComboBox>("AdSunday")!.SelectedItem = "2";
        Named<ToggleSwitch>("AdArtifactSalvage")!.IsChecked = true;
        await Settle();
        Named<ComboBox>("AdMaxStar")!.SelectedItem = "3";
        var reward = Named<ToggleSwitch>("AdRewardRecognition")!;
        var rewardBefore = reward.IsChecked == true;
        reward.IsChecked = !rewardBefore;
        await Settle();
        Named<TextBox>("AdPartyName")?.BringIntoView();
        await Settle();
        Shot("avalonia-domain-editor-manual.png");
        Named<ToggleSwitch>("AdArtifactSalvage")?.BringIntoView();
        await Settle();
        Shot("avalonia-domain-editor-manual-2.png");
        Named<ContentControl>("NodeDedicatedEditor")?.BringIntoView();
        await Click(Named<Button>("ApplyNodeButton"), "应用（指定秘境）");
        var p = Children(ReadPlan(id))[0]["parameters"] as JObject ?? new JObject();
        var pText = p.ToString(Newtonsoft.Json.Formatting.None);
        Check("存盘：队伍名称", p.Value<string>("party_name") == "M38 队伍", pText);
        Check("存盘：秘境", domain is not null && p.Value<string>("domain_name") == domain, pText);
        Check("存盘：周日奖励", p.Value<string>("sunday_selected_value") == "2", pText);
        Check("存盘：分解圣遗物", p.Value<bool?>("auto_artifact_salvage") == true && p.Value<string>("max_artifact_star") == "3", pText);
        Check("存盘：奖励识别", p.Value<bool?>("reward_recognition_enabled") == !rewardBefore, pText);

        // 重新选中后编辑器显示已存的值
        page.SelectedNode = null;
        await Settle();
        page.SelectedNode = page.Nodes.FirstOrDefault();
        await Settle(800);
        Check("重新打开显示存盘值", Named<TextBox>("AdPartyName")?.Text == "M38 队伍" && Named<ComboBox>("AdDomainName")?.SelectedItem as string == domain
                                 && Named<RadioButton>("AdSourceManual")?.IsChecked == true);

        // 跟随培养计划
        Named<RadioButton>("AdSourceTraining")!.IsChecked = true;
        await Settle();
        var adVm = page.NodeEditor?.Dedicated as ViewModel.SoloTaskSettings.AutoDomainSettingsViewModel;
        Check("培养计划设置显示", Named<ToggleSwitch>("AdCalculateRuns") is not null && Named<ComboBox>("AdDomainName") is null,
            $"domain={adVm?.DomainName} training={adVm?.DomainSource.IsTrainingPlan} radio={Named<RadioButton>("AdSourceTraining")?.IsChecked} same={ReferenceEquals(adVm, editor?.Dedicated)}");
        if (Named<ComboBox>("AdRunPreference") is null)
        {
            return;
        }
        Named<ComboBox>("AdRunPreference")!.SelectedIndex = 0;
        Named<NumericUpDown>("AdReservePercent")!.Value = 12;
        Named<ToggleSwitch>("AdDiagnostics")!.IsChecked = true;
        Named<ComboBox>("AdFallbackSunday")!.SelectedItem = "3";
        await Settle();
        Named<ToggleSwitch>("AdCalculateRuns")?.BringIntoView();
        await Settle();
        Shot("avalonia-domain-editor-training.png");
        await Click(Named<Button>("ApplyNodeButton"), "应用（培养计划）");
        p = Children(ReadPlan(id))[0]["parameters"] as JObject ?? new JObject();
        pText = p.ToString(Newtonsoft.Json.Formatting.None);
        Check("存盘：跟随培养计划", p.Value<string>("domain_name") == GameTask.AutoDomain.AutoDomainTask.TrainingGuideOption, pText);
        Check("存盘：预留率", p.Value<int?>("training_guide_crafting_bonus_reserve_percent") == 12, pText);
        Check("存盘：诊断", p.Value<bool?>("training_guide_diagnostics_enabled") == true, pText);
        Check("存盘：备选周日奖励", p.Value<string>("training_guide_fallback_sunday_selected_value") == "3", pText);
        Check("存盘：未改字段保留", p.Value<string>("party_name") == "M38 队伍", pText);

        // 删除计划
        if (page.SelectedPlan?.Plan.Id == id)
        {
            await Click(ButtonByText("删除计划"), "删除计划");
        }

        Check("删除计划文件", !File.Exists(Global.Absolute($@"User\Pulonia\plans\{id}.json")));
    }

    // —— 专属参数编辑器：地脉花、每日奖励、合成浓缩树脂、尘歌壶，以及树脂设置对话框 ——

    /// <summary>新建临时计划（同名旧计划先删除），返回计划 ID；失败返回 null</summary>
    private async Task<string?> CreateTempPlanAsync(TaskPlanPageViewModel page, string planName)
    {
        vm.Navigate("任务计划");
        await page.ReloadAsync();
        await Settle(1000);
        if (ButtonByText("收起") is { } collapse) await Click(collapse, "收起日志");
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
        return created ? id : null;
    }

    private async Task DeleteTempPlanAsync(TaskPlanPageViewModel page, string id)
    {
        if (page.SelectedPlan?.Plan.Id == id)
        {
            await Click(ButtonByText("删除计划"), "删除计划");
        }

        Check("删除计划文件", !File.Exists(Global.Absolute($@"User\Pulonia\plans\{id}.json")));
    }

    private async Task AddSoloTaskAsync(TaskPlanPageViewModel page, string taskType)
    {
        page.SelectedNode = null;
        page.SelectedAddOption = page.AddOptions.FirstOrDefault(o => o.Definition.TaskType == taskType);
        await Settle(300);
        await Click(ButtonByText("＋ 添加"), "添加 " + taskType);
        await Settle(300);
    }

    private async Task<T?> SelectDedicatedAsync<T>(TaskPlanPageViewModel page, string taskType) where T : class
    {
        page.SelectedNode = null;
        await Settle(200);
        page.SelectedNode = page.Nodes.FirstOrDefault(n => n.Task.TaskType == taskType);
        await Settle(800);
        var dedicated = page.NodeEditor?.Dedicated as T;
        var edited = page.NodeEditor?.Dedicated?.EditedParameterNames ?? [];
        Check($"{taskType} 显示专属编辑器", dedicated is not null && page.NodeEditor!.Fields.All(f => !edited.Contains(f.Name)),
            page.NodeEditor?.Dedicated?.GetType().Name);
        return dedicated;
    }

    private JObject NodeParams(string id, string taskType)
        => Children(ReadPlan(id)).FirstOrDefault(k => k.Value<string>("task_type") == taskType)?["parameters"] as JObject ?? new JObject();

    /// <summary>在 UI 线程稍后执行打开树脂对话框的命令（对话框是同步模态），返回打开的对话框</summary>
    private async Task<ResinSettingsWindow?> OpenResinDialogAsync(System.Windows.Input.ICommand command)
    {
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() => command.Execute(null));
        var dialog = await WaitFor(() => ResinSettingsWindow.Current);
        await Settle(600);
        return dialog;
    }

    private static T? InDialog<T>(Window dialog, string name) where T : Control
        => dialog.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name && c.IsEffectivelyVisible);

    private async Task DedicatedEditorsAsync()
    {
        var page = vm.TaskPlan;
        if (await CreateTempPlanAsync(page, "M39 专属编辑器自测") is not { } id)
        {
            return;
        }

        const string ley = "solo.auto_ley_line_outcrop", daily = "solo.claim_daily_rewards", craft = "solo.craft_condensed_resin", pot = "solo.serenitea_pot_rewards";
        foreach (var t in new[] { ley, daily, craft, pot })
        {
            await AddSoloTaskAsync(page, t);
        }

        var kids = Children(ReadPlan(id));
        Check("存盘：添加 4 个任务", kids.Select(k => k.Value<string>("task_type")).SequenceEqual([ley, daily, craft, pot]),
            string.Join(",", kids.Select(k => k.Value<string>("task_type"))));

        // 地脉花：跳过准备流程 + 树脂对话框（指定领奖次数 3）
        if (await SelectDedicatedAsync<ViewModel.SoloTaskSettings.AutoLeyLineOutcropSettingsViewModel>(page, ley) is { } leyVm)
        {
            var oneDragon = leyVm.OneDragonMode;
            Named<ToggleSwitch>("LeyOneDragonMode")!.IsChecked = !oneDragon;
            Check("树脂设置按钮可用", leyVm.Resin.CanEdit && Named<Button>("LeyResinButton")?.IsEffectivelyEnabled == true);
            var before = leyVm.Resin.Summary;
            Shot("avalonia-ley-editor.png");
            var dialog = await OpenResinDialogAsync(leyVm.Resin.EditCommand);
            Check("打开树脂设置对话框", dialog is not null);
            if (dialog is not null)
            {
                Shot(dialog, "avalonia-resin-dialog.png");
                if (InDialog<RadioButton>(dialog, "ResinModeRunCount") is { IsEffectivelyEnabled: true } runCount)
                {
                    runCount.IsChecked = true;
                    await Settle();
                    if (InDialog<NumericUpDown>(dialog, "ResinRunCount") is { } count) count.Value = 3;
                    await Settle();
                }

                Shot(dialog, "avalonia-resin-dialog-runcount.png");
                var confirm = InDialog<Button>(dialog, "ResinConfirmButton");
                Check("对话框可确定", confirm?.IsEffectivelyEnabled == true, InDialog<TextBlock>(dialog, "ResinValidationText")?.Text);
                if (confirm is { IsEffectivelyEnabled: true }) OnClickMethod.Invoke(confirm, null);
                else dialog.Close();
                await WaitFor(() => ResinSettingsWindow.Current is null ? "closed" : null);
                await Settle();
            }

            Check("树脂摘要更新", leyVm.Resin.Summary != before, leyVm.Resin.Summary);
            await Click(Named<Button>("ApplyNodeButton"), "应用（地脉花）");
            var p = NodeParams(id, ley);
            Check("存盘：地脉花跳过准备", p.Value<bool?>("one_dragon_mode") == !oneDragon, p.ToString(Newtonsoft.Json.Formatting.None));
            Check("存盘：地脉花树脂使用", p["resin_usage"] is JObject r && r.ToString().Contains('3'), p["resin_usage"]?.ToString(Newtonsoft.Json.Formatting.None));

            // 取消对话框不改值
            leyVm = page.NodeEditor?.Dedicated as ViewModel.SoloTaskSettings.AutoLeyLineOutcropSettingsViewModel;
            if (leyVm is not null)
            {
                var summary = leyVm.Resin.Summary;
                var cancelDialog = await OpenResinDialogAsync(leyVm.Resin.EditCommand);
                if (cancelDialog is not null)
                {
                    if (InDialog<RadioButton>(cancelDialog, "ResinModeExhaust") is { IsEffectivelyEnabled: true } exhaust) exhaust.IsChecked = true;
                    await Settle();
                    OnClickMethod.Invoke(InDialog<Button>(cancelDialog, "ResinCancelButton")!, null);
                    await WaitFor(() => ResinSettingsWindow.Current is null ? "closed" : null);
                    await Settle();
                }

                Check("取消对话框不改值", cancelDialog is not null && leyVm.Resin.Summary == summary, leyVm.Resin.Summary);
            }
        }

        // 每日奖励
        if (await SelectDedicatedAsync<ViewModel.SoloTaskSettings.ClaimDailyRewardsSettingsViewModel>(page, daily) is not null)
        {
            Named<ComboBox>("DailyCountry")!.SelectedItem = "枫丹";
            Named<TextBox>("DailyPartyName")!.Text = "M39 好感队";
            await Settle();
            Shot("avalonia-daily-editor.png");
            await Click(Named<Button>("ApplyNodeButton"), "应用（每日奖励）");
            var p = NodeParams(id, daily);
            Check("存盘：每日奖励地区与队伍", p.Value<string>("country") == "枫丹" && p.Value<string>("party_name") == "M39 好感队", p.ToString(Newtonsoft.Json.Formatting.None));
        }

        // 合成浓缩树脂：地区 + 保留 20 原粹树脂
        if (await SelectDedicatedAsync<ViewModel.SoloTaskSettings.CraftCondensedResinSettingsViewModel>(page, craft) is { } craftVm)
        {
            Named<ComboBox>("CraftCountry")!.SelectedItem = "稻妻";
            var dialog = await OpenResinDialogAsync(craftVm.EditResinCommand);
            Check("打开合成树脂对话框", dialog is not null);
            if (dialog is not null)
            {
                InDialog<RadioButton>(dialog, "CraftKeep")!.IsChecked = true;
                await Settle();
                if (InDialog<NumericUpDown>(dialog, "CraftKeepAmount") is { } keep) keep.Value = 20;
                await Settle();
                Shot(dialog, "avalonia-craft-resin-dialog.png");
                OnClickMethod.Invoke(InDialog<Button>(dialog, "ResinConfirmButton")!, null);
                await WaitFor(() => ResinSettingsWindow.Current is null ? "closed" : null);
                await Settle();
            }

            Shot("avalonia-craft-editor.png");
            await Click(Named<Button>("ApplyNodeButton"), "应用（合成浓缩树脂）");
            var p = NodeParams(id, craft);
            Check("存盘：合成地区与保留量", p.Value<string>("country") == "稻妻" && p.Value<int?>("min_resin_to_keep") == 20, p.ToString(Newtonsoft.Json.Formatting.None));
        }

        // 尘歌壶：道具进壶、星期三购买布匹和摩拉
        if (await SelectDedicatedAsync<ViewModel.SoloTaskSettings.SereniteaPotSettingsViewModel>(page, pot) is { } potVm)
        {
            Named<ComboBox>("PotTeleportType")!.SelectedItem = "尘歌壶道具";
            Named<ComboBox>("PotPurchaseSchedule")!.SelectedItem = "星期三";
            foreach (var item in potVm.PurchaseItems) item.IsSelected = item.Name is "布匹" or "摩拉";
            await Settle();
            Check("商品勾选框", Named<ItemsControl>("PotPurchaseItems")?.GetVisualDescendants().OfType<CheckBox>().Count(c => c.IsChecked == true) == 2);
            Shot("avalonia-pot-editor.png");
            await Click(Named<Button>("ApplyNodeButton"), "应用（尘歌壶）");
            var p = NodeParams(id, pot);
            Check("存盘：尘歌壶", p.Value<string>("teleport_type") == "尘歌壶道具"
                                 && p["secret_treasure_objects"] is JArray a && a.Values<string>().SequenceEqual(["星期三", "布匹", "摩拉"]),
                p.ToString(Newtonsoft.Json.Formatting.None));
        }

        await DeleteTempPlanAsync(page, id);
    }

    // —— 专属参数编辑器：首领讨伐、幽境危战 ——

    private async Task BossEditorsAsync()
    {
        var page = vm.TaskPlan;
        if (await CreateTempPlanAsync(page, "M39 首领编辑器自测") is not { } id)
        {
            return;
        }

        const string boss = "solo.auto_boss", stygian = "solo.auto_stygian_onslaught";
        await AddSoloTaskAsync(page, boss);
        await AddSoloTaskAsync(page, stygian);
        var kids = Children(ReadPlan(id));
        Check("存盘：添加 2 个任务", kids.Select(k => k.Value<string>("task_type")).SequenceEqual([boss, stygian]),
            string.Join(",", kids.Select(k => k.Value<string>("task_type"))));

        if (await SelectDedicatedAsync<ViewModel.SoloTaskSettings.AutoBossSettingsViewModel>(page, boss) is { } bossVm)
        {
            var bossBox = Named<ComboBox>("BossName");
            Check("首领下拉有选项", bossBox?.ItemCount > 3, bossBox?.ItemCount);
            var bossName = bossBox?.ItemCount > 2 ? bossBox.Items[2] as string : null;
            if (bossBox is not null && bossName is not null) bossBox.SelectedItem = bossName;
            Named<TextBox>("BossTeamName")!.Text = "M39 首领队";
            Named<NumericUpDown>("BossRunLimit")!.Value = 30;
            Named<NumericUpDown>("BossReviveRetry")!.Value = 1;
            var statue = bossVm.ReturnToStatueAfterEachRound;
            Named<ToggleSwitch>("BossReturnToStatue")!.IsChecked = !statue;
            await Settle(600);
            Check("显示累计次数", !string.IsNullOrEmpty(Named<TextBlock>("BossCompletedText")?.Text), Named<TextBlock>("BossCompletedText")?.Text);
            Shot("avalonia-boss-editor.png");
            Named<NumericUpDown>("BossRunLimit")?.BringIntoView();
            await Settle();
            Shot("avalonia-boss-editor-2.png");
            await Click(Named<Button>("ApplyNodeButton"), "应用（首领讨伐）");
            var p = NodeParams(id, boss);
            var t = p.ToString(Newtonsoft.Json.Formatting.None);
            Check("存盘：首领与队伍", bossName is not null && p.Value<string>("boss_name") == bossName && p.Value<string>("team_name") == "M39 首领队", t);
            Check("存盘：累计上限与重试", p.Value<int?>(Pulonia.Executors.PuloniaSoloTaskExecutor.BossTotalRunCountLimitParameter) == 30
                                     && p.Value<int?>("revive_retry_count") == 1, t);
            Check("存盘：返回神像", p.Value<bool?>("return_to_statue_after_each_round") == !statue, t);
        }

        if (await SelectDedicatedAsync<ViewModel.SoloTaskSettings.AutoStygianOnslaughtSettingsViewModel>(page, stygian) is { } styVm)
        {
            Named<ComboBox>("StygianBossNum")!.SelectedItem = 2;
            Named<TextBox>("StygianTeamName")!.Text = "M39 幽境队";
            Named<ToggleSwitch>("StygianArtifactSalvage")!.IsChecked = true;
            await Settle();
            Named<ComboBox>("StygianMaxStar")!.SelectedItem = "2";
            await Settle();
            Shot("avalonia-stygian-editor.png");
            await Click(Named<Button>("ApplyNodeButton"), "应用（幽境危战）");
            var p = NodeParams(id, stygian);
            var t = p.ToString(Newtonsoft.Json.Formatting.None);
            Check("存盘：幽境首领与队伍", p.Value<int?>("boss_num") == 2 && p.Value<string>("fight_team_name") == "M39 幽境队", t);
            Check("存盘：幽境分解圣遗物", p.Value<bool?>("auto_artifact_salvage") == true && p.Value<string>("max_artifact_star") == "2", t);
        }

        await DeleteTempPlanAsync(page, id);
    }

    // —— 专属参数编辑器：体力计划（含子任务设置窗口） ——

    private async Task ResinPlanEditorAsync()
    {
        var page = vm.TaskPlan;
        if (await CreateTempPlanAsync(page, "M40 体力计划自测") is not { } id)
        {
            return;
        }

        const string plan = "solo.resin_plan";
        await AddSoloTaskAsync(page, plan);
        if (await SelectDedicatedAsync<ViewModel.SoloTaskSettings.ResinPlanSettingsViewModel>(page, plan) is { } rp)
        {
            Named<ToggleSwitch>("RpLimitResin")!.IsChecked = true;
            await Settle();
            Named<NumericUpDown>("RpResinLimit")!.Value = 120;
            Named<ToggleSwitch>("RpCraftFirst")!.IsChecked = true;
            await Settle();
            Named<ComboBox>("RpCraftCountry")!.SelectedItem = "璃月";
            var bossEnabled = rp.Boss.Enabled;
            rp.Boss.Enabled = !bossEnabled;
            await Settle();
            Check("子任务设置可用", rp.Domain.CanEdit);
            Shot("avalonia-resin-plan-editor.png");

            // 自动秘境子任务：改队伍名称后确定
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => rp.Domain.EditCommand.Execute(null));
            var dialog = await WaitFor(() => ResinPlanSubTaskWindow.Current);
            await Settle(800);
            Check("打开子任务设置窗口", dialog is not null);
            if (dialog is not null)
            {
                var fields = dialog.GetVisualDescendants().OfType<StackPanel>().Where(p => p.Tag is string && p.IsEffectivelyVisible).Select(p => (string)p.Tag!).ToList();
                Check("子任务字段不含隐藏字段", fields.Count > 0 && !fields.Contains("domain_name") && !fields.Contains("resin_usage"), string.Join(",", fields));
                var party = dialog.GetVisualDescendants().OfType<StackPanel>().FirstOrDefault(p => p.Tag as string == "party_name")
                    ?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.IsEffectivelyVisible);
                Check("队伍名称输入框", party is not null);
                if (party is not null) party.Text = "M40 队伍";
                await Settle();
                var strategy = dialog.GetVisualDescendants().OfType<StackPanel>().FirstOrDefault(p => p.Tag as string == "combat_strategy_path")
                    ?.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault(c => c.IsEffectivelyVisible);
                Check("战斗策略显示默认（根据队伍自动选择）", strategy?.SelectedItem as string == GameTask.AutoFight.Strategy.CombatStrategyResolver.AutoSelectName,
                    strategy?.SelectedItem as string);
                dialog.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.ScrollToHome();
                await Settle();
                Shot(dialog, "avalonia-resin-plan-subtask.png");
                OnClickMethod.Invoke(NamedIn<Button>(dialog, "SubTaskConfirmButton")!, null);
                await WaitFor(() => ResinPlanSubTaskWindow.Current is null ? "closed" : null);
                await Settle();
            }

            Check("子任务摘要更新", rp.Domain.Summary.Contains("M40 队伍"), rp.Domain.Summary);
            await Click(Named<Button>("ApplyNodeButton"), "应用（体力计划）");
            var p = NodeParams(id, plan);
            var t = p.ToString(Newtonsoft.Json.Formatting.None);
            Check("存盘：体力上限", p.Value<int?>("max_resin") == 120, t);
            Check("存盘：优先合成与地区", p.Value<bool?>("craft_condensed_first") == true && p["craft"]?.Value<string>("country") == "璃月", t);
            Check("存盘：首领参与", p["boss"]?.Value<bool?>("enabled") == !bossEnabled, t);
            Check("存盘：秘境子任务队伍", p["domain"]?["param"]?.Value<string>("party_name") == "M40 队伍", t);
        }

        await DeleteTempPlanAsync(page, id);
    }

    // —— 独立任务页：自动秘境卡片的配置项（写入 config.json 的 autoDomainConfig） ——

    private async Task SoloDomainAsync()
    {
        var configPath = Path.Combine(Global.StartUpPath, "User", "config.json");
        vm.Navigate("独立任务");
        await Settle(1000);
        if (ButtonByText("收起") is { } collapse) await Click(collapse, "收起日志");
        var card = await WaitFor(() => All<Expander>().FirstOrDefault(e => e.DataContext is SoloTaskCardViewModel { IsDomain: true }));
        Check("找到自动秘境卡片", card is not null);
        if (card is null) return;
        card.IsExpanded = true;
        card.BringIntoView();
        await Settle(800);
        var d = vm.Tasks.Domain;
        Named<TextBox>("SdPartyName")!.Text = "M40 秘境队";
        Named<RadioButton>("SdSourceManual")!.IsChecked = true;
        await Settle();
        var domainBox = Named<ComboBox>("SdDomainName");
        Check("指定秘境时显示秘境下拉", domainBox is not null && Named<ToggleSwitch>("SdCalculateRuns") is null);
        // 选一个有奖励材料的秘境，供“按材料数量提前结束”使用
        var domain = domainBox?.Items.OfType<string>().FirstOrDefault(n => GameTask.Common.Reward.TargetItemCatalog.Instance.TryGetDomainItems(n, out var items) && items.Count > 0)
                     ?? (domainBox?.ItemCount > 2 ? domainBox.Items[2] as string : null);
        if (domainBox is not null && domain is not null) domainBox.SelectedItem = domain;
        Named<ToggleSwitch>("SdArtifactSalvage")!.IsChecked = true;
        await Settle();
        Named<ComboBox>("SdMaxStar")!.SelectedItem = "2";
        Named<TextBox>("SdFightEndDelay")!.Text = "3.5";
        Named<ToggleSwitch>("SdAutoEat")!.IsChecked = true;
        Named<TextBox>("SdReviveRetry")!.Text = "2";
        Named<TextBox>("SdLeftRightMoveTimes")!.Text = "4";
        await Settle();
        Named<TextBox>("SdPartyName")?.BringIntoView();
        await Settle();
        Shot("avalonia-solo-domain-manual.png");
        Named<TextBox>("SdReviveRetry")?.BringIntoView();
        await Settle();
        Shot("avalonia-solo-domain-manual-2.png");
        Check("配置：指定秘境", d.Config.DomainName == domain && domain is not null, d.Config.DomainName);

        // 按材料数量提前结束：开启后添加一项材料，数量 30
        var targetMode = Named<ToggleSwitch>("SdTargetMode");
        Check("材料目标开关可用（秘境有奖励材料）", targetMode?.IsEffectivelyEnabled == true, string.Join(",", d.TargetItems.Choices.Select(c => c.Name)));
        if (targetMode is { IsEffectivelyEnabled: true })
        {
            targetMode.IsChecked = true;
            await Settle();
            if (d.TargetItems.Items.Count == 0) await Click(Named<Button>("SdTargetAdd"), "添加材料");
            await Settle();
            if (d.TargetItems.Items.FirstOrDefault() is { } first) first.TargetCount = 30;
            await Settle();
            Named<Border>("SdTargetItems")?.BringIntoView();
            await Settle();
            Shot("avalonia-solo-domain-targets.png");
            Check("未出现重复选择提示", !d.TargetItems.HasDuplicateAttempt);
            Check("材料目标写入配置", d.Config.TargetItems.Count == 1 && d.Config.TargetItems[0].ItemName.Length > 0 && d.Config.TargetItems[0].TargetCount == 30,
                string.Join(",", d.Config.TargetItems.Select(t => $"{t.ItemName}×{t.TargetCount}")));
            // 切换为跟随培养计划后秘境变化会按新候选清理目标（与 WPF 相同），因此在此核对存盘
            await Task.Delay(3000);
            var saved = ReadJson(configPath)["autoDomainConfig"]?["targetItems"];
            Check("config.json：材料目标", (saved as JArray)?.Count == 1 && (int?)saved?[0]?["targetCount"] == 30, saved?.ToString(Newtonsoft.Json.Formatting.None));
        }

        // 跟随培养计划
        Named<RadioButton>("SdSourceTraining")!.IsChecked = true;
        await Settle();
        Check("跟随培养计划显示培养设置", Named<ToggleSwitch>("SdCalculateRuns") is not null && Named<ComboBox>("SdDomainName") is null);
        Named<ComboBox>("SdRunPreference")!.SelectedIndex = 0;
        Named<NumericUpDown>("SdReservePercent")!.Value = 10;
        Named<ComboBox>("SdFallbackSunday")!.SelectedItem = "1";
        Named<ToggleSwitch>("SdLowAr")!.IsChecked = true;
        await Settle();
        Named<ToggleSwitch>("SdCalculateRuns")?.BringIntoView();
        await Settle();
        Shot("avalonia-solo-domain-training.png");

        // 树脂使用对话框：刷至耗尽改为指定次数 2
        var resinDialog = await OpenResinDialogAsync(d.DomainResin.EditCommand);
        Check("打开树脂设置对话框", resinDialog is not null);
        if (resinDialog is not null)
        {
            if (InDialog<RadioButton>(resinDialog, "ResinModeRunCount") is { IsEffectivelyEnabled: true } rc) rc.IsChecked = true;
            await Settle();
            if (InDialog<NumericUpDown>(resinDialog, "ResinRunCount") is { } n) n.Value = 2;
            await Settle();
            OnClickMethod.Invoke(InDialog<Button>(resinDialog, "ResinConfirmButton")!, null);
            await WaitFor(() => ResinSettingsWindow.Current is null ? "closed" : null);
            await Settle();
        }

        // 配置防抖写盘后核对 config.json
        await Task.Delay(3000);
        var c = ReadJson(configPath)["autoDomainConfig"];
        var text = c?.ToString(Newtonsoft.Json.Formatting.None) ?? "";
        Check("config.json：队伍", (string?)c?["partyName"] == "M40 秘境队", text.Length > 300 ? text[..300] : text);
        Check("config.json：跟随培养计划", (string?)c?["domainName"] == GameTask.AutoDomain.AutoDomainTask.TrainingGuideOption, (string?)c?["domainName"]);
        Check("config.json：预留率 / 备选奖励 / 低冒险等阶", (int?)c?["trainingGuideCraftingBonusReservePercent"] == 10
                                                           && (string?)c?["trainingGuideFallbackSundaySelectedValue"] == "1"
                                                           && (bool?)c?["trainingGuideLowARAdaptationEnabled"] == true);
        Check("config.json：分解圣遗物", (bool?)c?["autoArtifactSalvage"] == true
                                     && (string?)ReadJson(configPath)["autoArtifactSalvageConfig"]?["maxArtifactStar"] == "2");
        Check("config.json：等待 / 吃药 / 重试 / 左右移动", Math.Abs(((double?)c?["fightEndDelay"] ?? 0) - 3.5) < 0.01 && (bool?)c?["autoEat"] == true
                                                       && (int?)c?["reviveRetryCount"] == 2 && (int?)c?["leftRightMoveTimes"] == 4);
        Check("config.json：树脂使用", d.Config.ResinUsage?.Mode == GameTask.Common.Resin.ResinUsageMode.RunCount && (int?)c?["resinUsage"]?["runCount"] == 2,
            c?["resinUsage"]?.ToString(Newtonsoft.Json.Formatting.None));
    }

    // —— 任务计划快捷键触发：编辑器新增快捷键触发、注册（XGrabKey）、xdotool 按键后计划启动 ——

    private async Task PlanHotkeyAsync()
    {
        var page = vm.TaskPlan;
        var services = vm.Host?.Services;
        var tasks = services?.GetService(typeof(Pulonia.Services.IPuloniaTaskService)) as Pulonia.Services.IPuloniaTaskService;
        var scheduler = services?.GetService(typeof(Host.Common.PuloniaSchedulerHost)) as Host.Common.PuloniaSchedulerHost;
        Check("调度与快捷键服务", tasks is not null && scheduler?.Hotkeys is not null);
        if (tasks is null || scheduler?.Hotkeys is null) return;
        if (await CreateTempPlanAsync(page, "M42 快捷键自测") is not { } id) return;
        try
        {
            page.SelectedTab = 2;
            await Settle(600);
            await Click(Named<Button>("AddHotkeyTriggerButton"), "＋ 快捷键触发");
            var editor = page.TriggerEditor;
            Check("快捷键触发编辑器", editor is { IsHotkey: true } && Named<TextBox>("TriggerHotkeyBox") is not null, editor?.Title);
            if (editor is null) return;
            // 键位校验：全局热键的普通单键无效
            editor.Hotkey = "A";
            await Settle();
            Check("全局热键拒绝普通单键", !editor.IsValid, editor.PreviewText);
            // Ctrl + Alt + F1~F12 被 X 服务器当作切换虚拟终端，拒绝
            editor.Hotkey = "Ctrl + Alt + F9";
            await Settle();
            Check("拒绝 Ctrl + Alt + F 键（切换虚拟终端）", !editor.IsValid, editor.PreviewText);
            // 录制：模拟页面收到 Ctrl + Shift + F9
            editor.IsRecording = true;
            editor.OnRecordKey("F9", true, true, false, false);
            Check("录制快捷键", editor.Hotkey == "Ctrl + Shift + F9" && !editor.IsRecording, editor.Hotkey);
            Named<CheckBox>("TriggerEnabledBox")!.IsChecked = true;
            await Settle();
            Check("预览", editor.IsValid && editor.PreviewText.Contains("Ctrl + Shift + F9"), editor.PreviewText);
            await Click(Named<Button>("SaveTriggerButton"), "保存触发器");
            var saved = ReadJson(Global.Absolute($@"User\Pulonia\plans\{id}.json"))["triggers"]?.FirstOrDefault();
            Check("存盘：快捷键触发", saved?.ToString(Newtonsoft.Json.Formatting.None).Contains("Ctrl + Shift + F9") == true, saved?.ToString(Newtonsoft.Json.Formatting.None));

            // 调度循环读取新计划后注册
            await WaitFor(() => scheduler.Hotkeys.Registered.Any(r => r.Contains("Ctrl + Shift + F9")) ? "ok" : null);
            Check("已注册计划快捷键", scheduler.Hotkeys.Registered.Any(r => r.Contains("Ctrl + Shift + F9")),
                string.Join("；", scheduler.Hotkeys.Registered) + " " + scheduler.Hotkeys.Error);
            await Settle(1500);
            Shot("avalonia-plan-hotkey.png");

            // xdotool 在同一显示上按下快捷键
            var before = (await tasks.ListRunsAsync()).Count(r => r.PlanId == id);
            var psi = new System.Diagnostics.ProcessStartInfo("xdotool", "key --delay 120 ctrl+shift+F9") { UseShellExecute = false, RedirectStandardError = true };
            using (var proc = System.Diagnostics.Process.Start(psi))
            {
                proc?.WaitForExit(5000);
                Check("xdotool 按键", proc?.ExitCode == 0, $"DISPLAY={Environment.GetEnvironmentVariable("DISPLAY")} {proc?.StandardError.ReadToEnd()}");
            }
            string? runId = null;
            for (var i = 0; i < 40 && runId is null; i++)
            {
                await Task.Delay(500);
                runId = (await tasks.ListRunsAsync()).Where(r => r.PlanId == id).Skip(before).Select(r => r.RunId).FirstOrDefault();
            }

            Check("按下快捷键后计划启动", runId is not null, runId);
            if (runId is not null)
            {
                try { await tasks.CancelAsync(runId); } catch (Exception) { }
                await Settle(1500);
            }
        }
        finally
        {
            page.SelectedTab = 0;
            await Settle();
            await DeleteTempPlanAsync(page, id);
            await WaitFor(() => scheduler.Hotkeys.Registered.Count == 0 ? "ok" : null);
            Check("删除计划后注销快捷键", scheduler.Hotkeys.Registered.Count == 0, string.Join("；", scheduler.Hotkeys.Registered));
        }
    }

    // —— 计划引用：引用已有计划、互相引用拒绝、被引用计划不可删除 ——

    private async Task PlanReferenceAsync()
    {
        var page = vm.TaskPlan;
        if (await CreateTempPlanAsync(page, "M42 引用自测 A") is not { } a) return;
        if (await CreateTempPlanAsync(page, "M42 引用自测 B") is not { } b)
        {
            await DeleteTempPlanAsync(page, a);
            return;
        }

        // 新建计划会重新加载列表，按 ID 取当前项
        var itemA = page.Plans.First(p => p.Plan.Id == a);
        var itemB = page.Plans.First(p => p.Plan.Id == b);
        page.SelectedTab = 0;
        await Settle();
        Check("引用候选不含自身", page.ReferencePlans.Contains(itemA) && !page.ReferencePlans.Contains(itemB));
        page.ReferenceTarget = itemA;
        await Click(Named<Button>("AddPlanReferenceButton"), "引用");
        var bJson = ReadJson(Global.Absolute($@"User\Pulonia\plans\{b}.json")).ToString(Newtonsoft.Json.Formatting.None);
        Check("存盘：引用节点", bJson.Contains("\"plan_id\":\"" + a + "\"") && bJson.Contains("引用：M42 引用自测 A"), bJson);
        Check("任务列表显示引用", page.Nodes.Any(n => n.Name == "引用：M42 引用自测 A" && n.Detail.StartsWith("引用计划")));
        await Settle(600);
        Shot("avalonia-plan-reference.png");

        // A 引用 B 会形成环
        page.SelectedPlan = page.Plans.First(p => p.Plan.Id == a);
        await Settle(400);
        itemB = page.Plans.First(p => p.Plan.Id == b);
        page.ReferenceTarget = itemB;
        await Click(Named<Button>("AddPlanReferenceButton"), "引用（成环）");
        var aJson = ReadJson(Global.Absolute($@"User\Pulonia\plans\{a}.json")).ToString(Newtonsoft.Json.Formatting.None);
        Check("拒绝互相引用", page.StatusMessage.Contains("互相引用") && !aJson.Contains(b), page.StatusMessage);

        // 被引用的 A 不能删除
        await Click(ButtonByText("删除计划"), "删除计划（被引用）");
        Check("被引用计划不可删除", File.Exists(Global.Absolute($@"User\Pulonia\plans\{a}.json")) && page.StatusMessage.Contains("仍引用"), page.StatusMessage);

        page.SelectedPlan = page.Plans.First(p => p.Plan.Id == b);
        await Settle(400);
        await DeleteTempPlanAsync(page, b);
        page.SelectedPlan = page.Plans.FirstOrDefault(p => p.Plan.Id == a);
        await Settle(400);
        await DeleteTempPlanAsync(page, a);
    }

    // —— 路线 CD 记录页签：读取账本中的路线记录 ——

    private async Task PlanCooldownAsync()
    {
        var page = vm.TaskPlan;
        vm.Navigate("任务计划");
        await page.ReloadAsync();
        await Settle(800);
        var tasks = vm.Host?.Services.GetService(typeof(Pulonia.Services.IPuloniaTaskService)) as Pulonia.Services.IPuloniaTaskService;
        page.SelectedTab = 4;
        await Settle(400);
        page.CooldownsForPlanOnly = false;
        await Click(Named<Button>("RefreshCooldownsButton"), "刷新路线 CD");
        var expected = tasks is null ? -1 : (await tasks.ListLedgerAsync()).Select(e => (e.ScopeKey, e.EffectKey)).Distinct()
            .Count(e => e.EffectKey.StartsWith(Pulonia.Services.PuloniaCollectionCooldown.EffectKeyPrefix, StringComparison.Ordinal));
        Check("路线 CD 记录与账本一致", page.Cooldowns.Count == expected, $"{page.Cooldowns.Count}/{expected}");
        Check("路线 CD 页签控件", Named<Button>("RefreshCooldownsButton") is not null
            && (page.Cooldowns.Count > 0 ? CooldownList() is not null : page.HasNoCooldowns && All<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text == "暂无路线 CD 记录。")));
        await Settle(400);
        Shot("avalonia-plan-cooldown.png");
        // 外部脚本预置的自测记录：点“清除”后从列表与账本中消失
        if (page.Cooldowns.FirstOrDefault(r => r.Route.Contains("M42自测路线")) is { } seeded && tasks is not null)
        {
            var clear = CooldownList()?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Content as string == "清除" && b.DataContext == seeded);
            await Click(clear, "清除路线 CD");
            await Settle(600);
            var left = (await tasks.ListLedgerAsync()).Count(e => e.EffectKey.Contains("M42自测路线"));
            Check("清除路线 CD 记录", left == 0 && page.Cooldowns.All(r => r != seeded), page.StatusMessage);
        }

        page.SelectedTab = 0;
        await Settle();
    }

    // —— 设置方案、共享设置与路线 CD 标记 / 清除（外部脚本预置一条路线完成记录：local:/tmp/m44-cd/示例路线.json） ——

    private async Task PlanProfilesCooldownAsync()
    {
        const string route = "/tmp/m44-cd/示例路线.json";
        var page = vm.TaskPlan;
        var tasks = vm.Host?.Services.GetService(typeof(Pulonia.Services.IPuloniaTaskService)) as Pulonia.Services.IPuloniaTaskService;
        if (tasks is null || await CreateTempPlanAsync(page, "M43 方案与 CD 自测") is not { } id) return;
        var planFile = Global.Absolute($@"User\Pulonia\plans\{id}.json");
        string? profileId = null, copyId = null;
        try
        {
            var ledger = await tasks.ListLedgerAsync();
            Check("预置的路线记录已加载", ledger.Any(e => e.EffectKey.Contains("m44-cd/示例")), string.Join(",", ledger.Select(e => e.EffectKey)));
            page.SelectedTab = 0;
            await page.AppendTaskAsync(new Pulonia.Models.PuloniaTask { Name = "M43 CD 自测路线", TaskType = "pathing", Path = route });
            await Settle(500);
            TaskPlanNodeRow? Row() => page.Nodes.FirstOrDefault(n => n.Name == "M43 CD 自测路线");
            Check("路线节点：自动识别未匹配 → 未识别 CD", Row()?.CooldownBadge == "未识别 CD", Row()?.CooldownBadge ?? "无标记");

            // 设置方案：新建、复制、重命名
            page.SelectedTab = 3;
            await Settle(500);
            page.ProfileName = "M43 自测方案";
            await Click(Named<Button>("CreateProfileButton"), "新建方案");
            profileId = page.SelectedProfile?.Profile?.Id;
            Check("新建方案写入文件", profileId is not null && File.Exists(Global.Absolute($@"User\Pulonia\profiles\{profileId}.json")), page.StatusMessage);
            await Click(Named<Button>("DuplicateProfileButton"), "复制方案");
            copyId = page.SelectedProfile?.Profile?.Id;
            Check("复制方案", page.SelectedProfile?.Name == "M43 自测方案 副本" && copyId != profileId, page.StatusMessage);
            page.ProfileName = "M43 自测方案 改名";
            await Click(Named<Button>("RenameProfileButton"), "重命名方案");
            var copyJson = copyId is null ? "" : File.ReadAllText(Global.Absolute($@"User\Pulonia\profiles\{copyId}.json"));
            Check("重命名方案", copyJson.Contains("M43 自测方案 改名"), page.StatusMessage);

            // 方案共享设置：路线 CD 改为滚动 48 小时
            page.SelectedProfile = page.Profiles.First(p => p.Profile?.Id == profileId);
            await Settle(300);
            await Click(Named<Button>("EditProfileSettingsButton"), "编辑方案设置");
            var editor = page.SharedEditor!;
            editor.SelectedCategory = editor.Categories.First(c => c.Id == Pulonia.Services.PuloniaSettingCatalog.CollectionCategory);
            await Settle(300);
            var profileCd = editor.Row(Pulonia.Services.PuloniaSettingCatalog.CollectionCooldownKey)!;
            profileCd.CdChoiceIndex = 2;
            profileCd.CdModeIndex = 0;
            profileCd.Cooldown!.Hours = "48";
            Named<Border>("SharedSettingsPanel")?.BringIntoView();
            await Settle(400);
            Shot("avalonia-plan-profiles.png");
            await Click(Named<Button>("SaveSharedSettingsButton"), "保存方案设置");
            var profileJson = File.ReadAllText(Global.Absolute($@"User\Pulonia\profiles\{profileId}.json"));
            Check("方案设置存盘", profileJson.Contains("rolling") && profileJson.Contains("48"), editor.Message);

            // 计划引用方案 → 标记变为 CD 中
            page.PlanProfile = page.PlanProfileOptions.First(p => p.Profile?.Id == profileId);
            await Settle(500);
            Check("计划引用方案存盘", File.ReadAllText(planFile).Contains($"\"profile_id\": \"{profileId}\""), page.StatusMessage);
            Check("方案规则生效：CD 中", Row()?.CooldownBadge?.StartsWith("CD 中") == true, Row()?.CooldownBadge ?? "无标记");
            page.SelectedProfile = page.Profiles.First(p => p.Profile?.Id == profileId);
            await Settle(200);
            await Click(Named<Button>("DeleteProfileButton"), "删除使用中的方案");
            Check("使用中的方案不可删除", page.StatusMessage.Contains("仍在使用") && File.Exists(Global.Absolute($@"User\Pulonia\profiles\{profileId}.json")), page.StatusMessage);

            // 计划级共享设置覆盖方案：一次性路线 → 已完成
            await Click(Named<Button>("EditPlanSettingsButton"), "编辑计划级共享设置");
            editor = page.SharedEditor!;
            editor.SelectedCategory = editor.Categories.First(c => c.Id == Pulonia.Services.PuloniaSettingCatalog.CollectionCategory);
            var cdRow = editor.Row(Pulonia.Services.PuloniaSettingCatalog.CollectionCooldownKey)!;
            Check("计划级设置显示继承自方案", cdRow.InheritedText.Contains("48") && !cdRow.IsOverridden, cdRow.InheritedText);
            cdRow.CdChoiceIndex = 2;
            cdRow.CdModeIndex = 4;
            await Click(Named<Button>("SaveSharedSettingsButton"), "保存计划级设置");
            Check("计划级设置存盘", File.ReadAllText(planFile).Replace(" ", "").Contains("\"mode\":\"once\""), editor.Message);
            Check("计划级覆盖生效：已完成", Row()?.CooldownBadge == "已完成", Row()?.CooldownBadge ?? "无标记");
            cdRow.CdModeIndex = 0;
            cdRow.Cooldown!.Hours = "abc";
            await Click(Named<Button>("SaveSharedSettingsButton"), "保存无效设置");
            Check("无效设置被拒绝", editor.Message.StartsWith("设置无效"), editor.Message);
            page.SharedEditor = null;
            page.SelectedTab = 0;
            await Settle(500);
            Shot("avalonia-plan-cd-badge.png");

            // 路线 CD 页签：仅本计划 → 清除
            page.SelectedTab = 4;
            page.CooldownsForPlanOnly = true;
            await Settle(800);
            Check("按计划筛选的 CD 记录", page.Cooldowns.Count == 1 && page.Cooldowns[0].Route.Contains("m44-cd"), $"{page.Cooldowns.Count}");
            Shot("avalonia-plan-cd-overview-plan.png");
            var seeded = page.Cooldowns.FirstOrDefault();
            var clear = CooldownList()?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Content as string == "清除" && b.DataContext == seeded);
            await Click(clear, "清除路线 CD");
            await Settle(800);
            var left = (await tasks.ListLedgerAsync()).Count(e => e.EffectKey.Contains("m44-cd/示例"));
            Check("清除路线 CD 记录", left == 0 && page.Cooldowns.Count == 0, page.StatusMessage);
            Check("清除后标记消失", Row()?.CooldownBadge is null, Row()?.CooldownBadge);
        }
        finally
        {
            // 清理：计划改回不用方案后删除方案与计划
            page.SelectedTab = 3;
            page.SharedEditor = null;
            page.PlanProfile = TaskPlanProfileItem.None;
            await Settle(500);
            foreach (var pid in new[] { profileId, copyId })
            {
                if (pid is null || page.Profiles.FirstOrDefault(p => p.Profile?.Id == pid) is not { } prof) continue;
                page.SelectedProfile = prof;
                await Settle(200);
                await Click(Named<Button>("DeleteProfileButton"), "删除方案");
                Check("删除方案文件", !File.Exists(Global.Absolute($@"User\Pulonia\profiles\{pid}.json")), page.StatusMessage);
            }

            page.SelectedTab = 0;
            await Settle();
            await DeleteTempPlanAsync(page, id);
        }
    }

    // —— 节点共享设置、目录引用 CD 汇总与一条龙账号作用域（外部脚本预置默认身份 3 条、UID 100000044 一条） ——

    private async Task PlanNodeCooldownAsync()
    {
        var page = vm.TaskPlan;
        var store = vm.Host?.Services.GetService(typeof(Pulonia.Services.PuloniaTaskStore)) as Pulonia.Services.PuloniaTaskStore;
        if (store is null || await CreateTempPlanAsync(page, "M44 节点与目录自测") is not { } id) return;
        var planFile = Global.Absolute($@"User\Pulonia\plans\{id}.json");
        string? dragonId = null;
        long dragonRevision = 0;
        try
        {
            page.SelectedTab = 0;
            await page.AppendTaskAsync(new Pulonia.Models.PuloniaTask
            {
                Name = "M44 分组", TaskType = "group",
                Children = [new Pulonia.Models.PuloniaTask { Name = "M44 分组内路线", TaskType = "pathing", Path = "/tmp/m44-cd/示例路线.json" }]
            });
            await page.AppendTaskAsync(new Pulonia.Models.PuloniaTask
            {
                Name = "M44 目录引用", TaskType = "group",
                Source = new Pulonia.Models.PuloniaTaskSource { Kind = "directory", TaskType = "pathing", Path = "/tmp/m44-cd/目录" }
            });
            await Settle(500);
            TaskPlanNodeRow? Row(string name) => page.Nodes.FirstOrDefault(n => n.Name == name);
            Check("初始：分组汇总未识别", Row("M44 分组")?.CooldownBadge == "1 项未识别 CD", Row("M44 分组")?.CooldownBadge ?? "无标记");

            // 分组级共享设置：只列出下属路线用到的设置项，CD 用专用表单设为滚动 48 小时
            page.SelectedNode = Row("M44 分组");
            await Settle(300);
            await Click(Named<Button>("NodeSettingsButton"), "分组共享设置");
            var editor = page.SharedEditor;
            Check("分组共享设置只列出使用的分类", editor is not null && editor.Categories.Count < Pulonia.Services.PuloniaSettingCatalog.Categories.Count
                && editor.Categories.Any(c => c.Id == Pulonia.Services.PuloniaSettingCatalog.CollectionCategory),
                editor is null ? page.StatusMessage : string.Join(",", editor.Categories.Select(c => c.Id)));
            if (editor is null) return;
            editor.SelectedCategory = editor.Categories.First(c => c.Id == Pulonia.Services.PuloniaSettingCatalog.CollectionCategory);
            var cd = editor.Row(Pulonia.Services.PuloniaSettingCatalog.CollectionCooldownKey)!;
            cd.CdChoiceIndex = 2;
            cd.CdModeIndex = 0;
            cd.Cooldown!.Hours = "48";
            await Settle(400);
            Check("CD 表单预览", cd.Cooldown.Preview.Contains("48"), cd.Cooldown.Preview);
            Named<Border>("SharedSettingsPanel")?.BringIntoView();
            await Settle(300);
            Shot("avalonia-plan-node-settings.png");
            await Click(Named<Button>("SaveSharedSettingsButton"), "保存分组共享设置");
            var saved = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(planFile));
            var groupJson = saved.SelectToken("$..children[?(@.name == 'M44 分组')].settings")?.ToString(Newtonsoft.Json.Formatting.None) ?? "";
            Check("分组共享设置存盘", groupJson.Contains("rolling") && groupJson.Contains("48"), groupJson);
            page.SharedEditor = null;
            page.SelectedTab = 0;
            await Settle(500);
            Check("分组设置生效：路线 CD 中", Row("M44 分组内路线")?.CooldownBadge?.StartsWith("CD 中") == true, Row("M44 分组内路线")?.CooldownBadge ?? "无标记");
            Check("分组汇总 1/1 CD 中", Row("M44 分组")?.CooldownBadge == "1/1 CD 中", Row("M44 分组")?.CooldownBadge ?? "无标记");
            Check("分组显示自有设置标记", Row("M44 分组")?.HasOwnSettings == true);

            // 目录引用节点的共享设置 → 目录下已记录路线汇总
            page.SelectedNode = Row("M44 目录引用");
            await Settle(300);
            await Click(Named<Button>("NodeSettingsButton"), "目录共享设置");
            editor = page.SharedEditor!;
            editor.SelectedCategory = editor.Categories.First(c => c.Id == Pulonia.Services.PuloniaSettingCatalog.CollectionCategory);
            cd = editor.Row(Pulonia.Services.PuloniaSettingCatalog.CollectionCooldownKey)!;
            cd.CdChoiceIndex = 2;
            cd.CdModeIndex = 0;
            cd.Cooldown!.Hours = "48";
            await Click(Named<Button>("SaveSharedSettingsButton"), "保存目录共享设置");
            page.SharedEditor = null;
            page.SelectedTab = 0;
            await Settle(500);
            Check("目录引用汇总 2 项 CD 中", Row("M44 目录引用")?.CooldownBadge?.StartsWith("2 项") == true, Row("M44 目录引用")?.CooldownBadge ?? "无标记");
            Check("计划 CD 汇总", page.PlanCooldownSummary?.Contains("3") == true, page.PlanCooldownSummary ?? "无");
            Shot("avalonia-plan-node-cd.png");
            page.SelectedTab = 4;
            page.CooldownsForPlanOnly = true;
            await Settle(800);
            Check("按计划筛选含目录下记录", page.Cooldowns.Count == 3 && page.Cooldowns.All(c => !c.Route.Contains("账号路线")), $"{page.Cooldowns.Count}");

            // 一条龙绑定账号：CD 作用域取该账号 UID
            var accounts = await store.LoadAccountSettingsAsync();
            accounts.Accounts.RemoveAll(a => a.Name == "M44 测试账号");
            var account = new Pulonia.Models.PuloniaGenshinAccountProfile { Name = "M44 测试账号", Uid = "100000044" };
            accounts.Accounts.Add(account);
            await store.SaveAccountSettingsAsync(accounts);
            var dragon = await store.SavePlanAsync(new Pulonia.Models.PuloniaTaskPlan
            {
                Name = "M44 一条龙自测", Purpose = Pulonia.Models.PuloniaTaskPlanPurpose.OneDragon,
                Accounts = [new Pulonia.Models.PuloniaTaskAccountBinding { AccountId = account.Id }],
                RootTask = new Pulonia.Models.PuloniaTask
                {
                    Name = "M44 一条龙自测", TaskType = "group",
                    Settings = new Newtonsoft.Json.Linq.JObject { [Pulonia.Services.PuloniaSettingCatalog.CollectionCooldownKey] = Newtonsoft.Json.Linq.JObject.Parse("{\"mode\":\"rolling\",\"hours\":48}") },
                    Children = [new Pulonia.Models.PuloniaTask { Name = "M44 账号路线", TaskType = "pathing", Path = "/tmp/m44-cd/账号路线.json" },
                        new Pulonia.Models.PuloniaTask { Name = "M44 默认身份路线", TaskType = "pathing", Path = "/tmp/m44-cd/示例路线.json" }]
                }
            });
            dragonId = dragon.Id;
            dragonRevision = dragon.Revision;
            page.CooldownsForPlanOnly = false;
            await Settle(1000);
            Check("一条龙作用域为绑定账号 UID", page.GetCooldownScope(dragon) == "cn/world/100000044", page.GetCooldownScope(dragon));
            var accountLeaf = page.DescribeCooldown([dragon.RootTask, dragon.RootTask.Children[0]]);
            var defaultLeaf = page.DescribeCooldown([dragon.RootTask, dragon.RootTask.Children[1]]);
            Check("账号路线按 UID 记录 CD 中", accountLeaf.Text?.StartsWith("CD 中") == true && accountLeaf.Tip?.Contains("M44 测试账号") == true,
                $"{accountLeaf.Text} | {accountLeaf.Tip}");
            Check("默认身份记录不影响该账号", defaultLeaf.Text is null, defaultLeaf.Text ?? "无标记");
            var scoped = page.Cooldowns.FirstOrDefault(c => c.Route.Contains("账号路线"));
            Check("总览显示账号作用域", scoped?.Scope == "M44 测试账号（UID 100000044）", scoped?.Scope ?? "无");
            CooldownList()?.ScrollIntoView(scoped!);
            await Settle(400);
            Shot("avalonia-plan-cd-overview.png");
        }
        finally
        {
            if (dragonId is not null) await store.DeletePlanAsync(dragonId, dragonRevision);
            page.SharedEditor = null;
            page.SelectedTab = 0;
            await Settle();
            await DeleteTempPlanAsync(page, id);
        }
    }

    // —— 开发者快捷键：模板素材制作、路径记录器 / 添加路径点（假游戏带小地图场景，xdotool 按键） ——

    private async Task DevHotkeysAsync()
    {
        const string tplRoot = "/tmp/m45-tpl";
        var settings = vm.Settings;
        if (Directory.Exists(tplRoot)) Directory.Delete(tplRoot, true);
        var runtime = await WaitFor(() => vm.Host?.RuntimeService.Current);
        Check("截图器已启动", runtime is not null);
        if (runtime is null) return;
        var keys = new Dictionary<string, string>
        {
            ["RecognitionTemplateEditorHotkey"] = "Ctrl + Shift + F10",
            ["PathRecorderHotkey"] = "Ctrl + Shift + F11",
            ["AddWaypointHotkey"] = "Ctrl + Shift + F12"
        };
        foreach (var (property, key) in keys)
        {
            var item = settings.Hotkeys.First(h => h.Property == property);
            item.Value = key;
        }

        settings.RegisterHotkeys();
        foreach (var property in keys.Keys)
        {
            var item = settings.Hotkeys.First(h => h.Property == property);
            Check($"注册 {item.Name}", item.IsSupported && item.Status.StartsWith("已注册"), item.Status);
        }

        bool Press(string key)
        {
            var psi = new System.Diagnostics.ProcessStartInfo("xdotool", $"key --delay 120 {key}") { UseShellExecute = false, RedirectStandardError = true };
            using var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit(5000);
            return proc?.ExitCode == 0;
        }

        // 1. 模板素材制作：截取画面 → 框选派蒙菜单 → 保存图片与 Recognition.json
        Check("xdotool 模板素材快捷键", Press("ctrl+shift+F10"));
        var window = await WaitFor(() => Views.TemplateEditorWindow.Current);
        Check("快捷键打开模板素材制作窗口", window is not null);
        if (window?.DataContext is ViewModels.TemplateEditorViewModel editor)
        {
            Check("画布为当前游戏截图", editor.CanvasWidth == 1920 && editor.CanvasHeight == 1080, $"{editor.CanvasWidth}x{editor.CanvasHeight}");
            editor.SetSelection(25, 15, 25 + 40, 15 + 40);
            editor.RecognitionJsonPath = tplRoot + "/Recognition.json";
            editor.AssetsRootPath = tplRoot;
            editor.TemplateFileName = "PaimonMenu Test";
            await Settle(600);
            Check("文件名生成对象名", editor.ObjectName == "PaimonMenuTest", editor.ObjectName);
            Shot(window, "avalonia-template-editor.png");
            await Click(window.FindControl<Button>("SaveTemplateButton"), "保存模板素材");
            await Settle(800);
            var image = tplRoot + "/1920x1080/paimon_menu_test.png";
            var json = File.Exists(tplRoot + "/Recognition.json") ? File.ReadAllText(tplRoot + "/Recognition.json") : "";
            using (var saved = File.Exists(image) ? OpenCvSharp.Cv2.ImRead(image) : new OpenCvSharp.Mat())
            {
                Check("模板图片按选区裁剪保存", saved.Width == 40 && saved.Height == 40, $"{image} {saved.Width}x{saved.Height}");
            }

            Check("Recognition.json 写入对象", json.Contains("PaimonMenuTest") && json.Contains("paimon_menu_test.png"), json.Length.ToString());
            Check("保存后关闭窗口并记住路径", Views.TemplateEditorWindow.Current is null
                && settings.Config.DevConfig.RecognitionJsonPath == tplRoot + "/Recognition.json", editor.Message);

            // 再次保存同名模板：提示冲突，确认后覆盖
            Check("xdotool 再次打开", Press("ctrl+shift+F10"));
            window = await WaitFor(() => Views.TemplateEditorWindow.Current);
            if (window?.DataContext is ViewModels.TemplateEditorViewModel again)
            {
                Check("再次打开沿用上次路径", again.RecognitionJsonPath == tplRoot + "/Recognition.json" && again.AssetsRootPath == tplRoot, again.AssetsRootPath);
                string? conflict = null;
                again.ConfirmOverwrite = text =>
                {
                    conflict = text;
                    return Task.FromResult(false);
                };
                again.SetSelection(20, 1030, 60, 1070);
                again.TemplateFileName = "paimon_menu_test";
                Check("同名冲突需确认，取消后不保存", !await again.SaveAsync() && conflict?.Contains("冲突") == true, conflict ?? again.Message);
                again.ConfirmOverwrite = _ => Task.FromResult(true);
                Check("确认后覆盖保存", await again.SaveAsync(), again.Message);
                window.Close();
                await Settle(300);
            }
        }

        // 2. 路径记录器：开始（初始传送点）→ 添加路径点（画面已切到另一位置）→ 停止并保存
        var before = Directory.Exists(BetterGenshinImpact.GameTask.AutoPathing.PathingPaths.PathJsonPath)
            ? Directory.GetFiles(BetterGenshinImpact.GameTask.AutoPathing.PathingPaths.PathJsonPath, "*.json").ToHashSet() : [];
        Check("xdotool 启动路径记录器", Press("ctrl+shift+F11"));
        for (var i = 0; i < 120 && settings.PathRecordCount < 1; i++) await Task.Delay(500);
        Check("记录初始传送点", settings.IsPathRecording && settings.PathRecordCount == 1, $"{settings.IsPathRecording} {settings.PathRecordCount}");
        await Task.Delay(4500); // 假游戏每 4 秒切换小地图位置
        Check("xdotool 添加路径点", Press("ctrl+shift+F12"));
        for (var i = 0; i < 60 && settings.PathRecordCount < 2; i++) await Task.Delay(500);
        Check("添加途经点", settings.PathRecordCount == 2, settings.PathRecordCount.ToString());
        Check("xdotool 停止路径记录器", Press("ctrl+shift+F11"));
        string? file = null;
        for (var i = 0; i < 20 && file is null; i++)
        {
            await Task.Delay(300);
            file = settings.LastPathRecordFile;
        }

        Check("停止后保存路线文件", file is not null && File.Exists(file) && !before.Contains(file) && !settings.IsPathRecording, file);
        if (file is not null && File.Exists(file))
        {
            var route = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));
            var positions = route["positions"] as Newtonsoft.Json.Linq.JArray ?? [];
            var p0 = positions.Count > 0 ? positions[0] : null;
            var p1 = positions.Count > 1 ? positions[1] : null;
            var distance = p0 is null || p1 is null ? 0 : Math.Sqrt(Math.Pow((double)p0["x"]! - (double)p1["x"]!, 2) + Math.Pow((double)p0["y"]! - (double)p1["y"]!, 2));
            Check("路线含传送点与途经点", positions.Count == 2 && (string?)p0?["type"] == "teleport" && (string?)p1?["type"] == "path",
                route.ToString(Newtonsoft.Json.Formatting.None));
            // 场景小地图取自精细地图的已知位置：(4618, 3594) 与 (4538, 3534)
            bool Known(Newtonsoft.Json.Linq.JToken? p) => p is not null
                && new[] { (4618d, 3594d), (4538d, 3534d) }.Any(k => Math.Abs((double)p["x"]! - k.Item1) < 3 && Math.Abs((double)p["y"]! - k.Item2) < 3);
            Check("坐标与场景小地图位置一致", Known(p0) && Known(p1), $"{p0?["x"]},{p0?["y"]} / {p1?["x"]},{p1?["y"]}");
            Check("两点位置随小地图变化", distance > 10, $"距离 {distance:0.0}");
            File.Copy(file, Path.Combine(dir ?? "/tmp", "path-record.json"), true);
            File.Delete(file);
        }
    }

    // —— 邮件与企业微信通知：只发往本机假 SMTP（127.0.0.1:18025）与假 Webhook（127.0.0.1:18045），由外部脚本记录收到的内容 ——

    private async Task NotifyChannelsAsync()
    {
        var settings = vm.Settings;
        var n = settings.Config.NotificationConfig;
        var outDir = Environment.GetEnvironmentVariable("FAKE_SERVER_OUT") ?? "/tmp/m45-fake";
        // 防护：其它渠道必须都未启用，避免向外部地址发送
        var others = typeof(BetterGenshinImpact.Service.Notification.NotificationConfig).GetProperties()
            .Where(p => p.PropertyType == typeof(bool) && p.Name.EndsWith("NotificationEnabled") && p.Name is not ("EmailNotificationEnabled" or "WorkweixinNotificationEnabled" or "JsNotificationEnabled"))
            .Where(p => (bool)p.GetValue(n)!).Select(p => p.Name).ToList();
        others.AddRange(n.WebhookEnabled ? ["WebhookEnabled"] : []);
        Check("其它通知渠道均未启用", others.Count == 0, string.Join(",", others));
        if (others.Count > 0) return;

        await WaitFor(() => vm.Host?.RuntimeService.Current);
        vm.Navigate("设置");
        await Settle(800);
        var tab = All<TabItem>().FirstOrDefault(t => t.Header as string == "通知");
        if (tab is not null) tab.IsSelected = true;
        await Settle(800);

        // 未启用时测试：与 WPF 相同提示“通知类型未启用”
        await Click(Named<Button>("WorkweixinTestButton"), "测试企业微信（未启用）");
        await WaitFor(() => string.IsNullOrEmpty(settings.WorkweixinStatus) ? null : "ok");
        Check("未启用时提示", settings.WorkweixinStatus.Contains("未启用"), settings.WorkweixinStatus);

        // 企业微信：界面填写地址并启用
        Named<TextBox>("WorkweixinUrlBox")!.Text = "http://127.0.0.1:18045/cgi-bin/webhook/send?key=m45-selftest";
        Named<ToggleSwitch>("WorkweixinSwitch")!.IsChecked = true;
        await Settle(300);
        Check("企业微信设置写入配置", n.WorkweixinNotificationEnabled && n.WorkweixinWebhookUrl.Contains("18045"), n.WorkweixinWebhookUrl);
        settings.WorkweixinStatus = "";
        await Click(Named<Button>("WorkweixinTestButton"), "测试企业微信");
        await WaitFor(() => string.IsNullOrEmpty(settings.WorkweixinStatus) ? null : "ok");
        Check("企业微信测试发送成功", settings.WorkweixinStatus.Contains("成功"), settings.WorkweixinStatus);
        var hook = File.Exists(Path.Combine(outDir, "webhook.jsonl")) ? File.ReadAllLines(Path.Combine(outDir, "webhook.jsonl")) : [];
        Check("假 Webhook 收到截图与文本", hook.Any(l => l.Contains("\"msgtype\": \"image\"") && l.Contains("key=m45-selftest"))
            && hook.Any(l => l.Contains("\"msgtype\": \"text\"") && l.Contains("这是一条测试通知信息")), $"{hook.Length} 条");

        // 邮件：界面填写 SMTP 设置并启用
        Named<TextBox>("SmtpServerBox")!.Text = "127.0.0.1";
        Named<TextBox>("SmtpPortBox")!.Text = "18025";
        Named<TextBox>("SmtpUserBox")!.Text = "";
        Named<TextBox>("FromEmailBox")!.Text = "bgi-selftest@example.invalid";
        Named<TextBox>("FromNameBox")!.Text = "BetterGI 自测";
        Named<TextBox>("ToEmailBox")!.Text = "";
        Named<ToggleSwitch>("EmailSwitch")!.IsChecked = true;
        await Settle(300);
        Check("SMTP 设置写入配置", n.EmailNotificationEnabled && n.SmtpServer == "127.0.0.1" && n.SmtpPort == 18025, $"{n.SmtpServer}:{n.SmtpPort}");
        await Click(Named<Button>("EmailTestButton"), "测试邮件（无收件人）");
        await WaitFor(() => string.IsNullOrEmpty(settings.EmailStatus) ? null : "ok");
        Check("收件人为空时报错", settings.EmailStatus.Contains("收件人"), settings.EmailStatus);
        Named<TextBox>("ToEmailBox")!.Text = "player@example.invalid";
        settings.EmailStatus = "";
        await Settle(300);
        await Click(Named<Button>("EmailTestButton"), "测试邮件");
        await WaitFor(() => string.IsNullOrEmpty(settings.EmailStatus) ? null : "ok");
        Check("邮件测试发送成功", settings.EmailStatus.Contains("成功"), settings.EmailStatus);
        var mail = File.Exists(Path.Combine(outDir, "mail.eml")) ? File.ReadAllText(Path.Combine(outDir, "mail.eml")) : "";
        var envelope = File.Exists(Path.Combine(outDir, "smtp-envelope.txt")) ? File.ReadAllText(Path.Combine(outDir, "smtp-envelope.txt")) : "";
        Check("假 SMTP 收到邮件", envelope.Contains("<player@example.invalid>") && envelope.Contains("<bgi-selftest@example.invalid>")
            && mail.Contains("screenshot.jpg") && mail.Contains("Subject:"), $"{mail.Length} 字节");
        if (ButtonByText("收起") is { } collapse) await Click(collapse, "收起日志");
        Named<Button>("EmailTestButton")?.BringIntoView();
        await Settle(600);
        Shot("avalonia-notify-channels.png");

        // 关闭后测试：提示未启用
        Named<ToggleSwitch>("EmailSwitch")!.IsChecked = false;
        settings.EmailStatus = "";
        await Settle(300);
        await Click(Named<Button>("EmailTestButton"), "测试邮件（已关闭）");
        await WaitFor(() => string.IsNullOrEmpty(settings.EmailStatus) ? null : "ok");
        Check("关闭后不再发送", settings.EmailStatus.Contains("未启用"), settings.EmailStatus);
        Named<ToggleSwitch>("WorkweixinSwitch")!.IsChecked = false;
    }

    private ListBox? CooldownList() => Named<ListBox>("CooldownList");

    // —— 执行记录 ——

    /// <summary>
    /// 执行记录页（--ui-test history）：新建只含一个 Shell 任务的计划并运行两次（成功 / 失败），
    /// 在执行记录页读取真实运行记录（Pulonia 历史文件），核对状态、节点详情，并对失败的记录“继续剩余”。
    /// 计划与历史写在 User/Pulonia，自测前后由外部脚本备份恢复
    /// </summary>
    private async Task HistoryAsync()
    {
        const string planName = "M51 记录自测";
        vm.Navigate("任务计划");
        var page = vm.TaskPlan;
        await page.ReloadAsync();
        await Settle(800);
        foreach (var old in page.Plans.Where(p => p.Name == planName).ToList())
        {
            page.SelectedPlan = old;
            await page.DeletePlanCommand.ExecuteAsync(null);
        }

        page.NewPlanName = planName;
        await Click(await WaitFor(() => All<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && ToolTip.GetTip(b) as string == "新建计划")), "新建计划");
        if (page.SelectedPlan?.Name != planName)
        {
            Check("新建计划", false);
            return;
        }

        page.SelectedAddOption = page.AddOptions.First(o => o.Definition.TaskType == "shell");
        await Settle();
        await Click(ButtonByText("＋ 添加"), "添加 Shell");
        page.SelectedNode = page.Nodes.First(n => n.Task.TaskType == "shell");
        await Settle();

        async Task<string?> RunShellAsync(string file)
        {
            Named<TextBox>("NodeNameBox")!.Text = "M51 Shell";
            FieldBox("file_name")!.Text = file;
            await Settle();
            await Click(Named<Button>("ApplyNodeButton"), "应用 " + file);
            var runId = await page.RunPlanAsync(page.SelectedPlan);
            if (runId is null) return null;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await page.WaitForRunAsync(runId, cts.Token);
            return runId;
        }

        var okRun = await RunShellAsync("/bin/true");
        page.SelectedNode = page.Nodes.First(n => n.Task.TaskType == "shell");
        await Settle();
        var failRun = await RunShellAsync("/bin/false");
        Check("两次运行已提交", okRun is not null && failRun is not null, $"{okRun} {failRun}");
        if (okRun is null || failRun is null) return;

        // 执行记录页：读取真实运行记录
        vm.Navigate("执行记录");
        await Settle(800);
        var history = vm.History;
        await history.RefreshAsync();
        await Settle();
        var files = Directory.Exists(Global.Absolute(@"User\Pulonia\history"))
            ? Directory.GetDirectories(Global.Absolute(@"User\Pulonia\history")).Sum(d => Directory.GetFiles(d, "*.json").Length) : 0;
        Check("列表包含两次运行", history.Runs.Any(r => r.Item.RunId == okRun) && history.Runs.Any(r => r.Item.RunId == failRun), history.CountText);
        Check("最新的记录在最上面", history.Runs.FirstOrDefault()?.Item.RunId == failRun);
        Check("记录数显示", Named<ListBox>("RunList") is not null && history.CountText == $"共 {history.Runs.Count} 条", $"{history.CountText}，历史文件 {files} 个");

        // 成功的记录：选中后显示状态与节点详情
        var okRow = history.Runs.First(r => r.Item.RunId == okRun);
        Named<ListBox>("RunList")!.SelectedItem = okRow;
        await Settle();
        Check("成功记录状态", okRow.Item.Run.Status == Pulonia.Models.PuloniaTaskRunStatus.Succeeded && okRow.IsSuccess, okRow.Item.StatusText);
        Check("详情显示计划与状态", Named<TextBlock>("RunStatusText")?.Text == okRow.Item.StatusText);
        Check("节点详情", history.SelectedNode?.Item.TaskName == "M51 Shell" && Named<ListBox>("NodeList")?.ItemCount == 1, history.SelectedNode?.Item.TaskName);
        Check("成功记录不可继续", !history.CanResumeRun && ButtonByText("继续剩余") is null);
        Shot("avalonia-history-ok.png");

        // 失败的记录：继续剩余（沿用原快照重新执行失败的任务）
        var failRow = history.Runs.First(r => r.Item.RunId == failRun);
        Named<ListBox>("RunList")!.SelectedItem = failRow;
        await Settle();
        Check("失败记录状态", failRow.IsFailed, failRow.Item.StatusText + " " + failRow.Item.Message);
        Check("默认选中失败的任务", history.SelectedNode?.IsFailed == true, history.SelectedNode?.Item.StatusText);
        Shot("avalonia-history-failed.png");
        var before = history.Runs.Count;
        await Click(ButtonByText("继续剩余"), "继续剩余");
        await WaitFor(() => history.Runs.Count > before ? "ok" : null);
        var resumed = history.Runs.FirstOrDefault();
        Check("继续剩余新增关联运行", resumed is not null && resumed.Item.Run.ResumedFromRunId == failRun, resumed?.Item.Run.ResumedFromRunId);
        if (resumed is not null)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await page.WaitForRunAsync(resumed.Item.RunId, cts.Token);
        }

        // 路线 CD 选项卡
        var cdTab = All<TabItem>().FirstOrDefault(t => t.Header as string == "路线 CD");
        if (cdTab is not null) cdTab.IsSelected = true;
        await Settle();
        Check("路线 CD 选项卡", cdTab is not null && ButtonByText("刷新") is not null);
        if (cdTab is not null) All<TabItem>().First(t => t.Header as string == "运行记录").IsSelected = true;

        vm.Navigate("任务计划");
        await Settle();
        page.SelectedPlan = page.Plans.First(p => p.Plan.Id == failRow.Item.Run.PlanId);
        await page.DeletePlanCommand.ExecuteAsync(null);
    }

    private TextBox? FieldBox(string name)
        => All<StackPanel>().FirstOrDefault(p => p.Tag as string == name && p.IsEffectivelyVisible)?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
}
