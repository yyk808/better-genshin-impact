using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
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
                case "newonedragon-panel": await NewOneDragonPanelAsync(); break;
                case "auto-pages": await AutoPagesAsync(); break;
                case "script-pathing": await ScriptPathingAsync(); break;
                case "newonedragon-more": await NewOneDragonMoreAsync(); break;
                case "taskplan-edit": await TaskPlanEditMenuAsync(); break;
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
                case "newonedragon": await NewOneDragonAsync(); break;
                case "macro": await MacroAsync(); break;
                case "music": await MusicAsync(); break;
                case "guide": await GuideAsync(); break;
                case "plugins": await PluginsAsync(); break;
                case "onedragon-layout": await OneDragonLayoutAsync(); break;
                case "onedragon-domain": await OneDragonDomainAsync(); break;
                case "onedragon-more": await OneDragonMoreAsync(); break;
                case "onedragon-edit": await OneDragonEditAsync(); break;
                case "home-banner": await HomeBannerAsync(); break;
                case "key-bindings": await KeyBindingsAsync(); break;
                case "settings-more": await SettingsMoreAsync(); break;
                case "settings-general": await SettingsGeneralAsync(); break;
                case "trigger-more": await TriggerMoreAsync(); break;
                case "solo-params": await SoloParamsAsync(); break;
                case "solo-boss": await SoloBossAsync(); break;
                case "script-more": await ScriptMoreAsync(); break;
                case "accounts": await AccountRecordsAsync(); break;
                case "accounts-io": await AccountRecordsIoAsync(); break;
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
    private static async Task<bool> Until(Func<bool> condition, int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return true;
            await Task.Delay(50);
        }

        return condition();
    }

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
        Check("不再显示“设置窗口尚未迁移”旧提示", Named<TextBlock>("QuestVoiceSettingsHint") is null);
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

    /// <summary>各账户全部路线完成记录</summary>
    private async Task<List<Pulonia.Models.PuloniaRouteCompletion>> AllCompletionsAsync()
    {
        var result = new List<Pulonia.Models.PuloniaRouteCompletion>();
        if (vm.Host?.Services.GetService(typeof(Pulonia.Services.IPuloniaAccountService)) is not Pulonia.Services.IPuloniaAccountService accounts) return result;
        foreach (var account in (await accounts.LoadAccountsAsync()).Accounts)
            result.AddRange((await accounts.GetRouteCompletionIndexAsync(account.Id)).Completions);
        return result;
    }

    /// <summary>为账户（为空时为本实例当前账户，没有时为第一个账户）写一条一小时前完成的自测路线记录，建出路线文件并刷新任务计划页</summary>
    private async Task SeedCompletionAsync(string? accountId, string path)
    {
        if (vm.Host?.Services.GetService(typeof(Pulonia.Services.IPuloniaAccountService)) is not Pulonia.Services.IPuloniaAccountService accounts
            || vm.Host?.Services.GetService(typeof(Pulonia.Services.PuloniaTaskStore)) is not Pulonia.Services.PuloniaTaskStore store) return;
        var list = (await accounts.LoadAccountsAsync()).Accounts;
        if (accountId is null)
        {
            var current = await accounts.GetCurrentAccountIdAsync(accounts.CurrentInstanceKey);
            accountId = current is not null && list.Any(a => a.Id == current) ? current : list.FirstOrDefault()?.Id;
        }
        if (accountId is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{\"info\":{\"name\":\"M44 自测路线\"},\"positions\":[]}");
        var route = Pulonia.Services.PuloniaRouteCooldown.GetFileIdentity(path, null);
        await store.RemoveRouteCompletionsAsync(accountId, [route]);
        await store.PutRouteCompletionAsync(accountId, new Pulonia.Models.PuloniaRouteCompletion
        {
            Route = route, CompletedAt = DateTimeOffset.Now.AddHours(-1), RunId = "run_m59selftest_" + Guid.NewGuid().ToString("N")[..8],
            TaskAddress = "m59-selftest", Attempt = 1, Instance = accounts.CurrentInstanceKey,
        });
        // 直接写存储不会触发账户进度事件：手动刷新任务计划页的完成记录
        await vm.TaskPlan.RefreshCooldownsCommand.ExecuteAsync(null);
    }

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
        var expected = tasks is null ? -1 : (await AllCompletionsAsync()).Count;
        Check("路线 CD 记录与各账户完成记录一致", page.Cooldowns.Count == expected, $"{page.Cooldowns.Count}/{expected}");
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
            var left = (await AllCompletionsAsync()).Count(c => c.Route.Contains("M42自测路线"));
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
            await SeedCompletionAsync(null, route);
            var ledger = await AllCompletionsAsync();
            Check("预置的路线记录已加载", ledger.Any(c => c.Route.Contains("m44-cd/示例")), string.Join(",", ledger.Select(c => c.Route)));
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
            editor.SelectedCategory = editor.Categories.First(c => c.Id == Pulonia.Services.PuloniaSettingCatalog.RouteCategory);
            await Settle(300);
            var profileCd = editor.Row(Pulonia.Services.PuloniaSettingCatalog.RouteCooldownKey)!;
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
            editor.SelectedCategory = editor.Categories.First(c => c.Id == Pulonia.Services.PuloniaSettingCatalog.RouteCategory);
            var cdRow = editor.Row(Pulonia.Services.PuloniaSettingCatalog.RouteCooldownKey)!;
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
            var left = (await AllCompletionsAsync()).Count(c => c.Route.Contains("m44-cd/示例"));
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
            // 默认账户的完成记录：单条路线、目录下与子目录下各一条（原由外部脚本写入账本）
            await SeedCompletionAsync(null, "/tmp/m44-cd/示例路线.json");
            await SeedCompletionAsync(null, "/tmp/m44-cd/目录/甲.json");
            await SeedCompletionAsync(null, "/tmp/m44-cd/目录/子目录/乙.json");
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
                && editor.Categories.Any(c => c.Id == Pulonia.Services.PuloniaSettingCatalog.RouteCategory),
                editor is null ? page.StatusMessage : string.Join(",", editor.Categories.Select(c => c.Id)));
            if (editor is null) return;
            editor.SelectedCategory = editor.Categories.First(c => c.Id == Pulonia.Services.PuloniaSettingCatalog.RouteCategory);
            var cd = editor.Row(Pulonia.Services.PuloniaSettingCatalog.RouteCooldownKey)!;
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
            editor.SelectedCategory = editor.Categories.First(c => c.Id == Pulonia.Services.PuloniaSettingCatalog.RouteCategory);
            cd = editor.Row(Pulonia.Services.PuloniaSettingCatalog.RouteCooldownKey)!;
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

            // 计划指定账户：CD 与完成记录按该账户（上游统筹设计 5.6）
            var accounts = await store.LoadAccountSettingsAsync();
            accounts.Accounts.RemoveAll(a => a.Name == "M44 测试账号");
            var account = new Pulonia.Models.PuloniaGenshinAccountProfile { Name = "M44 测试账号", Uid = "100000044" };
            accounts.Accounts.Add(account);
            await store.SaveAccountSettingsAsync(accounts);
            await SeedCompletionAsync(account.Id, "/tmp/m44-cd/账号路线.json");
            var dragon = await store.SavePlanAsync(new Pulonia.Models.PuloniaTaskPlan
            {
                Name = "M44 一条龙自测", Purpose = Pulonia.Models.PuloniaTaskPlanPurpose.OneDragon,
                AccountId = account.Id,
                RootTask = new Pulonia.Models.PuloniaTask
                {
                    Name = "M44 一条龙自测", TaskType = "group",
                    Settings = new Newtonsoft.Json.Linq.JObject { [Pulonia.Services.PuloniaSettingCatalog.RouteCooldownKey] = Newtonsoft.Json.Linq.JObject.Parse("{\"mode\":\"rolling\",\"hours\":48}") },
                    Children = [new Pulonia.Models.PuloniaTask { Name = "M44 账号路线", TaskType = "pathing", Path = "/tmp/m44-cd/账号路线.json" },
                        new Pulonia.Models.PuloniaTask { Name = "M44 默认身份路线", TaskType = "pathing", Path = "/tmp/m44-cd/示例路线.json" }]
                }
            });
            dragonId = dragon.Id;
            dragonRevision = dragon.Revision;
            page.CooldownsForPlanOnly = false;
            await Settle(1000);
            Check("一条龙按指定账户判定", page.GetCooldownScope(dragon) == account.Id, page.GetCooldownScope(dragon));
            var accountLeaf = page.DescribeCooldown([dragon.RootTask, dragon.RootTask.Children[0]]);
            var defaultLeaf = page.DescribeCooldown([dragon.RootTask, dragon.RootTask.Children[1]]);
            Check("账户路线按该账户记录 CD 中", accountLeaf.Text?.StartsWith("CD 中") == true && accountLeaf.Tip?.Contains("M44 测试账号") == true,
                $"{accountLeaf.Text} | {accountLeaf.Tip}");
            Check("默认身份记录不影响该账号", defaultLeaf.Text is null, defaultLeaf.Text ?? "无标记");
            var scoped = page.Cooldowns.FirstOrDefault(c => c.Route.Contains("账号路线"));
            Check("总览显示账户", scoped?.Scope == "CD 按账户：M44 测试账号", scoped?.Scope ?? "无");
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
        // 假游戏每 4 秒切换小地图；初始点的截图时刻未知（与切换相位有关，固定等待 4.5 秒可能恰好落回同一画面），
        // 所以等画面切换后立即添加一个途经点，再等下一次切换添加第二个：相邻两个途经点必定来自不同画面
        OpenCvSharp.Mat Frame()
        {
            using var region = BetterGenshinImpact.GameTask.Common.GameRuntimeExtensions.CaptureToRectArea(runtime);
            return region.SrcMat.Clone();
        }
        async Task<bool> WaitFrameSwitch()
        {
            using var reference = await Task.Run(Frame);
            for (var i = 0; i < 60; i++)
            {
                await Task.Delay(150);
                using var now = await Task.Run(Frame);
                using var diff = new OpenCvSharp.Mat();
                OpenCvSharp.Cv2.Absdiff(reference, now, diff);
                using var gray = diff.Channels() == 1 ? diff.Clone() : diff.CvtColor(diff.Channels() == 4 ? OpenCvSharp.ColorConversionCodes.BGRA2GRAY : OpenCvSharp.ColorConversionCodes.BGR2GRAY);
                if (OpenCvSharp.Cv2.CountNonZero(gray.Threshold(30, 255, OpenCvSharp.ThresholdTypes.Binary)) > 2000) return true;
            }
            return false;
        }
        for (var n = 2; n <= 3; n++)
        {
            Check($"小地图画面切换（第 {n - 1} 次）", await WaitFrameSwitch());
            await Task.Delay(300);
            Check($"xdotool 添加路径点（第 {n - 1} 个）", Press("ctrl+shift+F12"));
            for (var i = 0; i < 60 && settings.PathRecordCount < n; i++) await Task.Delay(200);
        }
        Check("添加途经点", settings.PathRecordCount == 3, settings.PathRecordCount.ToString());
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
            var p2 = positions.Count > 2 ? positions[2] : null;
            var distance = p1 is null || p2 is null ? 0 : Math.Sqrt(Math.Pow((double)p1["x"]! - (double)p2["x"]!, 2) + Math.Pow((double)p1["y"]! - (double)p2["y"]!, 2));
            Check("路线含传送点与途经点", positions.Count == 3 && (string?)p0?["type"] == "teleport" && (string?)p1?["type"] == "path" && (string?)p2?["type"] == "path",
                route.ToString(Newtonsoft.Json.Formatting.None));
            // 场景小地图取自精细地图的已知位置：(4618, 3594) 与 (4538, 3534)
            bool Known(Newtonsoft.Json.Linq.JToken? p) => p is not null
                && new[] { (4618d, 3594d), (4538d, 3534d) }.Any(k => Math.Abs((double)p["x"]! - k.Item1) < 3 && Math.Abs((double)p["y"]! - k.Item2) < 3);
            Check("坐标与场景小地图位置一致", Known(p0) && Known(p1) && Known(p2), $"{p0?["x"]},{p0?["y"]} / {p1?["x"]},{p1?["y"]} / {p2?["x"]},{p2?["y"]}");
            Check("两点位置随小地图变化", distance > 10, $"相邻途经点距离 {distance:0.0}");
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
    /// <summary>
    /// 新一条龙页（对应 WPF PuloniaOneDragonPage）：默认配置与清单、新建 / 重命名 / 删除配置、启用开关、
    /// 在选中行之后添加任务、引用计划、排序、运行（只启用一个 Shell 任务）后的状态点与底部状态、触发设置与执行记录入口
    /// </summary>
    private async Task NewOneDragonAsync()
    {
        const string configName = "M51 一条龙自测";
        vm.Navigate("新一条龙");
        var od = vm.NewOneDragon;
        var page = od.Plans;
        await WaitFor(() => page.Plans.Count > 0 ? page.Plans : null);
        await Settle(800);
        foreach (var old in page.Plans.Where(p => p.Name == configName).ToList())
        {
            page.SelectedPlan = old;
            await page.DeletePlanCommand.ExecuteAsync(null);
        }

        Check("至少有一份一条龙配置", page.Plans.Count > 0 && page.Plans.All(p => p.Plan.Purpose == Pulonia.Models.PuloniaTaskPlanPurpose.OneDragon),
            string.Join(",", page.Plans.Select(p => p.Name)));
        var generalIds = vm.TaskPlan.Plans.Select(p => p.Plan.Id).ToHashSet();
        Check("一条龙配置不出现在任务计划页", page.Plans.All(p => !generalIds.Contains(p.Plan.Id)));

        // 新建：默认清单为注册表声明了一条龙顺序的独立任务（Core PuloniaOneDragonDefaults，与 WPF 相同）
        var before = page.Plans.Count;
        await Click(Named<Button>("NewConfigurationButton"), "新建配置");
        await Settle(500);
        var expected = Pulonia.Models.PuloniaOneDragonDefaults.CreatePlan("x", page.AddOptions.Select(o => o.Definition)).RootTask.Children.Select(c => c.Name).ToList();
        Check("新建配置并选中", page.Plans.Count == before + 1 && page.SelectedPlan == page.Plans.Last() && page.SelectedPlan.Name.StartsWith("新一条龙配置"),
            page.SelectedPlan?.Name);
        Check("默认清单", expected.Count > 0 && page.Nodes.Select(n => n.Name).SequenceEqual(expected), string.Join(",", page.Nodes.Select(n => n.Name)));
        var list = Named<ListBox>("OneDragonTaskList");
        Check("清单为平面列表并显示启用开关", list?.ItemCount == expected.Count
                                     && list.GetVisualDescendants().OfType<ToggleSwitch>().Count(t => t.IsEffectivelyVisible) == expected.Count);
        Check("默认选中第一项并显示设置", page.SelectedNode == page.Nodes.FirstOrDefault() && Named<TextBox>("NodeNameBox")?.Text == expected[0],
            Named<TextBox>("NodeNameBox")?.Text);

        // 重命名（输入框对话框）
        var renameTask = page.RenamePlanCommand.ExecuteAsync(null);
        var dialog = await WaitFor(() => Services.FluentMessageBox.Current);
        var box = dialog?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Name == "PromptTextBox")
                  ?? (dialog?.Content as Panel)?.Children.OfType<TextBox>().FirstOrDefault();
        Check("重命名弹出输入框", box is not null && box.Text == page.SelectedPlan?.Name, box?.Text);
        if (box is not null) box.Text = configName;
        dialog?.Hide(FluentAvalonia.UI.Controls.FAContentDialogResult.Primary);
        await renameTask;
        await Settle();
        var store = Path.Combine(Global.Absolute(@"User\Pulonia"), "plans");
        string SavedJson() => page.SelectedPlan is { } p && File.Exists(Path.Combine(store, p.Plan.Id + ".json")) ? File.ReadAllText(Path.Combine(store, p.Plan.Id + ".json")) : "";
        Check("重命名并保存", page.SelectedPlan?.Name == configName && SavedJson().Contains(configName), page.SelectedPlan?.Name + " " + SavedJson().Length);

        await page.ReloadAsync();
        await vm.TaskPlan.ReloadAsync();
        await Settle();
        Check("重新读取后仍是一条龙配置", page.SelectedPlan?.Name == configName && page.SelectedPlan.Plan.Purpose == Pulonia.Models.PuloniaTaskPlanPurpose.OneDragon
                                      && vm.TaskPlan.Plans.All(p => p.Name != configName), page.SelectedPlan?.Name);

        // 启用开关：关闭第二项，保存后选中项不变
        var second = page.Nodes[1];
        page.SelectedNode = page.Nodes[0];
        second.IsEnabled = false;
        await Settle(500);
        Check("关闭启用并保存", page.SelectedPlan!.Plan.RootTask.Children[1].IsEnabled == false && page.Nodes[1].RowOpacity < 1, page.StatusMessage);
        Check("切换开关不改变选中项", page.SelectedNode?.Task.Id == page.Nodes[0].Task.Id);

        // 添加任务：插在选中行之后
        // 打开“…”菜单（菜单项的命令绑定在弹出后才生效）
        var moreButton = Named<DropDownButton>("MoreButton");
        moreButton?.Flyout?.ShowAt(moreButton);
        await Settle(300);
        var menu = (moreButton?.Flyout as MenuFlyout)?.Items.OfType<MenuItem>().ToList() ?? [];
        Check("更多菜单", menu.Select(m => m.Header as string).SequenceEqual(["添加任务…", "上移", "下移", "删除选中任务", "复制选中任务", "粘贴到选中任务之后", "撤销最近编辑", "预览本次执行", "定时与热键", "执行记录", "配置共享设置…", "设置方案…", "账号管理（依赖 Windows 版账号切换，Linux 版暂不可用）", "导入旧配置（Linux 版尚未迁移）", "查看导入报告（随导入旧配置迁移，Linux 版暂不可用）", "重新加载"]),
            string.Join(",", menu.Select(m => m.Header)));
        menu.FirstOrDefault()?.Command?.Execute(null);
        moreButton?.Flyout?.Hide();
        await Settle();
        Check("“添加任务…”展开添加面板", od.IsAddPanelOpen && Named<Button>("AddTaskButton") is not null);
        await Settle();
        page.SelectedAddOption = page.AddOptions.First(o => o.Definition.TaskType == "shell");
        await Settle();
        await Click(Named<Button>("AddTaskButton"), "添加 Shell");
        Check("新任务插在选中行之后并被选中", page.Nodes.Count == expected.Count + 1 && page.Nodes[1].Task.TaskType == "shell" && page.SelectedNode == page.Nodes[1],
            string.Join(",", page.Nodes.Select(n => n.Name)));

        // 引用已有任务计划
        generalIds = vm.TaskPlan.Plans.Select(p => p.Plan.Id).ToHashSet();
        var target = page.ReferencePlans.FirstOrDefault();
        Check("可引用的任务计划来自任务计划页", target is not null && generalIds.Contains(target.Plan.Id), target?.Name);
        if (target is not null)
        {
            page.ReferenceTarget = target;
            await Click(Named<Button>("AddPlanReferenceButton"), "引用");
            var reference = page.Nodes.FirstOrDefault(n => n.Task.Source is { Kind: "plan" } src && src.PlanId == target.Plan.Id);
            Check("引用计划行", reference is not null && page.Nodes.IndexOf(reference) == 2 && reference.Name == target.Name, reference?.Name);
            // 排序与删除
            await page.MoveTaskCommand.ExecuteAsync("up");
            await Settle();
            Check("上移", page.Nodes.IndexOf(page.SelectedNode!) == 1 && page.SelectedNode!.Task.Source is not null);
            await page.RemoveTaskCommand.ExecuteAsync(null);
            await Settle();
            Check("删除引用", page.Nodes.All(n => n.Task.Source is null) && page.Nodes.Count == expected.Count + 1);
        }

        // 运行：只启用 Shell（/bin/true），其余关闭
        var shell = page.Nodes.First(n => n.Task.TaskType == "shell");
        // 每次保存后行会按新的计划对象重建，逐个取当前行
        for (var i = 0; i < 20 && page.Nodes.FirstOrDefault(n => n.Task.TaskType != "shell" && n.IsEnabled) is { } row; i++)
        {
            row.IsEnabled = false;
            await Settle(300);
        }

        Check("只启用 Shell", page.SelectedPlan!.Plan.RootTask.Children.Count(c => c.IsEnabled) == 1, page.StatusMessage);

        page.SelectedNode = page.Nodes.First(n => n.Task.TaskType == "shell");
        await Settle();
        Named<TextBox>("NodeNameBox")!.Text = "M51 一条龙 Shell";
        FieldBox("file_name")!.Text = "/bin/true";
        await Settle();
        await Click(Named<Button>("ApplyNodeButton"), "应用");
        Check("运行前状态", Named<TextBlock>("ExecutionStatusText")?.Text == "尚未执行", Named<TextBlock>("ExecutionStatusText")?.Text);
        var runId = await page.RunPlanAsync(null);
        if (runId is not null)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await page.WaitForRunAsync(runId, cts.Token);
        }

        await Settle(1200);
        shell = page.Nodes.First(n => n.Task.TaskType == "shell");
        var status = Named<TextBlock>("ExecutionStatusText")?.Text ?? "";
        Check("运行完成后底部状态", runId is not null && status.StartsWith("已完成"), status);
        Check("Shell 行状态为已完成", shell.StatusKind == Pulonia.Models.PuloniaTaskSubtreeStatus.Completed && shell.StatusText == "已完成", shell.StatusText);
        Check("关闭的任务未执行", page.Nodes.Where(n => n != shell).All(n => n.StatusKind != Pulonia.Models.PuloniaTaskSubtreeStatus.Completed),
            string.Join(",", page.Nodes.Select(n => n.StatusText)));
        Check("运行结束后停止按钮不可用", Named<Button>("StopOneDragonButton")?.IsEffectivelyEnabled == false);
        Shot("avalonia-newonedragon-run.png");

        // 触发设置：右栏切到触发方式
        od.ShowTriggersCommand.Execute(null);
        await Settle();
        Check("触发设置", od.RightTab == 1 && await WaitFor(() => Named<Button>("AddScheduleTriggerButton")) is not null);
        Shot("avalonia-newonedragon-triggers.png");
        od.RightTab = 0;

        // 执行记录入口：转到执行记录页并能看到本次运行
        od.ShowHistoryCommand.Execute(null);
        await Settle(800);
        await vm.History.RefreshAsync();
        Check("执行记录入口", vm.SelectedNav?.Title == "执行记录" && vm.History.Runs.Any(r => r.Item.RunId == runId), vm.SelectedNav?.Title);

        // 删除测试配置
        vm.Navigate("新一条龙");
        await Settle(500);
        page.SelectedPlan = page.Plans.First(p => p.Name == configName);
        var id = page.SelectedPlan.Plan.Id;
        await Click(Named<Button>("DeleteConfigurationButton"), "删除配置");
        Check("删除配置", page.Plans.All(p => p.Plan.Id != id) && !File.Exists(Path.Combine(store, id + ".json")) && page.SelectedPlan is not null,
            page.SelectedPlan?.Name);
    }

    /// <summary>
    /// 狛荷屋(test) 第 3 步：导出（临时文件替换）、清除（取消/确认）、导入预览对话框（改目标 UID）、删除 UID、两次重置确认；
    /// 所有确认都是 FAContentDialog，文字来自 Core（AccountRecordsAccounts / AccountRecordsImport）
    /// </summary>
    private async Task AccountRecordsIoAsync()
    {
        var page = vm.AccountRecords;
        var root = Global.Absolute(@"User\AccountRecords");
        var createdRoot = !Directory.Exists(root);
        const string uidA = "100000053", uidB = "1000000053";
        const string exportDir = "/tmp/m52-accounts-export";
        if (Directory.Exists(exportDir)) Directory.Delete(exportDir, true);
        Directory.CreateDirectory(exportDir);
        var store = new BetterGenshinImpact.Service.AccountRecords.InventoryStore(new BetterGenshinImpact.Service.AccountRecords.InventoryCatalog(), root);
        FluentAvalonia.UI.Controls.FAContentDialog? last = null;
        async Task<FluentAvalonia.UI.Controls.FAContentDialog?> NextDialog()
        {
            var d = await WaitFor(() => Services.FluentMessageBox.Current is { } c && c != last ? c : null);
            last = d;
            await Settle(200);
            return d;
        }
        string DialogText(FluentAvalonia.UI.Controls.FAContentDialog? d) =>
            d is null ? "" : string.Join("\n", (d.Content as Control)?.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text) ?? []);
        void Answer(FluentAvalonia.UI.Controls.FAContentDialog? d, bool yes) =>
            d?.Hide(yes ? FluentAvalonia.UI.Controls.FAContentDialogResult.Primary : FluentAvalonia.UI.Controls.FAContentDialogResult.None);
        try
        {
            await store.AddUidAsync(uidA);
            var now = DateTimeOffset.Now.ToString("O");
            await store.ReportAsync(uidA, new BetterGenshinImpact.Service.AccountRecords.Model.AccountRecordSource { Reference = "m52/uitest-io", DisplayName = "M52 导入导出" },
            [
                new BetterGenshinImpact.Service.AccountRecords.Model.InventoryReport { Name = "摩拉", Quantity = 4321, RecordedAt = now },
                new BetterGenshinImpact.Service.AccountRecords.Model.InventoryReport { Name = "原石", Quantity = 160, RecordedAt = now }
            ]);
            vm.Navigate("狛荷屋(test)");
            await Settle(800);
            page.SelectedUid = uidA;
            await WaitFor(() => page.Rows.Count == 2 ? (object?)true : null);
            Check("账号操作菜单", Named<DropDownButton>("AccountMenuButton") is not null);

            // 导出
            string? suggested = null;
            page.PickExportPath = name => { suggested = name; return Task.FromResult<string?>(Path.Combine(exportDir, name)); };
            var export = page.ExportInventoryCommand.ExecuteAsync(null);
            var done = await NextDialog();
            Check("导出完成提示（Core 文本）", DialogText(done).Contains(BetterGenshinImpact.Service.AccountRecords.AccountRecordsAccounts.ExportDoneText), DialogText(done));
            Answer(done, true);
            await export;
            var file = suggested is null ? null : Path.Combine(exportDir, suggested);
            Check("导出文件（默认文件名、无临时文件残留）", file is not null && suggested!.StartsWith($"AccountRecords-{uidA}-") && File.Exists(file)
                && File.ReadAllText(file).Contains("摩拉") && Directory.GetFiles(exportDir).Length == 1, suggested);

            // 清空库存：先取消再确认
            var clear = page.ClearAccountCommand.ExecuteAsync("inventory");
            var confirm = await NextDialog();
            Check("清空确认（Core 文本）", DialogText(confirm).Contains(BetterGenshinImpact.Service.AccountRecords.AccountRecordsAccounts.ClearConfirmText("inventory")), DialogText(confirm));
            Answer(confirm, false);
            await clear;
            await Settle(300);
            Check("取消后库存保留", page.Rows.Count == 2);
            clear = page.ClearAccountCommand.ExecuteAsync("inventory");
            Answer(await NextDialog(), true);
            await clear;
            await WaitFor(() => page.Rows.Count == 0 ? (object?)true : null);
            Check("确认后库存清空、UID 保留", page.Rows.Count == 0 && page.UidOptions.Contains(uidA) && page.SelectedUid == uidA);

            // 导入到新 UID
            page.PickImportPath = () => Task.FromResult(file);
            var import = page.OpenImportCommand.ExecuteAsync(null);
            var preview = await NextDialog();
            var uidBox = (preview?.Content as Control)?.GetLogicalDescendants().OfType<TextBox>().FirstOrDefault(t => t.Name == "ImportUidBox");
            Check("导入预览对话框（文件 UID 与摘要）", uidBox?.Text == uidA && DialogText(preview).Contains("库存 2 项"), DialogText(preview));
            if (uidBox is not null) uidBox.Text = uidB;
            Answer(preview, true);
            var summary = await NextDialog();
            Check("导入完成摘要", DialogText(summary).Contains("接受 2 项"), DialogText(summary));
            Answer(summary, true);
            await import;
            await Settle(300);
            Check("导入后切换到目标 UID 并显示库存", page.SelectedUid == uidB && page.UidOptions.Contains(uidB) && page.Rows.Count == 2
                && page.Rows.Any(r => r.Name == "摩拉" && r.QuantityText == "4321"), $"{page.SelectedUid} {page.Rows.Count}");
            Shot("avalonia-accounts-import.png");

            // 删除 UID
            var delete = page.ClearAccountCommand.ExecuteAsync("uid");
            var del = await NextDialog();
            Check("删除 UID 确认（Core 文本）", DialogText(del).Contains(BetterGenshinImpact.Service.AccountRecords.AccountRecordsAccounts.ClearConfirmText("uid")));
            Answer(del, true);
            await delete;
            await Settle(300);
            Check("删除后 UID 移除", !page.UidOptions.Contains(uidB) && !Directory.Exists(Path.Combine(root, uidB)), string.Join(",", page.UidOptions));

            // 两次重置确认：只在测试前不存在账号记录目录时执行，避免覆盖已有数据
            if (createdRoot)
            {
                var reset = page.ResetToDefaultsCommand.ExecuteAsync(null);
                var first = await NextDialog();
                Check("重置第 1 次确认", first?.Title as string == BetterGenshinImpact.Service.AccountRecords.AccountRecordsAccounts.ResetConfirm1.Title, first?.Title?.ToString());
                Answer(first, false);
                await reset;
                Check("第 1 次取消不重置", page.UidOptions.Contains(uidA));
                reset = page.ResetToDefaultsCommand.ExecuteAsync(null);
                Answer(await NextDialog(), true);
                var second = await NextDialog();
                Check("重置第 2 次确认", second?.Title as string == BetterGenshinImpact.Service.AccountRecords.AccountRecordsAccounts.ResetConfirm2.Title, second?.Title?.ToString());
                Answer(second, true);
                var resetDone = await NextDialog();
                Check("重置完成提示", DialogText(resetDone).Contains(BetterGenshinImpact.Service.AccountRecords.AccountRecordsAccounts.ResetDoneText));
                Answer(resetDone, true);
                await reset;
                Check("重置后 UID 列表为空", page.UidOptions.Count == 0 && page.SelectedUid is null && page.Rows.Count == 0);
            }
            else
            {
                log.Information("狛荷屋：已有账号记录目录，跳过重置检查");
            }
        }
        finally
        {
            foreach (var uid in new[] { uidA, uidB })
                try { if (Directory.Exists(Path.Combine(root, uid))) await store.ClearAsync(uid, false, true); } catch (Exception e) { log.Warning("清理测试 UID {Uid} 失败：{Message}", uid, e.Message); }
            if (createdRoot)
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
                foreach (var dir in Directory.GetDirectories(Path.GetDirectoryName(root)!, "AccountRecords.backup-*")) Directory.Delete(dir, true);
            }
            if (Directory.Exists(exportDir)) Directory.Delete(exportDir, true);
        }
    }

    /// <summary>
    /// 狛荷屋(test)（对应 WPF AccountRecordsPage）：UID 校验与新增（Core AccountRecordsAccounts / InventoryStore）、只读库存表（Core InventoryRowBuilder）、搜索、UID 切换互不串号
    /// </summary>
    private async Task AccountRecordsAsync()
    {
        var page = vm.AccountRecords;
        var root = Global.Absolute(@"User\AccountRecords");
        var createdRoot = !Directory.Exists(root);
        const string uidA = "100000052", uidB = "1000000052";
        var store = new BetterGenshinImpact.Service.AccountRecords.InventoryStore(new BetterGenshinImpact.Service.AccountRecords.InventoryCatalog(), root);
        try
        {
            await store.AddUidAsync(uidA);
            var now = DateTimeOffset.Now.ToString("O");
            await store.ReportAsync(uidA, new BetterGenshinImpact.Service.AccountRecords.Model.AccountRecordSource { Reference = "m52/uitest", DisplayName = "M52 自测" },
            [
                new BetterGenshinImpact.Service.AccountRecords.Model.InventoryReport { Name = "摩拉", Quantity = 1234567, RecordedAt = now },
                new BetterGenshinImpact.Service.AccountRecords.Model.InventoryReport { Name = "原石", Quantity = 1600, RecordedAt = now },
                new BetterGenshinImpact.Service.AccountRecords.Model.InventoryReport { Name = "大英雄的经验", Quantity = 88, RecordedAt = now }
            ]);
            vm.Navigate("狛荷屋(test)");
            await Settle(800);
            Check("UID 列表含测试账号", page.UidOptions.Contains(uidA), string.Join(",", page.UidOptions));
            page.SelectedUid = uidA;
            await WaitFor(() => page.Rows.Count >= 3 ? (object?)page.Rows : null);
            await Settle();
            var list = Named<ItemsControl>("InventoryList");
            Check("库存表显示上报的 3 项", page.Rows.Count == 3 && list?.ItemCount == 3, string.Join(",", page.Rows.Select(r => $"{r.Name}={r.QuantityText}")));
            var mora = page.Rows.FirstOrDefault(r => r.Name == "摩拉");
            Check("行数据：数量/来源/时间", mora is { QuantityText: "1234567", SourceName: "M52 自测" } && mora.TimeText != "未记录", $"{mora?.QuantityText} {mora?.SourceName} {mora?.TimeText}");
            Check("空库存提示隐藏", page.HasRows && Named<TextBlock>("EmptyInventoryText") is null);
            Shot("avalonia-accounts.png");
            page.SearchText = "原石";
            await Settle();
            Check("搜索过滤", page.Rows.Count == 1 && page.Rows[0].Name == "原石", page.Rows.Count.ToString());
            page.SearchText = "";
            await Settle();

            await Click(Named<Button>("AddUidButton")!, "新增 UID");
            Check("新增区展开", Named<Border>("AddUidPanel")?.IsVisible == true);
            page.PendingUid = "012345678";
            await page.AddUid();
            Check("首位为 0 被拒", page.UidValidationMessage == BetterGenshinImpact.Service.AccountRecords.AccountRecordsAccounts.UidValidationMessage && !page.UidOptions.Contains("012345678"), page.UidValidationMessage);
            page.PendingUid = " " + uidB + " ";
            await page.AddUid();
            await Settle(400);
            Check("新增 UID 并切换", page.SelectedUid == uidB && page.UidOptions.Contains(uidB) && Directory.Exists(Path.Combine(root, uidB)) && !page.IsAddingUid, page.SelectedUid);
            await WaitFor(() => page.Rows.Count == 0 ? (object?)true : null);
            Check("新 UID 库存为空且显示提示", page.Rows.Count == 0 && Named<TextBlock>("EmptyInventoryText")?.IsVisible == true);
            Named<ComboBox>("UidBox")!.SelectedItem = uidA;
            await WaitFor(() => page.Rows.Count == 3 ? (object?)true : null);
            Check("UID 切换回来恢复库存", page.SelectedUid == uidA && page.Rows.Count == 3);
        }
        finally
        {
            foreach (var uid in new[] { uidA, uidB })
                try { await store.ClearAsync(uid, false, true); } catch (Exception e) { log.Warning("清理测试 UID {Uid} 失败：{Message}", uid, e.Message); }
            if (createdRoot && Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// 旧一条龙页右栏“自动秘境”（对照 WPF OneDragonFlowPage）：每日开关与每周开关互斥、队伍、秘境来源单选、指定秘境、
    /// 每周表格（勾选培养计划写入提升指南选项、取消写回空）、培养计划刷取设置；每次修改都写回同一份 User/OneDragon 配置文件
    /// </summary>
    private async Task OneDragonDomainAsync()
    {
        vm.Navigate("一条龙");
        await Settle(600);
        await Settle(300);
        var page = vm.OneDragon;
        var config = page.SelectedConfig;
        var ds = page.DomainSettings;
        Check("自动秘境设置已绑定当前配置", config is not null && ds is not null && ReferenceEquals(ds.Config, config));
        if (config is null || ds is null) return;
        BetterGenshinImpact.Core.Config.OneDragonFlowConfig? Saved() => BetterGenshinImpact.Core.Config.OneDragonFlowConfigStore.Load(config.Name);
        var training = BetterGenshinImpact.GameTask.AutoDomain.AutoDomainTask.TrainingGuideOption;
        Check("三张卡片", Named<FluentAvalonia.UI.Controls.FASettingsExpander>("OdDailyDomainCard") is not null
                         && Named<FluentAvalonia.UI.Controls.FASettingsExpander>("OdTrainingCard") is not null
                         && Named<FluentAvalonia.UI.Controls.FASettingsExpander>("OdWeeklyCard") is not null);

        // 每日/每周互斥
        var weekly = Named<ToggleSwitch>("OdWeeklyEnabled");
        var daily = Named<ToggleSwitch>("OdDailyEnabled");
        var wasWeekly = config.WeeklyDomainEnabled;
        weekly!.IsChecked = !wasWeekly;
        await Settle();
        Check("每周开关写回并与每日互斥", config.WeeklyDomainEnabled == !wasWeekly && daily!.IsChecked == wasWeekly && Saved()?.WeeklyDomainEnabled == !wasWeekly);
        weekly.IsChecked = false;
        await Settle();
        foreach (var e in All<FluentAvalonia.UI.Controls.FASettingsExpander>().Where(e => e.Name is "OdDailyDomainCard" or "OdTrainingCard" or "OdWeeklyCard")) e.IsExpanded = true;
        await Settle(400);

        // 每日：队伍、来源、指定秘境
        var party = Named<TextBox>("OdDailyParty");
        party!.Text = "m53队伍";
        await Settle();
        Check("每日队伍写回配置文件", config.PartyName == "m53队伍" && Saved()?.PartyName == "m53队伍", Saved()?.PartyName);
        Named<RadioButton>("OdDailySourceManual")!.IsChecked = true;
        await Settle();
        var domainBox = Named<ComboBox>("OdDailyDomain");
        var domains = DedicatedTaskSettingsChoices.DomainNames;
        Check("指定秘境时显示秘境选择", domainBox is not null && domains.Count > 1);
        if (domainBox is null || domains.Count < 2) return;
        domainBox.SelectedItem = domains[1];
        await Settle();
        Check("选择秘境写回", config.DomainName == domains[1] && Saved()?.DomainName == domains[1], config.DomainName);
        Named<RadioButton>("OdDailySourceTraining")!.IsChecked = true;
        await Settle();
        Check("跟随培养计划写入提升指南选项、隐藏秘境选择", config.DomainName == training && Named<ComboBox>("OdDailyDomain") is null && Saved()?.DomainName == training);
        Named<RadioButton>("OdDailySourceManual")!.IsChecked = true;
        await Settle();
        Check("切回指定秘境恢复上次秘境", config.DomainName == domains[1], config.DomainName);
        var sunday = Named<ComboBox>("OdDailySunday");
        sunday!.SelectedItem = "2";
        await Settle();
        Check("每日奖励序号写回", config.SundayEverySelectedValue == "2" && Saved()?.SundayEverySelectedValue == "2");

        // 培养计划刷取设置（全局 AutoDomainConfig）
        var calc = Named<ToggleSwitch>("OdCalculateRuns");
        var wasCalc = page.Config.AutoDomainConfig.TrainingGuideCalculateRunsEnabled;
        calc!.IsChecked = !wasCalc;
        await Settle();
        Check("自动计算次数写回全局配置", page.Config.AutoDomainConfig.TrainingGuideCalculateRunsEnabled == !wasCalc);
        calc.IsChecked = wasCalc;
        await Settle();

        // 每周表格：周一
        var rows = Named<ItemsControl>("OdWeeklyRows");
        Check("每周表格 8 行（默认 + 周一至周日）", ds.Weekly.Count == 8 && rows?.ItemCount == 8 && ds.Weekly[1].Label == "周一", rows?.ItemCount.ToString());
        if (rows is null) return;
        var monday = ds.Weekly[1];
        var mondayBox = rows.GetVisualDescendants().OfType<CheckBox>().First(c => c.DataContext == monday);
        mondayBox.IsChecked = true;
        await Settle();
        Check("勾选培养计划写入提升指南选项", config.MondayDomainName == training && Saved()?.MondayDomainName == training);
        mondayBox.IsChecked = false;
        await Settle();
        Check("取消勾选写回空秘境", config.MondayDomainName == "" && Saved()?.MondayDomainName == "");
        var mondayDomain = rows.GetVisualDescendants().OfType<ComboBox>().First(c => c.DataContext == monday && c.IsEffectivelyVisible && c.ItemCount > 4);
        mondayDomain.SelectedItem = domains[0];
        await Settle();
        var mondayParty = rows.GetVisualDescendants().OfType<TextBox>().First(c => c.DataContext == monday);
        mondayParty.Text = "周一队";
        await Settle();
        Check("周一秘境与队伍写回", config.MondayDomainName == domains[0] && Saved() is { MondayPartyName: "周一队" } saved && saved.MondayDomainName == domains[0],
            $"{config.MondayDomainName} {config.MondayPartyName}");
        Shot("avalonia-onedragon-domain.png");
    }

    /// <summary>
    /// 旧一条龙页右栏其余卡片（对照 WPF OneDragonFlowPage）：合成树脂、树脂使用、分解圣遗物、奖励识别、自动首领讨伐、地脉花（按星期）、领取奖励、尘歌壶购买；
    /// 一条龙配置项每次修改都写回同一份 User/OneDragon 文件，全局项写入 AllConfig
    /// </summary>
    private async Task OneDragonMoreAsync()
    {
        vm.Navigate("一条龙");
        await Settle(600);
        var page = vm.OneDragon;
        var config = page.SelectedConfig;
        Check("当前配置", config is not null);
        if (config is null) return;
        foreach (var e in All<FluentAvalonia.UI.Controls.FASettingsExpander>().Where(e => e.Name?.StartsWith("Od") == true)) e.IsExpanded = true;
        await Settle(400);
        await Settle(200);
        BetterGenshinImpact.Core.Config.OneDragonFlowConfig? Saved() => BetterGenshinImpact.Core.Config.OneDragonFlowConfigStore.Load(config.Name);
        var cards = new[] { "OdCraftCard", "OdResinCard", "OdSalvageCard", "OdRewardCard", "OdBossCard", "OdLeyLineCard", "OdLeyLineDaysCard", "OdGuildCard", "OdRewardPartyCard", "OdPotCard" };
        var missing = cards.Where(n => Named<FluentAvalonia.UI.Controls.FASettingsExpander>(n) is null).ToList();
        Check("10 张卡片", missing.Count == 0, string.Join(",", missing));

        // 合成树脂
        Named<ComboBox>("OdCraftCountry")!.SelectedItem = "璃月";
        Named<NumericUpDown>("OdMinResin")!.Value = 12;
        await Settle();
        Check("合成台地区与保留数量写回", config.CraftingBenchCountry == "璃月" && config.MinResinToKeep == 12 && Saved() is { CraftingBenchCountry: "璃月", MinResinToKeep: 12 });

        // 树脂使用 / 分解 / 奖励识别（全局 AutoDomainConfig）
        var resinCard = Named<FluentAvalonia.UI.Controls.FASettingsExpander>("OdResinCard");
        Check("树脂使用摘要与设置按钮", !string.IsNullOrEmpty(resinCard?.Description as string) && Named<Button>("OdResinButton")?.Command is not null, resinCard?.Description as string);
        var ad = page.Tasks.Domain.Config;
        var wasSalvage = ad.AutoArtifactSalvage;
        Named<ToggleSwitch>("OdSalvage")!.IsChecked = !wasSalvage;
        await Settle();
        Check("分解圣遗物写入全局配置", ad.AutoArtifactSalvage == !wasSalvage);
        Named<ToggleSwitch>("OdSalvage")!.IsChecked = wasSalvage;
        Named<ComboBox>("OdMaxStar")!.SelectedItem = "3";
        await Settle();
        Check("最高星级写入全局配置", page.Tasks.Domain.Salvage.MaxArtifactStar == "3");
        var wasReward = ad.RewardRecognitionEnabled;
        Named<ToggleSwitch>("OdRewardRecognition")!.IsChecked = !wasReward;
        await Settle();
        Check("秘境奖励识别写入全局配置", ad.RewardRecognitionEnabled == !wasReward);
        Named<ToggleSwitch>("OdRewardRecognition")!.IsChecked = wasReward;

        // 自动首领讨伐
        var bosses = DedicatedTaskSettingsChoices.BossNames;
        Named<ComboBox>("OdBossName")!.SelectedItem = bosses[0];
        Named<TextBox>("OdBossTeam")!.Text = "首领队";
        Named<ToggleSwitch>("OdBossSpecify")!.IsChecked = true;
        await Settle(300);
        Check("首领与队伍写回、开启次数限制显示上限", Saved() is { AutoBossTeamName: "首领队", AutoBossSpecifyRunCount: true } b && b.AutoBossName == bosses[0]
            && Named<NumericUpDown>("OdBossRunCount") is not null);
        Named<NumericUpDown>("OdBossRunCount")!.Value = 4;
        config.AutoBossCompletedRunCount = 7;
        await Settle();
        Check("累计次数显示", Named<TextBlock>("OdBossCompleted")?.Text?.Contains("7") == true, Named<TextBlock>("OdBossCompleted")?.Text);
        await Click(Named<Button>("OdBossReset")!, "清空累计");
        Check("单次上限写回、清空累计", Saved() is { AutoBossRunCount: 4, AutoBossCompletedRunCount: 0 });

        // 地脉花
        Named<ToggleSwitch>("OdLeyLineSkip")!.IsChecked = true;
        Named<NumericUpDown>("OdLeyLineCount")!.Value = 3;
        var rows = page.LeyLineRows;
        Check("地脉花 7 行", rows.Count == 7 && Named<ItemsControl>("OdLeyLineRows")?.ItemCount == 7);
        var list = Named<ItemsControl>("OdLeyLineRows")!;
        list.GetVisualDescendants().OfType<CheckBox>().First(c => c.DataContext == rows[0]).IsChecked = false;
        var combos = list.GetVisualDescendants().OfType<ComboBox>().Where(c => c.DataContext == rows[0]).ToList();
        combos[0].SelectedItem = "藏金之花";
        combos[1].SelectedItem = "璃月";
        await Settle();
        Check("地脉花设置写回（周一不运行、类型、国家）", Saved() is { LeyLineOneDragonMode: true, LeyLineRunCount: 3, LeyLineRunMonday: false, LeyLineMondayType: "藏金之花", LeyLineMondayCountry: "璃月" });

        // 领取奖励
        Named<ComboBox>("OdGuildCountry")!.SelectedItem = "挪德卡莱";
        Named<TextBox>("OdRewardParty")!.Text = "好感队";
        await Settle();
        Check("冒险者协会与好感队写回", Saved() is { AdventurersGuildCountry: "挪德卡莱", DailyRewardPartyName: "好感队" });

        // 尘歌壶
        Named<ComboBox>("OdPotTpType")!.SelectedItem = "尘歌壶道具";
        Named<ComboBox>("OdPotSchedule")!.SelectedItem = "星期三";
        foreach (var item in page.SereniteaPotItems) item.IsSelected = item.Name is "布匹" or "摩拉";
        await Click(Named<Button>("OdPotApply")!, "应用");
        var pot = Saved();
        Check("尘歌壶进壶方式与购买设置写回", pot is { SereniteaPotTpType: "尘歌壶道具" } && string.Join("/", pot.SecretTreasureObjects) == "星期三/布匹/摩拉",
            pot is null ? null : string.Join("/", pot.SecretTreasureObjects));
        Shot("avalonia-onedragon-more.png");
    }

    /// <summary>设置页补项：快捷键筛选框（Core HotkeyFilter）、通知 WebSocket 卡片</summary>
    private async Task SettingsMoreAsync()
    {
        vm.Navigate("快捷键");
        await Settle(600);
        var page = vm.Settings;
        var box = Named<TextBox>("HotkeyFilterBox");
        Check("快捷键筛选框", box is not null && box.Watermark as string == "按功能名称筛选");
        var total = page.Hotkeys.Count;
        var target = page.Hotkeys.First(h => !string.IsNullOrEmpty(h.Group));
        box!.Text = target.Name;
        await Settle();
        var shown = page.Hotkeys.Where(h => h.IsFilterVisible).ToList();
        Check("按功能名筛选只显示匹配项并显示其分组标题", shown.Contains(target) && shown.Count < total && shown.All(h => h.Name.Contains(target.Name) || h.Group.Contains(target.Name))
            && target.ShowGroupHeader, $"{shown.Count}/{total}");
        Shot("avalonia-settings-hotkey-filter.png");
        box.Text = "  " + target.Group.ToUpperInvariant() + " ";
        await Settle();
        Check("按分组名筛选显示整组（忽略首尾空白）", page.Hotkeys.Where(h => h.Group == target.Group).All(h => h.IsFilterVisible));
        box.Text = "不存在的功能xyz";
        await Settle();
        Check("无结果时显示“没有匹配的功能”", page.Hotkeys.All(h => !h.IsFilterVisible) && Named<TextBlock>("HotkeyFilterEmptyText") is not null);
        box.Text = "";
        await Settle();
        Check("清空后全部显示", page.Hotkeys.All(h => h.IsFilterVisible));

        vm.Navigate("通知");
        await Settle(600);
        var nc = page.Config.NotificationConfig;
        var (wasOn, wasEp) = (nc.WebSocketNotificationEnabled, nc.WebSocketEndpoint);
        if (Named<FluentAvalonia.UI.Controls.FASettingsExpander>("WebSocketCard") is { } card) card.IsExpanded = true;
        await Settle();
        Named<ToggleSwitch>("WebSocketSwitch")!.IsChecked = !wasOn;
        Named<TextBox>("WebSocketEndpointBox")!.Text = "ws://127.0.0.1:9/m55";
        await Settle();
        Check("WebSocket 开关与端点写入配置", nc.WebSocketNotificationEnabled == !wasOn && nc.WebSocketEndpoint == "ws://127.0.0.1:9/m55");
        await Click(Named<Button>("WebSocketTestButton"), "测试 WebSocket");
        await Until(() => !string.IsNullOrEmpty(page.WebSocketStatus), 8000);
        Check("测试 WebSocket 显示结果（无服务时为失败信息）", !string.IsNullOrEmpty(page.WebSocketStatus), page.WebSocketStatus);
        Shot("avalonia-settings-notify-websocket.png");
        nc.WebSocketNotificationEnabled = wasOn;
        nc.WebSocketEndpoint = wasEp;
        await Settle();
    }

    /// <summary>调度器“更多功能”：重命名 / 复制组、根据文件夹更新 / 移除、打开所在目录</summary>
    private async Task ScriptMoreAsync()
    {
        vm.Navigate("调度器");
        await Settle(600);
        var page = vm.Auto.Scheduler;
        page.Reload();
        await Settle();
        var dir = Path.Combine(BetterGenshinImpact.GameTask.AutoPathing.PathingPaths.PathJsonPath, "m56测试目录");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "m56-a.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "m56-b.json"), "{}");
        try
        {
            page.NewGroupName = "m56测试组";
            page.AddScriptGroupCommand.Execute(null);
            await Settle();
            var g = page.SelectedScriptGroup!;
            g.AddProject(ScriptGroupProject.BuildPathingProject("m56-a.json", "m56测试目录"));
            g.AddProject(ScriptGroupProject.BuildShellProject("echo m56"));
            await Settle();
            Check("“更多功能”按钮", Named<Button>("GroupMoreButton") is not null);
            page.UpdateByFolderCommand.Execute(null);
            await Settle();
            Check("根据文件夹更新补齐新路线并保留其它项目", g.Projects.Count == 3 && g.Projects.Count(p => p.Type == "Pathing") == 2,
                string.Join(",", g.Projects.Select(p => p.Name)));
            var rowMenu = All<Border>().Select(b => b.ContextMenu).FirstOrDefault(m => m is not null);
            rowMenu?.Open(All<Border>().First(b => b.ContextMenu == rowMenu));
            await Settle(400);
            Check("项目行右键菜单：打开所在目录 / 根据文件夹移除", rowMenu?.Items.OfType<MenuItem>().Select(i => i.Header as string).SequenceEqual(["打开所在目录", "根据文件夹移除"]) == true);
            Shot("avalonia-script-more-rowmenu.png");
            rowMenu?.Close();
            await Settle();
            Shot("avalonia-script-more.png");

            var last = Services.FluentMessageBox.Current;
            var rename = page.RenameScriptGroupCommand.ExecuteAsync(null);
            var dialog = await WaitFor(() => Services.FluentMessageBox.Current is { } c && c != last ? c : null);
            var box = (dialog?.Content as Panel)?.Children.OfType<TextBox>().FirstOrDefault();
            if (box is not null) box.Text = "m56测试组改";
            dialog?.Hide(FluentAvalonia.UI.Controls.FAContentDialogResult.Primary);
            await rename;
            await Settle();
            Check("重命名配置组并移动文件", g.Name == "m56测试组改" && File.Exists(ScriptGroupStore.FilePath("m56测试组改")) && !File.Exists(ScriptGroupStore.FilePath("m56测试组")));

            last = Services.FluentMessageBox.Current;
            var copy = page.CopyScriptGroupCommand.ExecuteAsync(null);
            dialog = await WaitFor(() => Services.FluentMessageBox.Current is { } c && c != last ? c : null);
            box = (dialog?.Content as Panel)?.Children.OfType<TextBox>().FirstOrDefault();
            if (box is not null) box.Text = "m56测试组副本";
            dialog?.Hide(FluentAvalonia.UI.Controls.FAContentDialogResult.Primary);
            await copy;
            await Settle();
            var c2 = page.ScriptGroups.FirstOrDefault(x => x.Name == "m56测试组副本");
            Check("复制组（深拷贝、写文件、选中副本）", c2 is not null && c2 != g && c2.Projects.Count == 3 && File.Exists(ScriptGroupStore.FilePath("m56测试组副本")) && page.SelectedScriptGroup == c2);

            page.SelectedScriptGroup = g;
            await Settle();
            var missing = ScriptGroupProject.BuildPathingProject("x.json", "m56不存在的目录");
            page.OpenProjectFolderCommand.Execute(missing);
            Check("目录不存在时不打开", page.LastOpenedFolder is null);
            // 通过行右键菜单移除（菜单项的 DataContext 是该行项目）
            var pathingRow = All<Border>().First(b => b.ContextMenu is not null && b.DataContext is ScriptGroupProject { Type: "Pathing" });
            var delItem = pathingRow.ContextMenu!.Items.OfType<MenuItem>().Last();
            pathingRow.ContextMenu.Open(pathingRow);
            await Settle(300);
            delItem.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            pathingRow.ContextMenu.Close();
            await Settle();
            Check("根据文件夹移除（右键菜单）", g.Projects.Count == 1 && g.Projects[0].Type == "Shell");
            page.DeleteScriptGroupCommand.Execute(c2);
            page.DeleteScriptGroupCommand.Execute(g);
            await Settle();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>任务计划编辑菜单：复制 / 粘贴、启用全部、撤销、预览本次执行</summary>
    private async Task TaskPlanEditMenuAsync()
    {
        const string planName = "M57 编辑自测";
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
        await page.NewPlanCommand.ExecuteAsync(null);
        await Settle();
        var item = page.SelectedPlan;
        Check("新建自测计划", item?.Name == planName);
        if (item is null || item.Name != planName) return;
        var id = item.Plan.Id;
        try
        {
            Check("工具栏：复制 / 粘贴 / 启用全部 / 撤销 / 预览", new[] { "CopyTaskButton", "PasteTaskButton", "EnableAllButton", "UndoButton", "PreviewRunButton" }.All(n => Named<Button>(n) is not null));
            page.SelectedAddOption = page.AddOptions.First(o => o.Definition.TaskType == "shell");
            await Settle();
            page.SelectedNode = null;
            await page.AddTaskCommand.ExecuteAsync(null);
            page.SelectedNode = null;
            await page.AddGroupCommand.ExecuteAsync(null);
            await Settle();
            var shell = page.Nodes.First(n => n.Task.TaskType == "shell");
            page.SelectedNode = shell;
            page.CopyTaskCommand.Execute(null);
            page.SelectedNode = page.Nodes.First(n => n.Task.TaskType == "group");
            await page.PasteTaskCommand.ExecuteAsync(null);
            await Settle();
            var kids = Children(ReadPlan(id));
            var groupKids = kids.FirstOrDefault(k => k.Value<string>("task_type") == "group")?["children"] as JArray;
            Check("粘贴到选中分组内，是新 ID 的独立副本", groupKids?.Count == 1 && groupKids[0].Value<string>("task_type") == "shell"
                && groupKids[0].Value<string>("id") != kids[0].Value<string>("id"), groupKids?.ToString(Newtonsoft.Json.Formatting.None));

            await page.SetEnabledAsync(page.Nodes.First(n => n.Task.TaskType == "shell"), false);
            page.SelectedNode = null;
            await page.EnableAllCommand.ExecuteAsync(null);
            kids = Children(ReadPlan(id));
            Check("启用全部写入文件", kids[0].Value<bool?>("is_enabled") != false, kids[0].ToString(Newtonsoft.Json.Formatting.None));
            await page.UndoCommand.ExecuteAsync(null);
            kids = Children(ReadPlan(id));
            Check("撤销：恢复为关闭", kids[0].Value<bool?>("is_enabled") == false, kids[0].ToString(Newtonsoft.Json.Formatting.None));
            await page.UndoCommand.ExecuteAsync(null);
            kids = Children(ReadPlan(id));
            Check("再撤销：恢复为启用", kids[0].Value<bool?>("is_enabled") != false);
            await page.SetEnabledAsync(page.Nodes.First(n => n.Task.TaskType == "shell"), false);
            await Settle();

            var last = Services.FluentMessageBox.Current;
            var preview = page.PreviewRunCommand.ExecuteAsync(null);
            var dialog = await WaitFor(() => Services.FluentMessageBox.Current is { } c && c != last ? c : null);
            await Settle(600);
            var rows = page.LastPreviewRows;
            Check("预览本次执行：逐项结果（关闭的一项、执行的一项）", dialog is not null && rows.Count == 2 && rows.Count(r => r.IsDisabled) == 1 && rows.Count(r => r.OutcomeText == "执行") == 1,
                string.Join(" | ", rows.Select(r => $"{r.Name}:{r.OutcomeText}")));
            Check("预览汇总文字", (dialog?.Content as Panel)?.Children.OfType<TextBlock>().FirstOrDefault()?.Text?.StartsWith("预计执行 1 项") == true);
            Shot("avalonia-taskplan-preview.png");
            dialog?.Hide(FluentAvalonia.UI.Controls.FAContentDialogResult.None);
            await preview;
            await Settle();
            Shot("avalonia-taskplan-editmenu.png");
        }
        finally
        {
            page.SelectedPlan = page.Plans.FirstOrDefault(p => p.Plan.Id == id);
            if (page.SelectedPlan is not null) await page.DeletePlanCommand.ExecuteAsync(null);
            await Settle();
            Check("删除自测计划", !File.Exists(Global.Absolute($@"User\Pulonia\plans\{id}.json")));
        }
    }

    /// <summary>新一条龙“更多”菜单与右栏：复制 / 粘贴、撤销、预览、配置共享设置与设置方案页签</summary>
    private async Task NewOneDragonMoreAsync()
    {
        vm.Navigate("新一条龙");
        await Settle(1000);
        var page = vm.NewOneDragon;
        var plans = page.Plans;
        await plans.ReloadAsync();
        await Settle(600);
        if (plans.SelectedPlan is null) { Check("有一条龙配置", false); return; }
        var more = await WaitFor(() => Named<DropDownButton>("MoreButton"));
        var headers = (more?.Flyout as MenuFlyout)?.Items.OfType<MenuItem>().Select(i => i.Header as string ?? "").ToList() ?? [];
        Check("“更多”菜单：复制 / 粘贴 / 撤销 / 预览 / 定时与热键 / 配置共享设置 / 设置方案",
            new[] { "复制选中任务", "粘贴到选中任务之后", "撤销最近编辑", "预览本次执行", "定时与热键", "配置共享设置…", "设置方案…" }.All(headers.Contains), string.Join(",", headers));
        Check("账号管理、导入旧配置置灰并说明", (more?.Flyout as MenuFlyout)?.Items.OfType<MenuItem>().Where(i => (i.Header as string ?? "").StartsWith("账号管理") || (i.Header as string ?? "").StartsWith("导入旧配置")).All(i => !i.IsEnabled) == true);
        more?.Flyout?.ShowAt(more);
        await Settle(400);
        Shot("avalonia-newonedragon-more.png");
        more?.Flyout?.Hide();

        var count = plans.Nodes.Count;
        plans.SelectedNode = plans.Nodes.FirstOrDefault();
        plans.CopyTaskCommand.Execute(null);
        await plans.PasteTaskCommand.ExecuteAsync(null);
        await Settle();
        Check("粘贴到选中任务之后", plans.Nodes.Count == count + 1 && plans.Nodes.Count > 1 && plans.Nodes[1].Name == plans.Nodes[0].Name, $"{count}->{plans.Nodes.Count}");
        await plans.UndoCommand.ExecuteAsync(null);
        await Settle();
        Check("撤销最近编辑", plans.Nodes.Count == count, $"{plans.Nodes.Count}");

        page.ShowPlanSettingsCommand.Execute(null);
        await Settle(800);
        Check("配置共享设置：切到“共享设置”页签并打开计划级共享设置", page.RightTab == 2 && plans.SharedEditor is not null && Named<Border>("SharedSettingsPanel") is not null);
        Check("设置方案管理在同一页签", Named<ListBox>("ProfileList") is not null && Named<Button>("CreateProfileButton") is not null);
        Shot("avalonia-newonedragon-shared.png");
        page.RightTab = 0;
        await Settle();

        var last = Services.FluentMessageBox.Current;
        var preview = plans.PreviewRunCommand.ExecuteAsync(null);
        var dialog = await WaitFor(() => Services.FluentMessageBox.Current is { } c && c != last ? c : null);
        await Settle(500);
        Check("预览本次执行（一条龙配置）", dialog is not null && plans.LastPreviewRows.Count > 0, plans.LastPreviewRows.Count.ToString());
        Shot("avalonia-newonedragon-preview.png");
        dialog?.Hide(FluentAvalonia.UI.Controls.FAContentDialogResult.None);
        await preview;
        await Settle();
    }

    /// <summary>调度器“添加地图追踪任务”选择对话框：筛选、排除已选择过的目录、勾选添加</summary>
    private async Task ScriptPathingAsync()
    {
        vm.Navigate("调度器");
        await Settle(600);
        var page = vm.Auto.Scheduler;
        page.Reload();
        await Settle();
        var root = BetterGenshinImpact.GameTask.AutoPathing.PathingPaths.PathJsonPath;
        var dir = Path.Combine(root, "m57测试目录");
        Directory.CreateDirectory(Path.Combine(dir, "子目录"));
        File.WriteAllText(Path.Combine(dir, "m57-a.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "子目录", "m57-b.json"), "{}");
        try
        {
            page.NewGroupName = "m57测试组";
            page.AddScriptGroupCommand.Execute(null);
            await Settle();
            var g = page.SelectedScriptGroup!;
            Check("“地图追踪”添加按钮", Named<Button>("AddPathingButton") is not null);
            var last = Services.FluentMessageBox.Current;
            var add = page.AddPathingCommand.ExecuteAsync(null);
            var dialog = await WaitFor(() => Services.FluentMessageBox.Current is { } c && c != last ? c : null);
            var panel = dialog?.Content as Panel;
            var filter = panel?.Children.OfType<TextBox>().FirstOrDefault();
            if (filter is not null) filter.Text = "m57";
            await Settle(400);
            Check("筛选：只剩两条测试路线", page.LastPathingCandidates.Count == 2 && page.LastPathingCandidates.Any(e => e.Display == "m57测试目录/子目录/m57-b.json"),
                string.Join(",", page.LastPathingCandidates.Select(e => e.Display)));
            var list = (panel?.Children.OfType<ScrollViewer>().FirstOrDefault()?.Content as Panel)?.Children.OfType<CheckBox>().ToList() ?? [];
            var a = list.FirstOrDefault(c => (c.Content as string) == "m57测试目录/m57-a.json");
            if (a is not null) a.IsChecked = true;
            await Settle();
            Shot("avalonia-script-pathing.png");
            dialog?.Hide(FluentAvalonia.UI.Controls.FAContentDialogResult.Primary);
            await add;
            await Settle();
            var p = g.Projects.FirstOrDefault(x => x.Type == "Pathing");
            Check("勾选的路线加入配置组（目录取相对路径）", g.Projects.Count == 1 && p?.Name == "m57-a.json" && p.FolderName == "m57测试目录", $"{p?.Name} {p?.FolderName}");

            last = Services.FluentMessageBox.Current;
            add = page.AddPathingCommand.ExecuteAsync(null);
            dialog = await WaitFor(() => Services.FluentMessageBox.Current is { } c && c != last ? c : null);
            panel = dialog?.Content as Panel;
            filter = panel?.Children.OfType<TextBox>().FirstOrDefault();
            if (filter is not null) filter.Text = "m57";
            var ex = panel?.Children.OfType<CheckBox>().FirstOrDefault();
            if (ex is not null) ex.IsChecked = true;
            await Settle(400);
            Check("排除已选择过的目录（含子目录）", page.LastPathingCandidates.Count == 0, string.Join(",", page.LastPathingCandidates.Select(e => e.Display)));
            dialog?.Hide(FluentAvalonia.UI.Controls.FAContentDialogResult.None);
            await add;
            await Settle();
            Check("取消时不添加", g.Projects.Count == 1);
            page.DeleteScriptGroupCommand.Execute(g);
            await Settle();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>JS 脚本 / 地图追踪 / 录制回放页头：标题、说明与教程链接、打开目录、脚本仓库说明、执行按钮、创建时间</summary>
    private async Task AutoPagesAsync()
    {
        vm.Navigate("调度器");
        await Settle(600);
        var auto = vm.Auto;
        var opened = new List<string>();
        ViewModels.AutoPageViewModel.OpenOverride = opened.Add;
        try
        {
            auto.Refresh();
            foreach (var (tab, prefix, kind, folder, url, shot) in new[]
                     {
                         (1, "Js", "js", auto.JsFolder, global::BetterGenshinImpact.ViewModel.Pages.AutoScriptPageTexts.JsTutorialUrl, "avalonia-auto-js-header.png"),
                         (2, "Pathing", "pathing", auto.PathingFolder, global::BetterGenshinImpact.ViewModel.Pages.AutoScriptPageTexts.PathingTutorialUrl, "avalonia-auto-pathing-header.png"),
                         (3, "Km", "km", auto.KmFolder, global::BetterGenshinImpact.ViewModel.Pages.AutoScriptPageTexts.KmTutorialUrl, "avalonia-auto-record-header.png"),
                     })
            {
                auto.SelectedTab = tab;
                await Settle(600);
                Check($"{prefix} 页头标题与说明", Named<TextBlock>(prefix + "Title")?.Text?.EndsWith("（实验功能）") == true);
                Check($"{prefix} 脚本仓库置灰并说明", All<Button>().Any(b => b.IsEffectivelyVisible && !b.IsEnabled && (b.Content as string ?? "").Contains("WebView2")));
                opened.Clear();
                await Click(Named<Button>(prefix + "OpenFolderButton"), "打开目录");
                Named<HyperlinkButton>(prefix + "TutorialLink")?.Command?.Execute(kind);
                Check($"{prefix} 打开目录与教程链接", opened.SequenceEqual([folder, url]), string.Join(" | ", opened));
                Shot(shot);
            }

            auto.SelectedTab = 1;
            await Settle(400);
            Check("JS 行有“执行脚本”按钮", auto.JsScripts.Count == 0 || ButtonByText("执行脚本") is not null);
            auto.SelectedTab = 2;
            await Settle(400);
            Check("地图追踪行有“执行”按钮", auto.PathingScripts.Count == 0 || ButtonByText("执行") is not null);
            Check("录制列表显示创建时间", auto.KmScripts.Count == 0 || auto.KmScripts.All(k => k.Detail.StartsWith("创建时间 ")), auto.KmScripts.FirstOrDefault()?.Detail);
        }
        finally
        {
            ViewModels.AutoPageViewModel.OpenOverride = null;
            auto.SelectedTab = 0;
        }
    }

    /// <summary>新一条龙右栏：任务名称 / 整组执行次数说明、高级设置（JSON 编辑、无效输入不写入、撤销）、账号切换步骤与导入报告置灰说明</summary>
    private async Task NewOneDragonPanelAsync()
    {
        vm.Navigate("新一条龙");
        await Settle(1000);
        var page = vm.NewOneDragon;
        var plans = page.Plans;
        await plans.ReloadAsync();
        await Settle(600);
        if (plans.SelectedPlan is null) { Check("有一条龙配置", false); return; }
        page.RightTab = 0;
        var id = plans.SelectedPlan.Plan.Id;
        var node = plans.Nodes.FirstOrDefault(n => n.Task.TaskType != "group") ?? plans.Nodes.FirstOrDefault();
        if (node is null) { Check("一条龙清单非空", false); return; }
        var tid = node.Task.Id;
        plans.SelectedNode = node;
        await Settle(600);
        var texts = All<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToList();
        Check("右栏“任务名称”及说明", texts.Contains("任务名称") && texts.Contains("只影响当前任务的显示名称。"));
        Check("账号切换步骤置灰说明", Named<Border>("AccountStepNote") is { Opacity: < 1 } && texts.Any(t => t.Contains("账号切换依赖 Windows")));
        var more = Named<DropDownButton>("MoreButton");
        Check("查看导入报告置灰说明", (more?.Flyout as MenuFlyout)?.Items.OfType<MenuItem>().Any(i => (i.Header as string ?? "").StartsWith("查看导入报告") && !i.IsEnabled) == true);
        Check("高级设置按钮", Named<Button>("AdvancedNodeButton") is not null);
        Check("可添加进程内 C# 任务", plans.AddOptions.Any(o => o.Definition.TaskType == "csharp"));

        static TextBox? Box(Control? root, string name) => root?.GetLogicalDescendants().OfType<TextBox>().FirstOrDefault(t => t.Name == name);
        JToken? Policy() => ReadPlan(id).Descendants().OfType<JObject>().FirstOrDefault(o => o.Value<string>("id") == tid)?["policy"]?["max_retries"];
        async Task Advanced(Action<Control?> edit, FluentAvalonia.UI.Controls.FAContentDialogResult result, string? shot = null)
        {
            var last = Services.FluentMessageBox.Current;
            var run = plans.EditNodeAdvancedCommand.ExecuteAsync(null);
            var dialog = await WaitFor(() => Services.FluentMessageBox.Current is { } c && c != last ? c : null);
            await Settle(500);
            edit(dialog?.Content as Control);
            if (shot is not null) Shot(shot);
            dialog?.Hide(result);
            await run;
            await Settle();
        }

        string? effective = null;
        await Advanced(root =>
        {
            effective = Box(root, "AdvancedEffectiveBox")?.Text;
            if (Box(root, "AdvancedPolicyBox") is { } p) p.Text = "{\"max_retries\": 2}";
        }, FluentAvalonia.UI.Controls.FAContentDialogResult.Primary, "avalonia-newonedragon-advanced.png");
        Check("高级设置显示有效参数", !string.IsNullOrWhiteSpace(effective) && effective!.TrimStart().StartsWith("{"), effective);
        Check("高级设置写入执行策略", Policy()?.Value<int?>() == 2, plans.StatusMessage);

        await Advanced(root => { if (Box(root, "AdvancedParametersBox") is { } b) b.Text = "[1, 2]"; }, FluentAvalonia.UI.Controls.FAContentDialogResult.Primary);
        Check("无效 JSON 不写入并提示", plans.StatusMessage.Contains("高级设置无效") && Policy()?.Value<int?>() == 2, plans.StatusMessage);

        await Advanced(root => { if (Box(root, "AdvancedPolicyBox") is { } p) p.Text = "{\"max_retries\": 5}"; }, FluentAvalonia.UI.Controls.FAContentDialogResult.None);
        Check("取消不写入", Policy()?.Value<int?>() == 2);

        await plans.UndoCommand.ExecuteAsync(null);
        await Settle();
        Check("撤销高级设置", Policy() is null || Policy()!.Type == JTokenType.Null, Policy()?.ToString());
        Shot("avalonia-newonedragon-panel.png");

        // 进程内 C# 任务：添加后为清单中的一项（默认 sample.sum 操作），再撤销
        var before = plans.Nodes.Count;
        plans.SelectedAddOption = plans.AddOptions.FirstOrDefault(o => o.Definition.TaskType == "csharp");
        await Settle();
        plans.AddTaskCommand.Execute(null);
        await Settle(800);
        Check("添加进程内 C# 任务", plans.Nodes.Count == before + 1 && plans.Nodes.Any(n => n.Task.TaskType == "csharp"), plans.StatusMessage);
        await plans.UndoCommand.ExecuteAsync(null);
        await Settle();
        Check("撤销添加", plans.Nodes.Count == before && plans.Nodes.All(n => n.Task.TaskType != "csharp"));
    }

    /// <summary>独立任务参数：自动战斗高级参数、伐木、打牌、幽境危战、首领、钓鱼</summary>
    // —— 独立任务“首领讨伐”：选择首领、树脂使用、按材料数量提前结束（gaps 12） ——

    private async Task SoloBossAsync()
    {
        vm.Navigate("独立任务");
        await Settle(600);
        var configPath = Path.Combine(Global.StartUpPath, "User", "config.json");
        var b = vm.Tasks.Boss;
        var savedBoss = b.Config.BossName;
        foreach (var ex in All<FluentAvalonia.UI.Controls.FASettingsExpander>()) ex.IsExpanded = true;
        await Settle(800);
        var box = Named<ComboBox>("BossNameBox");
        Check("首领选择下拉框", box is not null && b.BossNames.Count > 10, $"{b.BossNames.Count}");
        var boss = b.BossNames.FirstOrDefault(n => BetterGenshinImpact.GameTask.Common.Reward.TargetItemCatalog.Instance.GetBossTargetChoices(n).Count > 1);
        if (box is null || boss is null) return;
        box.SelectedItem = boss;
        await Settle();
        Check("选择首领写入配置", b.Config.BossName == boss, b.Config.BossName);
        Check("材料候选随首领刷新（首领材料在前）", b.TargetItems.Choices.Count > 1 && b.TargetItems.Choices[0].DisplayName.StartsWith("首领材料｜"),
            string.Join(",", b.TargetItems.Choices.Take(3).Select(c => c.DisplayName)));
        var targetMode = Named<ToggleSwitch>("BossTargetMode");
        Check("材料目标开关可用", targetMode?.IsEffectivelyEnabled == true);
        targetMode!.IsChecked = true;
        await Settle();
        if (b.TargetItems.Items.Count == 0) await Click(Named<Button>("BossTargetAdd"), "添加材料");
        await Settle();
        if (b.TargetItems.Items.FirstOrDefault() is { } first) first.TargetCount = 5;
        await Settle();
        Check("材料目标写入配置", b.Config.TargetItems.Count == 1 && b.Config.TargetItems[0].ItemName.Length > 0 && b.Config.TargetItems[0].TargetCount == 5,
            string.Join(",", b.Config.TargetItems.Select(t => $"{t.ItemName}×{t.TargetCount}")));
        Check("开启材料目标时奖励识别开关不可改（同 WPF）", Named<ToggleSwitch>("BossRewardSwitch")?.IsEffectivelyEnabled == false);
        Named<Border>("BossTargetItems")?.BringIntoView();
        await Settle(400);
        Shot("avalonia-solo-boss-targets.png");

        var before = b.BossResin.Summary;
        var resinDialog = await OpenResinDialogAsync(b.BossResin.EditCommand);
        Check("打开首领树脂使用对话框", resinDialog is not null);
        if (resinDialog is not null)
        {
            if (InDialog<RadioButton>(resinDialog, "ResinModeRunCount") is { IsEffectivelyEnabled: true } rc) rc.IsChecked = true;
            await Settle();
            if (InDialog<NumericUpDown>(resinDialog, "ResinRunCount") is { } n) n.Value = 2;
            await Settle();
            Shot("avalonia-solo-boss-resin-dialog.png");
            OnClickMethod.Invoke(InDialog<Button>(resinDialog, "ResinConfirmButton")!, null);
            await WaitFor(() => ResinSettingsWindow.Current is null ? "closed" : null);
            await Settle();
        }
        Check("树脂摘要更新", b.BossResin.Summary != before && Named<FluentAvalonia.UI.Controls.FASettingsExpanderItem>("BossResinItem")?.Description as string == b.BossResin.Summary,
            $"{before} → {b.BossResin.Summary}");
        Named<ComboBox>("BossNameBox")?.BringIntoView();
        await Settle(400);
        Shot("avalonia-solo-boss.png");
        await Task.Delay(3000);
        var saved = ReadJson(configPath)["autoBossConfig"];
        Check("config.json：首领 / 材料目标 / 树脂使用", (string?)saved?["bossName"] == boss && (saved?["targetItems"] as JArray)?.Count == 1 && saved?["resinUsage"] is JObject,
            saved?.ToString(Newtonsoft.Json.Formatting.None)[..Math.Min(300, saved?.ToString(Newtonsoft.Json.Formatting.None).Length ?? 0)]);
        // 还原（每组结束后脚本另会从备份恢复 config.json）
        targetMode.IsChecked = false;
        if (!string.IsNullOrEmpty(savedBoss)) b.Config.BossName = savedBoss;
        await Settle();
    }

    private async Task SoloParamsAsync()
    {
        vm.Navigate("独立任务");
        await Settle(600);
        var cfg = vm.Tasks.Config;
        string Snap() => System.Text.Json.JsonSerializer.Serialize(new object[] { cfg.AutoFightConfig, cfg.AutoWoodConfig, cfg.AutoGeniusInvokationConfig,
            cfg.AutoStygianOnslaughtConfig, cfg.AutoBossConfig, cfg.AutoFishingConfig, cfg.AutoArtifactSalvageConfig });
        var json = Snap();
        foreach (var ex in All<FluentAvalonia.UI.Controls.FASettingsExpander>()) ex.IsExpanded = true;
        await Settle(800);
        var f = cfg.AutoFightConfig;
        var saved = (f.ActionSchedulerByCd, f.FightFinishDetectEnabled, f.FinishDetectConfig.RotateFindEnemyEnabled, f.FinishDetectConfig.SkipFightEndCheckWhenEnemyVisible,
            f.DamageNumberRecognitionMode, f.GuardianAvatar, f.Timeout, cfg.AutoWoodConfig.UseWonderlandRefresh, cfg.AutoWoodConfig.AfterZSleepDelay,
            cfg.AutoGeniusInvokationConfig.SleepDelay, cfg.AutoStygianOnslaughtConfig.BossNum, cfg.AutoStygianOnslaughtConfig.FightTeamName,
            cfg.AutoBossConfig.TeamName, cfg.AutoFishingConfig.AutoThrowRodTimeOut, cfg.AutoFishingConfig.FishingTimePolicy);
        Check("自动战斗高级参数卡片", Named<Views.SoloFightCard>("SoloFightCard") is not null && Named<TextBox>("SfActionSchedulerByCd") is not null);
        Named<TextBox>("SfActionSchedulerByCd")!.Text = "白术;钟离,12";
        Named<ToggleSwitch>("SfFinishDetect")!.IsChecked = true;
        await Settle();
        Named<ToggleSwitch>("SfSkipWhenEnemyVisible")!.IsChecked = true;
        await Settle();
        Named<ToggleSwitch>("SfRotateFindEnemy")!.IsChecked = true;
        await Settle();
        Check("旋转寻敌与“敌人可见时跳过”互斥（Core 配置逻辑）", f.FinishDetectConfig.RotateFindEnemyEnabled && !f.FinishDetectConfig.SkipFightEndCheckWhenEnemyVisible
            && Named<ToggleSwitch>("SfSkipWhenEnemyVisible")?.IsChecked == false);
        Named<ComboBox>("SfDamageNumberMode")!.SelectedIndex = 1;
        Named<ComboBox>("SfGuardianAvatar")!.SelectedItem = "2";
        Named<NumericUpDown>("SfTimeout")!.Value = 180;
        await Settle();
        Check("出招 CD / 伤害数字模式 / 盾奶位 / 超时写入配置", f.ActionSchedulerByCd == "白术;钟离,12"
            && f.DamageNumberRecognitionMode == BetterGenshinImpact.GameTask.AutoFight.DamageNumberRecognitionMode.Ocr && f.GuardianAvatar == "2" && f.Timeout == 180,
            $"{f.DamageNumberRecognitionMode} {f.GuardianAvatar} {f.Timeout}");
        Named<Views.SoloFightCard>("SoloFightCard")!.BringIntoView();
        await Settle(400);
        Shot("avalonia-solo-fight.png");

        Named<ToggleSwitch>("WoodWonderlandSwitch")!.IsChecked = !saved.UseWonderlandRefresh;
        Named<NumericUpDown>("WoodAfterZDelayBox")!.Value = 300;
        Named<NumericUpDown>("TcgSleepDelayBox")!.Value = 200;
        Named<ComboBox>("StygianBossNumBox")!.SelectedItem = 3;
        Named<TextBox>("StygianTeamBox")!.Text = "m56队伍";
        Named<TextBox>("BossTeamNameBox")!.Text = "m56首领队";
        Named<NumericUpDown>("FishThrowTimeoutBox")!.Value = 25;
        var fishBox = Named<ComboBox>("FishTimePolicyBox")!;
        Check("钓鱼昼夜策略显示当前值", fishBox.SelectedItem is BetterGenshinImpact.ViewModel.Pages.SoloTask.SoloTaskChoice c0 && Equals(c0.Value, saved.FishingTimePolicy), fishBox.SelectedItem?.ToString());
        fishBox.SelectedIndex = 2;
        await Settle();
        Check("伐木 / 打牌 / 幽境危战 / 首领 / 钓鱼参数写入配置", cfg.AutoWoodConfig.UseWonderlandRefresh == !saved.UseWonderlandRefresh && cfg.AutoWoodConfig.AfterZSleepDelay == 300
            && cfg.AutoGeniusInvokationConfig.SleepDelay == 200 && cfg.AutoStygianOnslaughtConfig.BossNum == 3 && cfg.AutoStygianOnslaughtConfig.FightTeamName == "m56队伍"
            && cfg.AutoBossConfig.TeamName == "m56首领队" && cfg.AutoFishingConfig.AutoThrowRodTimeOut == 25
            && cfg.AutoFishingConfig.FishingTimePolicy == BetterGenshinImpact.GameTask.AutoFishing.FishingTimePolicy.Nighttime);
        Named<ComboBox>("StygianBossNumBox")!.BringIntoView();
        await Settle(400);
        Shot("avalonia-solo-stygian.png");
        Named<ComboBox>("FishTimePolicyBox")!.BringIntoView();
        await Settle(400);
        Shot("avalonia-solo-fishing.png");
        // 还原
        f.ActionSchedulerByCd = saved.ActionSchedulerByCd; f.FightFinishDetectEnabled = saved.FightFinishDetectEnabled;
        f.FinishDetectConfig.SkipFightEndCheckWhenEnemyVisible = saved.SkipFightEndCheckWhenEnemyVisible; f.FinishDetectConfig.RotateFindEnemyEnabled = saved.RotateFindEnemyEnabled;
        f.DamageNumberRecognitionMode = saved.DamageNumberRecognitionMode; f.GuardianAvatar = saved.GuardianAvatar; f.Timeout = saved.Timeout;
        cfg.AutoWoodConfig.UseWonderlandRefresh = saved.UseWonderlandRefresh; cfg.AutoWoodConfig.AfterZSleepDelay = saved.AfterZSleepDelay;
        cfg.AutoGeniusInvokationConfig.SleepDelay = saved.SleepDelay; cfg.AutoStygianOnslaughtConfig.BossNum = saved.BossNum;
        cfg.AutoStygianOnslaughtConfig.FightTeamName = saved.FightTeamName; cfg.AutoBossConfig.TeamName = saved.TeamName;
        cfg.AutoFishingConfig.AutoThrowRodTimeOut = saved.AutoThrowRodTimeOut; cfg.AutoFishingConfig.FishingTimePolicy = saved.FishingTimePolicy;
        await Settle();
        Check("还原后配置与开始时相同", Snap() == json);
    }

    /// <summary>实时触发补项：拾取 OCR 引擎、黑白名单规则对话框、自动剧情/邀约/吃药/快速传送参数、地图遮罩、冷却提示与角色 CD 配置</summary>
    private async Task TriggerMoreAsync()
    {
        vm.Navigate("实时触发");
        await Settle(600);
        var page = vm.Trigger;
        var cfg = page.Config;
        foreach (var ex in All<Expander>()) ex.IsExpanded = true;
        await Settle(600);
        var savedMode = cfg.AutoPickConfig.Mode;
        var savedEngine = cfg.AutoPickConfig.OcrEngine;
        var savedEat = cfg.AutoEatConfig.CheckInterval;
        var savedPrio = cfg.AutoSkipConfig.CustomPriorityOptions;
        var savedCd = cfg.SkillCdConfig.CustomCdList;
        var savedPickOn = cfg.AutoPickConfig.BlacklistModePickEnabled;
        Named<ComboBox>("PickOcrEngineBox")!.SelectedItem = "Yap";
        Named<NumericUpDown>("EatCheckIntervalBox")!.Value = 300;
        Named<TextBox>("CustomPriorityOptionsBox")!.Text = "m55选项A;m55选项B";
        await Settle();
        Check("拾取引擎/吃药间隔/自定义优先选项写入配置", cfg.AutoPickConfig.OcrEngine == "Yap" && cfg.AutoEatConfig.CheckInterval == 300
            && cfg.AutoSkipConfig.CustomPriorityOptions == "m55选项A;m55选项B");
        Check("后台模式 / 画中画 / 语音等待显示 Linux 不可用说明", All<TextBlock>().Any(t => t.Text?.StartsWith("Linux 版不可用：XTest") == true)
            && All<TextBlock>().Any(t => t.Text?.Contains("WASAPI") == true));
        Check("地图遮罩、冷却提示、快速传送、邀约卡片", Named<Expander>("MapMaskCard") is not null && Named<Expander>("SkillCdCard") is not null
            && Named<NumericUpDown>("TeleportListClickDelayBox") is not null && Named<ComboBox>("HangoutBranchBox") is { ItemCount: > 0 });
        Shot("avalonia-trigger-top.png");

        // 黑名单规则对话框：读写 User/pick_black_lists.txt，保存后恢复原文件
        var file = Global.Absolute(BetterGenshinImpact.ViewModel.Pages.Trigger.AutoPickRuleFiles.BlacklistDoNotPickPath);
        var existed = File.Exists(file);
        var original = existed ? File.ReadAllText(file) : null;
        page.SetAutoPickModeCommand.Execute(BetterGenshinImpact.GameTask.AutoPick.AutoPickMode.Blacklist);
        await Settle();
        var last = Services.FluentMessageBox.Current;
        var open = page.OpenBlacklistModeConfigCommand.ExecuteAsync(null);
        var dialog = await WaitFor(() => Services.FluentMessageBox.Current is { } c && c != last ? c : null);
        await Settle(400);
        var boxes = dialog?.GetVisualDescendants().OfType<TextBox>().ToList() ?? [];
        var doNot = boxes.FirstOrDefault(t => t.Name == "BlacklistDoNotPickBox");
        Check("黑名单规则对话框显示三个名单", doNot is not null && boxes.Any(t => t.Name == "BlacklistFuzzyBox") && boxes.Any(t => t.Name == "BlacklistPickBox")
            && doNot.Text == (original ?? ""));
        if (doNot is not null) doNot.Text = (original ?? "") + "\nm55测试宝箱";
        Shot("avalonia-trigger-blacklist-dialog.png");
        dialog?.Hide(FluentAvalonia.UI.Controls.FAContentDialogResult.Primary);
        await open;
        await Settle();
        Check("保存写入名单文件", File.Exists(file) && File.ReadAllText(file).Contains("m55测试宝箱"));
        if (existed) File.WriteAllText(file, original!); else File.Delete(file);
        foreach (var p in new[] { BetterGenshinImpact.ViewModel.Pages.Trigger.AutoPickRuleFiles.BlacklistFuzzyDoNotPickPath, BetterGenshinImpact.ViewModel.Pages.Trigger.AutoPickRuleFiles.BlacklistPickPath })
        {
            var f = Global.Absolute(p);
            if (File.Exists(f) && File.ReadAllText(f).Length == 0 && File.GetLastWriteTime(f) > DateTime.Now.AddMinutes(-2)) File.Delete(f);
        }

        // 白名单规则对话框：取消不写文件
        page.SetAutoPickModeCommand.Execute(BetterGenshinImpact.GameTask.AutoPick.AutoPickMode.Whitelist);
        await Settle();
        Check("切到白名单后显示白名单规则按钮", Named<Button>("WhitelistRulesButton") is not null && Named<Button>("BlacklistRulesButton") is null);
        last = Services.FluentMessageBox.Current;
        open = page.OpenWhitelistModeConfigCommand.ExecuteAsync(null);
        dialog = await WaitFor(() => Services.FluentMessageBox.Current is { } c && c != last ? c : null);
        await Settle(400);
        Check("白名单规则对话框", dialog?.GetVisualDescendants().OfType<TextBox>().Any(t => t.Name == "WhitelistPickBox") == true);
        dialog?.Hide(FluentAvalonia.UI.Controls.FAContentDialogResult.None);
        await open;

        // 角色 CD 配置
        last = Services.FluentMessageBox.Current;
        open = page.EditSkillCdConfigCommand.ExecuteAsync(null);
        dialog = await WaitFor(() => Services.FluentMessageBox.Current is { } c && c != last ? c : null);
        await Settle(400);
        var add = dialog?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "SkillCdAddRuleButton");
        add?.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await Settle();
        var rows = dialog?.GetVisualDescendants().OfType<StackPanel>().FirstOrDefault(p => p.Name == "SkillCdRulesPanel");
        var lastRow = rows?.Children.OfType<StackPanel>().LastOrDefault();
        var tbs = lastRow?.Children.OfType<TextBox>().ToList();
        if (tbs is { Count: 2 }) { tbs[0].Text = " m55测试角色 "; tbs[1].Text = "12"; }
        await Settle();
        Shot("avalonia-trigger-skillcd-dialog.png");
        dialog?.Hide(FluentAvalonia.UI.Controls.FAContentDialogResult.Primary);
        await open;
        Check("角色 CD 配置保存（去空白、丢弃空行）", cfg.SkillCdConfig.CustomCdList.Any(r => r.RoleName == "m55测试角色" && r.CdValueText == "12")
            && cfg.SkillCdConfig.CustomCdList.All(r => !string.IsNullOrWhiteSpace(r.RoleName)), string.Join(",", cfg.SkillCdConfig.CustomCdList.Select(r => r.RoleName)));

        Named<Button>("EditSkillCdButton")?.BringIntoView();
        await Settle(400);
        Shot("avalonia-trigger-bottom.png");
        // 还原
        cfg.SkillCdConfig.CustomCdList = savedCd;
        cfg.AutoPickConfig.OcrEngine = savedEngine;
        cfg.AutoEatConfig.CheckInterval = savedEat;
        cfg.AutoSkipConfig.CustomPriorityOptions = savedPrio;
        cfg.AutoPickConfig.BlacklistModePickEnabled = savedPickOn;
        page.SetAutoPickModeCommand.Execute(savedMode);
        await Settle();
        await Click(Named<Button>("GoToHotkeysButton"), "快捷键链接");
        await Settle(600);
        Check("“手动触发快速传送触发快捷键”跳转快捷键页", vm.CurrentPage == vm.Settings && vm.Settings.SelectedTab == 1);
    }

    /// <summary>通用设置补项：七天神像国家/区域写回点位、准星类型、传送间隔、图标识别模型、米游社、Windows 专属说明、OCR 跳转插件页</summary>
    private async Task SettingsGeneralAsync()
    {
        vm.Navigate("设置");
        await Settle(600);
        var page = vm.Settings;
        var cfg = page.Config;
        foreach (var ex in All<FluentAvalonia.UI.Controls.FASettingsExpander>()) ex.IsExpanded = true;
        await Settle(600);
        Check("Windows 专属设置显示说明", Named<FluentAvalonia.UI.Controls.FASettingsExpander>("WindowsOnlyCard") is not null
            && Named<FluentAvalonia.UI.Controls.FASettingsExpander>("UiLanguageCard") is not null);
        var tp = cfg.TpConfig;
        var saved = (tp.ReviveStatueOfTheSevenCountry, tp.ReviveStatueOfTheSevenArea, tp.ReviveStatueOfTheSevenPointX, tp.ReviveStatueOfTheSevenPointY, tp.TeleportOperationDelayMilliseconds,
            cfg.MaskWindowConfig.CrosshairType, cfg.OtherConfig.ItemIconRecognitionMode);
        Check("七天神像国家列表来自地图点位", page.StatueCountries.Count >= 4 && page.SelectedStatueCountry == tp.ReviveStatueOfTheSevenCountry,
            string.Join(",", page.StatueCountries));
        var country = page.StatueCountries.First(c => c != tp.ReviveStatueOfTheSevenCountry);
        Named<ComboBox>("StatueCountryBox")!.SelectedItem = country;
        await Settle();
        Check("切换国家后刷新区域并写回国家/区域/坐标", page.StatueAreas.Count > 0 && tp.ReviveStatueOfTheSevenCountry == country
            && tp.ReviveStatueOfTheSevenArea == page.StatueAreas[0] && tp.ReviveStatueOfTheSevenPointX != saved.ReviveStatueOfTheSevenPointX,
            $"{country}/{tp.ReviveStatueOfTheSevenArea} ({tp.ReviveStatueOfTheSevenPointX},{tp.ReviveStatueOfTheSevenPointY})");
        Named<NumericUpDown>("TeleportDelayBox")!.Value = 35;
        Named<ComboBox>("CrosshairTypeBox")!.SelectedItem = BetterGenshinImpact.Core.Config.CrosshairType.Dot;
        Named<ComboBox>("ItemIconModeBox")!.SelectedItem = BetterGenshinImpact.GameTask.Common.Job.ItemIconRecognitionMode.GridIcon;
        await Settle();
        Check("传送间隔/准星类型/图标识别模型写入配置", tp.TeleportOperationDelayMilliseconds == 35
            && cfg.MaskWindowConfig.CrosshairType == BetterGenshinImpact.Core.Config.CrosshairType.Dot
            && cfg.OtherConfig.ItemIconRecognitionMode == BetterGenshinImpact.GameTask.Common.Job.ItemIconRecognitionMode.GridIcon);
        Check("米游社 cookie 输入框为密码样式", Named<TextBox>("MiyousheCookieBox") is { PasswordChar: '●' });
        Shot("avalonia-settings-general.png");
        Named<ComboBox>("StatueCountryBox")!.BringIntoView();
        await Settle(400);
        Shot("avalonia-settings-general-statue.png");
        Named<ComboBox>("ItemIconModeBox")!.BringIntoView();
        await Settle(400);
        Shot("avalonia-settings-general-other.png");
        // 还原
        page.SelectedStatueCountry = saved.ReviveStatueOfTheSevenCountry;
        page.SelectedStatueArea = saved.ReviveStatueOfTheSevenArea;
        tp.ReviveStatueOfTheSevenPointX = saved.ReviveStatueOfTheSevenPointX;
        tp.ReviveStatueOfTheSevenPointY = saved.ReviveStatueOfTheSevenPointY;
        tp.TeleportOperationDelayMilliseconds = saved.TeleportOperationDelayMilliseconds;
        cfg.MaskWindowConfig.CrosshairType = saved.CrosshairType;
        cfg.OtherConfig.ItemIconRecognitionMode = saved.ItemIconRecognitionMode;
        await Settle();
        Check("还原七天神像设置", tp.ReviveStatueOfTheSevenCountry == saved.ReviveStatueOfTheSevenCountry && tp.ReviveStatueOfTheSevenArea == saved.ReviveStatueOfTheSevenArea);
        await Click(Named<Button>("OcrPluginsButton"), "管理插件");
        await Settle(600);
        Check("OCR 配置“管理插件”跳转插件页", vm.CurrentPage == vm.Plugins);
    }

    /// <summary>按键绑定设置（WPF 通用设置“按键绑定设置”打开的窗口）：树、改键写回 KeyBindingsConfig、全局按键映射开关</summary>
    private async Task KeyBindingsAsync()
    {
        vm.Navigate("设置");
        await Settle(600);
        var cfg = vm.Settings.Config.KeyBindingsConfig;
        Check("通用设置有按键绑定卡片", Named<FluentAvalonia.UI.Controls.FASettingsExpander>("KeyBindingsCard") is not null);
        var mapping = cfg.GlobalKeyMappingEnabled;
        if (Named<FluentAvalonia.UI.Controls.FASettingsExpander>("KeyBindingsCard") is { } card) card.IsExpanded = true;
        await Settle();
        Named<ToggleSwitch>("GlobalKeyMappingSwitch")!.IsChecked = !mapping;
        await Settle();
        Check("全局按键映射开关写入配置", cfg.GlobalKeyMappingEnabled == !mapping);
        Named<ToggleSwitch>("GlobalKeyMappingSwitch")!.IsChecked = mapping;
        await Click(Named<Button>("OpenKeyBindingsButton"), "打开");
        var win = await WaitFor(() => Views.KeyBindingsWindow.Current);
        Check("打开按键绑定窗口", win is not null);
        if (win is null) return;
        await Settle(600);
        var model = (win.DataContext as KeyBindingsWindowViewModel)!;
        Check("动作 / 菜单两组", model.Models.Select(m => m.ActionName).SequenceEqual(new[] { "动作", "菜单" }) && model.Models[0].Children.Count > 5);
        var combos = win.GetVisualDescendants().OfType<ComboBox>().Where(c => c.IsVisible && c.ItemCount > 50).ToList();
        Check("每个按键一个下拉框并显示当前按键", combos.Count > 5 && combos[0].SelectedValue is BetterGenshinImpact.Core.Config.KeyId k0 && k0 == cfg.MoveForward, combos.FirstOrDefault()?.SelectedValue?.ToString());
        var old = cfg.MoveForward;
        var target = old == BetterGenshinImpact.Core.Config.KeyId.Up ? BetterGenshinImpact.Core.Config.KeyId.Down : BetterGenshinImpact.Core.Config.KeyId.Up;
        if (combos.Count > 0) combos[0].SelectedValue = target;
        await Settle();
        Check("改键写回 KeyBindingsConfig", cfg.MoveForward == target, cfg.MoveForward.ToString());
        Check("注册表读取在 Linux 上禁用并说明", win.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "FetchFromRegistryButton")?.IsEnabled == false);
        Shot(win, "avalonia-key-bindings.png");
        if (combos.Count > 0) combos[0].SelectedValue = old;
        await Settle();
        Check("恢复原按键", cfg.MoveForward == old);
        win.Close();
        await Settle();
    }

    /// <summary>首页横幅：与 WPF 相同使用角色背景图（默认 WPF Resources/Images/banner.jpg）</summary>
    private async Task HomeBannerAsync()
    {
        vm.Navigate("启动");
        await Settle(600);
        var banner = Named<Border>("HomeBannerImage");
        var brush = banner?.Background as global::Avalonia.Media.ImageBrush;
        var bitmap = brush?.Source as global::Avalonia.Media.Imaging.Bitmap;
        Check("横幅为图片背景", bitmap is not null && bitmap.PixelSize.Width > 0, bitmap?.PixelSize.ToString());
        Check("未设置自定义图片时使用默认图", !vm.Home.IsCustomBanner && bitmap?.PixelSize == new global::Avalonia.PixelSize(2538, 1157) || vm.Home.IsCustomBanner, bitmap?.PixelSize.ToString());
        Check("横幅左下向右上渐隐", banner?.OpacityMask is global::Avalonia.Media.LinearGradientBrush);
        Check("标题文字", Named<Grid>("HomeBanner")?.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "BetterGI") == true);
        Shot("avalonia-home-banner.png");

        // 更换背景图片（本地文件；文件选择框由 ChangeBannerImageCommand 打开，这里直接应用测试图片）与恢复默认
        var menu = Named<Grid>("HomeBanner")?.ContextMenu;
        Check("横幅右键菜单（更换背景图片 / 恢复默认图片）", menu?.Items.OfType<MenuItem>().Select(m => m.Header as string).SequenceEqual(new[] { "更换背景图片", "恢复默认图片" }) == true);
        var test = Path.Combine(Path.GetTempPath(), "m54-banner.png");
        using (var rt = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new global::Avalonia.PixelSize(64, 32))) rt.Save(test);
        vm.Home.ApplyCustomBanner(test);
        await Settle();
        bitmap = (Named<Border>("HomeBannerImage")?.Background as global::Avalonia.Media.ImageBrush)?.Source as global::Avalonia.Media.Imaging.Bitmap;
        Check("更换后显示自定义图片并复制到 User/Images/custom_banner.jpg", vm.Home.IsCustomBanner && bitmap?.PixelSize == new global::Avalonia.PixelSize(64, 32)
            && File.Exists(BetterGenshinImpact.Core.Config.HomeBannerImage.CustomPath), bitmap?.PixelSize.ToString());
        Shot("avalonia-home-banner-custom.png");
        vm.Home.ResetBannerImageCommand.Execute(null);
        await Settle();
        bitmap = (Named<Border>("HomeBannerImage")?.Background as global::Avalonia.Media.ImageBrush)?.Source as global::Avalonia.Media.Imaging.Bitmap;
        Check("恢复默认：删除自定义图片并显示默认图", !vm.Home.IsCustomBanner && !File.Exists(BetterGenshinImpact.Core.Config.HomeBannerImage.CustomPath)
            && bitmap?.PixelSize == new global::Avalonia.PixelSize(2538, 1157), bitmap?.PixelSize.ToString());
        File.Delete(test);
    }

    /// <summary>
    /// 旧一条龙页：新增/删除/复制/排序任务，新增/重命名/删除配置（FAContentDialog 确认），结束后切回原配置
    /// </summary>
    private async Task OneDragonEditAsync()
    {
        vm.Navigate("一条龙");
        await Settle(600);
        var page = vm.OneDragon;
        var original = page.SelectedConfig?.Name;
        Check("当前配置", original is not null);
        if (original is null) return;
        Check("未迁移提示已去掉", !All<TextBlock>().Any(t => t.Text?.Contains("尚未迁移") == true));
        BetterGenshinImpact.Core.Config.OneDragonFlowConfig? Load(string n) => BetterGenshinImpact.Core.Config.OneDragonFlowConfigStore.Load(n);
        async Task<FluentAvalonia.UI.Controls.FAContentDialog?> Dialog(FluentAvalonia.UI.Controls.FAContentDialog? last = null) =>
            await WaitFor(() => Services.FluentMessageBox.Current is { } c && c != last ? c : null);
        TextBox? Prompt(FluentAvalonia.UI.Controls.FAContentDialog? d) => (d?.Content as Panel)?.Children.OfType<TextBox>().FirstOrDefault(t => t.Name == "PromptTextBox");

        // 新增配置
        await Click(Named<Button>("OdAddConfigButton"), "新增配置");
        var d = await Dialog();
        Check("新增配置弹出输入框", Prompt(d) is not null && d?.Title as string == "新增一条龙配置", d?.Title as string);
        if (Prompt(d) is { } b1) b1.Text = "m54测试";
        d?.Hide(FluentAvalonia.UI.Controls.FAContentDialogResult.Primary);
        await Settle();
        var baseCount = page.TaskList.Count;
        Check("新增配置写入并选中（沿用当前任务列表，与 WPF 相同）", page.SelectedConfig?.Name == "m54测试" && Load("m54测试") is { } nc && nc.TaskOrder.Count == baseCount && baseCount > 0
            && page.Config.SelectedOneDragonFlowConfigName == "m54测试", baseCount.ToString());

        // 新增任务（多选：一个内置任务 + 一个配置组）
        await Click(Named<Button>("OdAddTaskButton"), "新增任务");
        d = await Dialog();
        var boxes = ((d?.Content as ScrollViewer)?.Content as Panel)?.Children.OfType<CheckBox>().ToList() ?? [];
        Check("新增任务弹窗列出内置任务与配置组", boxes.Any(x => (string?)x.Tag == "领取邮件") && boxes.Any(x => (string?)x.Tag == "m31-macro"), string.Join(",", boxes.Select(x => x.Tag)));
        foreach (var x in boxes) x.IsChecked = (string?)x.Tag is "领取邮件" or "m31-macro";
        Shot("avalonia-onedragon-edit-add.png");
        d?.Hide(FluentAvalonia.UI.Controls.FAContentDialogResult.Primary);
        await Settle();
        var names = page.TaskList.Select(t => t.Name).ToList();
        var lastBuiltIn = names.FindLastIndex(n => n == "领取邮件");
        Check("内置任务插在内置任务之后、配置组追加到末尾", page.TaskList.Count == baseCount + 2 && names[^1] == "m31-macro"
            && lastBuiltIn >= 0 && (lastBuiltIn + 1 == names.Count - 1 || BetterGenshinImpact.Model.OneDragonTaskItem.BuiltInTaskNames.Contains(names[lastBuiltIn + 1]) == false), string.Join(",", names));
        var saved = Load("m54测试");
        Check("新增任务已保存", saved is not null && saved.TaskOrder.Count == page.TaskList.Count && saved.TaskDefinitions.Values.Contains("m31-macro"));

        // 排序：配置组上移一位
        var macro = page.TaskList[^1];
        page.MoveTaskUpCommand.Execute(macro);
        await Settle();
        Check("上移写回顺序", page.TaskList[^2] == macro && Load("m54测试")?.TaskOrder[^2] == macro.Id);
        page.MoveTaskDownCommand.Execute(macro);
        await Settle();
        Check("下移写回顺序", page.TaskList[^1] == macro && Load("m54测试")?.TaskOrder[^1] == macro.Id);

        // 复制、删除（删除用 FAContentDialog 确认）
        page.CopyTaskCommand.Execute(macro);
        await Settle();
        Check("复制任务插在原任务之后", page.TaskList.Count == baseCount + 3 && page.TaskList[^1].Name == "m31-macro" && page.TaskList[^1].Id != macro.Id);
        var copy = page.TaskList[^1];
        var del = page.DeleteTaskCommand.ExecuteAsync(copy);
        d = await Dialog();
        Check("删除任务弹出确认", d is not null);
        d?.Hide(FluentAvalonia.UI.Controls.FAContentDialogResult.Primary);
        await del;
        await Settle();
        Check("删除任务写回", !page.TaskList.Contains(copy) && Load("m54测试")?.TaskDefinitions.ContainsKey(copy.Id) == false);

        // 重命名配置
        await Click(Named<Button>("OdRenameConfigButton"), "重命名配置");
        d = await Dialog();
        Check("重命名弹出输入框（初值为原名）", Prompt(d)?.Text == "m54测试", Prompt(d)?.Text);
        if (Prompt(d) is { } b2) b2.Text = "m54改名";
        d?.Hide(FluentAvalonia.UI.Controls.FAContentDialogResult.Primary);
        await Settle();
        Check("重命名：新文件、删除旧文件、更新选中名", Load("m54改名") is { } r && r.TaskOrder.Count == page.TaskList.Count && Load("m54测试") is null
            && page.Config.SelectedOneDragonFlowConfigName == "m54改名" && Named<ComboBox>("OneDragonConfigBox")?.SelectedItem == page.SelectedConfig);

        // 删除配置：先取消，再确认
        await Click(Named<Button>("OdDeleteConfigButton"), "删除配置");
        d = await Dialog();
        Check("删除配置确认文字来自 Core", d?.Content is Control c && c.GetLogicalDescendants().OfType<SelectableTextBlock>().Any(t => t.Text == BetterGenshinImpact.ViewModel.Pages.OneDragon.OneDragonConfigEditing.DeleteConfirmText("m54改名")));
        d?.Hide(FluentAvalonia.UI.Controls.FAContentDialogResult.None);
        await Settle();
        Check("取消删除不改变", Load("m54改名") is not null && page.SelectedConfig?.Name == "m54改名");
        await Click(Named<Button>("OdDeleteConfigButton"), "删除配置");
        d = await Dialog();
        d?.Hide(FluentAvalonia.UI.Controls.FAContentDialogResult.Primary);
        await Settle();
        Check("确认删除：文件删除并选中其他配置", Load("m54改名") is null && page.ConfigList.All(x => x.Name != "m54改名") && page.SelectedConfig is not null);

        page.SelectedConfig = page.ConfigList.FirstOrDefault(x => x.Name == original);
        await Settle();
        Check("切回原配置", page.SelectedConfig?.Name == original && page.Config.SelectedOneDragonFlowConfigName == original);
        Shot("avalonia-onedragon-edit.png");
    }

    /// <summary>
    /// 旧一条龙页（对应 WPF OneDragonFlowPage）：左右两栏（左任务列表与启动，右配置）、勾选写入配置、“从此执行”标记下次开始的任务并保存
    /// </summary>
    private async Task OneDragonLayoutAsync()
    {
        vm.Navigate("一条龙");
        await Settle(600);
        var page = vm.OneDragon;
        var list = Named<ItemsControl>("OneDragonTaskList");
        var configBox = Named<ComboBox>("OneDragonConfigBox");
        var run = Named<Button>("OneDragonRunButton");
        Check("两栏控件", list is not null && configBox is not null && run is not null && Named<ComboBox>("CompletionActionBox") is not null);
        if (list is null || configBox is null || run is null) return;
        var listPos = list.TranslatePoint(new Point(0, 0), window);
        var configPos = configBox.TranslatePoint(new Point(0, 0), window);
        Check("任务列表在左、配置在右", listPos is { } l && configPos is { } c && l.X + list.Bounds.Width <= c.X && run.TranslatePoint(new Point(0, 0), window)!.Value.X < c.X,
            $"{listPos} {configPos}");
        Check("任务列表项", page.TaskList.Count > 0 && list.ItemCount == page.TaskList.Count, page.TaskList.Count.ToString());
        Check("配置下拉", page.ConfigList.Count > 0 && configBox.SelectedItem == page.SelectedConfig);
        var item = page.TaskList[Math.Min(1, page.TaskList.Count - 1)];
        var box = list.GetVisualDescendants().OfType<CheckBox>().First(b => b.DataContext == item);
        var was = item.IsEnabled;
        box.IsChecked = !was;
        await Settle();
        Check("勾选写回任务", item.IsEnabled == !was);
        box.IsChecked = was;
        var next = list.GetVisualDescendants().OfType<Button>().First(b => b.DataContext == item && b.Content as string == "从此执行");
        await Click(next, "从此执行");
        Check("标记下次开始", item.IsNextTask && page.SelectedConfig?.NextTaskId == item.Id && page.NextTaskText.Contains(item.Name), page.NextTaskText);
        Shot("avalonia-onedragon.png");
        await Click(next, "取消从此执行");
        Check("再次点击取消", !item.IsNextTask && page.SelectedConfig?.NextTaskId == "");
    }

    /// <summary>
    /// 插件页（对应 WPF PluginsPage）：功能选择（Core PluggableRegistry，改选写入 PluginConfig）、已安装插件只读列表（读取 plugin.json）
    /// </summary>
    private async Task PluginsAsync()
    {
        var page = vm.Plugins;
        var testDir = Path.Combine(page.PluginsDirectory, "m51-test-plugin");
        var createdRoot = !Directory.Exists(page.PluginsDirectory);
        Directory.CreateDirectory(testDir);
        File.WriteAllText(Path.Combine(testDir, "plugin.json"), "{\"manifest_version\":1,\"id\":\"m51.test\",\"name\":\"M51 测试插件\",\"version\":\"1.2.3\",\"description\":\"只读列表测试\"}");
        try
        {
            vm.Navigate("插件");
            await Settle(600);
            Check("Linux 提示", Named<Border>("LinuxPluginNotice")?.IsEffectivelyVisible == true);
            var registry = page.Registry;
            Check("功能选择与注册表一致", registry is not null && page.Pluggables.Count == registry.All.Count && page.Pluggables.Count > 0, $"{page.Pluggables.Count}");
            Check("每项都有当前选择", page.Pluggables.All(p => p.SelectedOption is not null && p.Options.Count > 0),
                string.Join(";", page.Pluggables.Select(p => $"{p.DisplayName}:{p.Options.Count}")));
            var multi = page.Pluggables.FirstOrDefault(p => p.Options.Count(o => !o.IsUnavailable) > 1);
            if (multi is not null && registry is not null)
            {
                var before = multi.SelectedOption!;
                var other = multi.Options.First(o => !o.IsUnavailable && o.Id != before.Id);
                multi.SelectedOption = other;
                await Settle();
                Check("改选写入注册表", registry.GetSelection(multi.Descriptor.Id).Effective.Id == other.Id, other.Id);
                multi.SelectedOption = multi.Options.First(o => o.Id == before.Id);
                await Settle();
                Check("改回原选择", registry.GetSelection(multi.Descriptor.Id).Effective.Id == before.Id);
            }
            else
            {
                log.Information("插件页：没有多实现的功能，跳过改选检查");
            }

            await Click(Named<Button>("RefreshPluginsButton")!, "刷新");
            var item = page.Plugins.FirstOrDefault(p => p.Id == "m51.test");
            Check("读取已安装插件", item is { Name: "M51 测试插件", VersionText: "v1.2.3" } && !page.HasNoPlugins, item?.ToString());
            Check("列表显示", Named<ItemsControl>("InstalledList")?.ItemCount == page.Plugins.Count);
            Check("权限警告", Named<Border>("PluginPermissionWarning")?.IsEffectivelyVisible == true);
            var uninstall = All<Button>().Where(b => b.IsEffectivelyVisible && b.Content as string == "卸载").ToList();
            Check("卸载按钮置灰并说明", uninstall.Count == page.Plugins.Count && uninstall.All(b => !b.IsEnabled && ToolTip.GetTip(b) is string), $"{uninstall.Count}");
            Check("提供的功能一行", All<TextBlock>().Count(t => t.IsEffectivelyVisible && t.Inlines?.OfType<global::Avalonia.Controls.Documents.Run>().FirstOrDefault()?.Text == "提供的功能：") == page.Plugins.Count);
            Shot("avalonia-plugins.png");
        }
        finally
        {
            Directory.Delete(testDir, true);
            if (createdRoot && !Directory.EnumerateFileSystemEntries(page.PluginsDirectory).Any()) Directory.Delete(page.PluginsDirectory);
        }

        page.Refresh();
        Check("删除后刷新", page.Plugins.All(p => p.Id != "m51.test"));
    }

    /// <summary>
    /// 悬浮小窗设置页（对应 WPF GuideWindowSettingsPage）：卡片与顺序、设置写入 GuideWindowConfig（下拉框按值选择）、
    /// 收藏与观看记录（Core GuideLibraryStore：进度文本、改标题、删除、清空确认）
    /// </summary>
    private async Task GuideAsync()
    {
        var page = vm.Guide;
        var lib = page.Library;
        if (!lib.IsFavorite("BV1m51test")) lib.ToggleFavorite("BV1m51test", "M51 收藏测试", "https://www.bilibili.com/video/BV1m51test");
        lib.UpdateProgress("BV1m51test", "M51 收藏测试", "https://www.bilibili.com/video/BV1m51test", 75, 600, 1.0);
        lib.UpdateProgress("BV1m51hist", "M51 记录测试", "https://www.bilibili.com/video/BV1m51hist", 30, 0, 1.0);
        vm.Navigate("悬浮小窗");
        await Settle(600);
        var headers = All<FluentAvalonia.UI.Controls.FASettingsExpander>().Where(e => e.IsEffectivelyVisible).Select(e => e.Header as string).ToList();
        Check("卡片与 WPF 相同", headers.SequenceEqual(["小窗", "主页", "外观与窗口", "鼠标穿透与交互", "自动显示与隐藏", "播放习惯", "字幕浮窗", "快捷键", "收藏与观看记录", "网页数据与快捷打开"]),
            string.Join(",", headers));
        Check("提示 Linux 不支持小窗", Named<Border>("UnsupportedBanner")?.IsEffectivelyVisible == true);
        var cfg = page.Config;
        await Settle(600); // 第二次：展开后才出现的卡片内控件
        Check("下拉框按值显示", (Named<ComboBox>("ZoomBox")?.SelectedItem as ViewModels.GuideOption)?.Value.Equals(cfg.ZoomFactor) == true
                              && (Named<ComboBox>("SeekBox")?.SelectedItem as ViewModels.GuideOption)?.Value.Equals(cfg.SeekSeconds) == true,
            $"{cfg.ZoomFactor} {cfg.SeekSeconds}");
        var zoom0 = cfg.ZoomFactor; var seek0 = cfg.SeekSeconds; var pos0 = cfg.PositionMode; var top0 = cfg.Topmost; var home0 = cfg.HomeUrl; var fade0 = cfg.SmartFadeEnabled;
        Named<ComboBox>("ZoomBox")!.SelectedIndex = 1;
        Named<ComboBox>("SeekBox")!.SelectedIndex = 3;
        Named<ComboBox>("PositionModeBox")!.SelectedIndex = 1;
        Named<ToggleSwitch>("TopmostToggle")!.IsChecked = !top0;
        await Settle();
        Check("设置写入配置", cfg.ZoomFactor == 0.8 && cfg.SeekSeconds == 15 && cfg.PositionMode == Core.Config.GuidePositionMode.Free && cfg.Topmost == !top0,
            $"{cfg.ZoomFactor} {cfg.SeekSeconds} {cfg.PositionMode} {cfg.Topmost}");
        cfg.SmartFadeEnabled = true;
        await Settle();
        var fadeVisible = Named<Slider>("SmartFadeSlider") is not null;
        cfg.SmartFadeEnabled = false;
        await Settle();
        Check("智能透明关闭时隐藏淡化不透明度", fadeVisible && Named<Slider>("SmartFadeSlider") is null);
        cfg.HomeUrl = "https://example.com/";
        await Click(Named<Button>("ResetHomeButton")!, "恢复 B 站首页");
        Check("恢复 B 站首页", cfg.HomeUrl == Core.Config.GuideWindowConfig.DefaultHomeUrl);
        cfg.ZoomFactor = zoom0; cfg.SeekSeconds = seek0; cfg.PositionMode = pos0; cfg.Topmost = top0; cfg.HomeUrl = home0; cfg.SmartFadeEnabled = fade0;

        // 收藏与观看记录
        var fav = page.Favorites.FirstOrDefault(f => f.Id is not null && f.Url.EndsWith("BV1m51test"));
        Check("收藏显示进度", fav?.Progress == "看到 01:15 / 10:00" || fav?.Progress.StartsWith("看到 1:15") == true, fav?.Progress);
        Check("观看记录", page.History.Any(h => h.Title == "M51 记录测试" && h.Progress.StartsWith("看到")) && page.LibrarySummary.Contains("收藏"), page.LibrarySummary);
        if (fav is not null)
        {
            fav.Title = "M51 改名";
            await Settle();
            Check("改标题写回收藏", lib.GetFavorites().Any(f => f.Title == "M51 改名"));
            page.RemoveFavoriteCommand.Execute(fav);
            await Settle(500);
            Check("删除收藏", !lib.GetFavorites().Any(f => f.VideoKey == "BV1m51test") && page.Favorites.All(f => !f.Url.EndsWith("BV1m51test")));
        }

        var hist = page.History.First(h => h.VideoKey == "BV1m51hist");
        page.RemoveHistoryCommand.Execute(hist);
        await Settle(500);
        Check("删除观看记录", lib.FindRecord("BV1m51hist") is null && page.History.All(h => h.VideoKey != "BV1m51hist"));
        lib.UpdateProgress("BV1m51hist2", "M51 记录二", "https://www.bilibili.com/video/BV1m51hist2", 10, 100, 1.0);
        await Settle(500);
        var clear = page.ClearHistoryCommand.ExecuteAsync(null);
        var dialog = await WaitFor(() => Services.FluentMessageBox.Current);
        Check("清空前确认", dialog is not null);
        Shot("avalonia-guide-confirm.png");
        dialog?.Hide(FluentAvalonia.UI.Controls.FAContentDialogResult.Primary);
        await clear;
        await Settle(500);
        Check("清空观看记录", lib.FindRecord("BV1m51hist2") is null && page.History.All(h => h.VideoKey != "BV1m51hist2"));
        lib.UpdateProgress("BV1m51shot", "M51 截图用记录", "https://www.bilibili.com/video/BV1m51shot", 42, 300, 1.0);
        await Settle(500);
        await Click(Named<Button>("OpenHotKeyButton")!, "前往快捷键页");
        Check("前往快捷键页", vm.SelectedNav?.Title == "快捷键", vm.SelectedNav?.Title);
        vm.Navigate("悬浮小窗");
        await Settle(600);
        Shot("avalonia-guide.png");
        lib.RemoveRecord("BV1m51shot");
        lib.Flush();
    }

    /// <summary>
    /// 自动演奏页（对应 WPF MusicPage）：选择曲谱目录、曲目解析与筛选、播放模式与输入方式写入配置；
    /// 功能性：在假游戏上播放键盘谱（按键顺序与数量）、暂停时不再发键、停止
    /// </summary>
    private async Task MusicAsync()
    {
        vm.Navigate("自动演奏");
        await Settle(600);
        var page = vm.Music;
        var music = page.Config.MusicConfig;
        Check("空曲库提示", Named<StackPanel>("EmptyState")?.IsEffectivelyVisible == true);
        var folder = Path.Combine(dir ?? Path.GetTempPath(), "music-lib");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "song1.json"), "{\"type\":\"keyboard\",\"name\":\"音阶一\",\"author\":\"测试甲\",\"bpm\":300,\"notes\":\" QWERTYU\"}");
        File.WriteAllText(Path.Combine(folder, "song2.json"), "{\"type\":\"keyboard\",\"name\":\"长曲二\",\"author\":\"测试乙\",\"bpm\":120,\"notes\":\" ASDFGHJASDFGHJ\"}");
        File.WriteAllText(Path.Combine(folder, "bad.json"), "{\"type\":\"unknown\",\"notes\":\"Q\"}");
        await page.SelectMusicFolderAsync(folder);
        await Settle(400);
        Check("载入曲目", page.AllMusicItems.Count == 3 && page.AllMusicItems.Count(x => x.IsValid) == 2, page.LibraryStatusText);
        Check("目录写入配置与历史", music.MusicFolder == folder && page.MusicFolderHistory.FirstOrDefault()?.FullPath == folder && page.MusicFolderDisplayText == "music-lib");
        Check("列表显示", Named<ListBox>("MusicList")?.IsEffectivelyVisible == true && Named<ListBox>("MusicList")!.ItemCount == 3);
        Check("无效曲谱显示错误", page.AllMusicItems.Any(x => !x.IsValid && !string.IsNullOrEmpty(x.Error)));
        Named<TextBox>("SearchBox")!.Text = "测试乙";
        await Settle();
        Check("搜索作者", page.MusicItems.Count == 1 && page.MusicItems[0].Name == "长曲二");
        Named<TextBox>("SearchBox")!.Text = "";
        await Settle();
        Check("格式筛选项", page.FormatFilters.Count >= 2 && page.FormatFilters[0] == "全部格式", string.Join(",", page.FormatFilters));
        var mode0 = page.PlaybackMode;
        await Click(Named<Button>("PlaybackModeButton")!, "播放模式");
        Check("切换播放模式写入配置", page.PlaybackMode != mode0 && music.PlaybackMode == page.PlaybackMode.ToString(), music.PlaybackMode);
        while (page.PlaybackMode != GameTask.Music.Model.MusicPlaybackMode.Sequential) page.CyclePlaybackModeCommand.Execute(null);
        Named<ToggleSwitch>("InputModeToggle")!.IsChecked = false;
        await Settle();
        Check("输入方式写入配置", music.InputMode == "ForegroundSendInput" && page.InputModeDisplayText == "前台");
        Named<ComboBox>("PlayModeBox")!.SelectedIndex = 1;
        await Settle();
        Check("引导式跟弹提示不支持", Named<ComboBox>("PlayModeBox")!.SelectedIndex == 0 && page.Message.Contains("暂不支持"), page.Message);
        var settingsButton = Named<Button>("MusicSettingsButton")!;
        settingsButton.Flyout!.ShowAt(settingsButton);
        await Settle(300);
        T? InFlyout<T>(string name) where T : Control => ((settingsButton.Flyout as Flyout)?.Content as Control)?.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name);
        var speedBox = InFlyout<NumericUpDown>("SpeedBox");
        Check("设置弹出：速度", speedBox?.Value == (decimal)music.Speed, speedBox?.Value?.ToString());
        Check("自定义 BPM 关闭时不可编辑", music.UseCustomBpm || InFlyout<NumericUpDown>("CustomBpmBox")?.IsEffectivelyEnabled == false);
        settingsButton.Flyout.Hide();
        await Settle();
        Check("播放模式图标", page.PlaybackModeGlyph != "\u25A1");
        Shot("avalonia-music.png");

        // —— 假游戏上的演奏 ——
        var runtime = await WaitFor(() => vm.Host?.RuntimeService.Current);
        Check("截图器已启动（假游戏）", runtime is not null);
        if (runtime is null || dir is null) return;
        var gameLog = Path.Combine(dir, "fakegame.jsonl");
        List<System.Text.Json.JsonElement> Events(long since) => File.ReadAllLines(gameLog).Select(l => System.Text.Json.JsonDocument.Parse(l).RootElement)
            .Where(e => e.GetProperty("t").GetInt64() >= since).ToList();
        long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        List<string> Downs(long since) => Events(since).Where(e => e.GetProperty("ev").GetString() == "key" && e.GetProperty("action").GetString() == "down"
            && !(e.TryGetProperty("repeat", out var r) && r.GetBoolean())).Select(e => e.GetProperty("key").GetString()!.ToUpperInvariant()).ToList();

        // 谱面开头留一拍休止：Core MusicPlaybackService 从“当前位置”之后的事件开始（开始时位置已略大于 0），时间为 0 的音符会被跳过（WPF 相同，见报告）
        var song1 = page.AllMusicItems.First(x => x.Name == "音阶一");
        var start = Now();
        await page.PlaySelectedCommand.ExecuteAsync(song1);
        var playing = await Until(() => page.IsPlaying, 5000);
        Check("开始播放", playing && page.CurrentTrackName.Contains("音阶一"), page.CurrentTrackName);
        // 顺序播放：第一首结束后续播第二首，第二首开始即停止
        await Until(() => page.CurrentTrackName.Contains("长曲二"), 8000);
        var keys1 = Downs(start);
        Check("假游戏按顺序收到 QWERTYU", string.Join("", keys1.Take(7)) == "QWERTYU", string.Join("", keys1));
        Check("顺序播放续播下一首", page.CurrentTrackName.Contains("长曲二"), page.CurrentTrackName);
        await Task.Delay(1200);
        await Click(Named<Button>("PlayPauseButton")!, "暂停");
        await Settle(300);
        Check("暂停", page.IsPaused && !page.IsPlaying);
        var pausedAt = Now();
        await Task.Delay(1200);
        Check("暂停时不发键", Downs(pausedAt).Count == 0, string.Join("", Downs(pausedAt)));
        await Click(Named<Button>("PlayPauseButton")!, "继续");
        await Task.Delay(1200);
        Check("继续后发键", page.IsPlaying && Downs(pausedAt).Count >= 1, string.Join("", Downs(pausedAt)));
        await Click(Named<Button>("StopButton")!, "停止");
        await Settle(500);
        var stoppedAt = Now();
        await Task.Delay(800);
        Check("停止", !page.IsPlaying && !page.IsPaused && Downs(stoppedAt).Count == 0 && page.CurrentTimeText == "00:00");
        var held = Events(start).Where(e => e.GetProperty("ev").GetString() == "key").GroupBy(e => e.GetProperty("key").GetString())
            .Where(g => g.Last().GetProperty("action").GetString() == "down").Select(g => g.Key).ToList();
        Check("没有残留按下的键", held.Count == 0, string.Join(",", held));
        Shot("avalonia-music-playing.png");
    }

    /// <summary>
    /// 辅助操控页（对应 WPF MacroSettingsPage）：卡片与顺序、一键领取奖励选项联动、绑定快捷键跳转、宏配置编辑（JSON 校验）；
    /// 功能性：在假游戏上长按空格 / F 连发、按住“那维莱特转圈圈”快捷键平移鼠标（读假游戏记录的按键与鼠标事件）
    /// </summary>
    private async Task MacroAsync()
    {
        vm.Navigate("辅助操控");
        await Settle(600);
        var page = vm.Macro;
        var macro = page.Config.MacroConfig;
        var headers = All<FluentAvalonia.UI.Controls.FASettingsExpander>().Where(e => e.IsEffectivelyVisible).Select(e => e.Header as string).ToList();
        Check("卡片与 WPF 相同", headers.SequenceEqual(["一键宏（按角色）", "一键领取奖励", "那维莱特 - 转圈圈", "快速强化圣遗物", "一键购买", "一键进出尘歌壶", "一键确认/取消",
            "长按空格等于连续按下空格", "长按 F 等于连续按下 F"]), string.Join(",", headers));
        Check("绑定快捷键按钮", All<Button>().Count(b => b.IsEffectivelyVisible && b.Content as string == "绑定快捷键") == 6);

        // 一键领取奖励：滚轮下滑仅在“按住持续”可用，幅度仅在滚轮下滑打开时可用
        macro.OneKeyClaimRewardHotkeyMode = GameTask.QuickClaimReward.OneKeyClaimRewardTask.ClickOnceMode;
        macro.OneKeyClaimRewardScrollDownEnabled = false;
        await Settle();
        Check("点按一次时滚轮下滑不可用", Named<ToggleSwitch>("ClaimRewardScrollToggle")?.IsEffectivelyEnabled == false);
        Named<ComboBox>("ClaimRewardModeBox")!.SelectedItem = GameTask.QuickClaimReward.OneKeyClaimRewardTask.HoldMode;
        await Settle();
        Check("按住持续时滚轮下滑可用", macro.OneKeyClaimRewardHotkeyMode == GameTask.QuickClaimReward.OneKeyClaimRewardTask.HoldMode
                                      && Named<ToggleSwitch>("ClaimRewardScrollToggle")?.IsEffectivelyEnabled == true
                                      && Named<NumericUpDown>("ClaimRewardScrollAmountBox")?.IsEffectivelyEnabled == false);
        Named<ToggleSwitch>("ClaimRewardScrollToggle")!.IsChecked = true;
        await Settle();
        Check("打开滚轮下滑后幅度可用", macro.OneKeyClaimRewardScrollDownEnabled && Named<NumericUpDown>("ClaimRewardScrollAmountBox")?.IsEffectivelyEnabled == true);
        Named<ComboBox>("CombatMacroModeBox")!.SelectedItem = GameTask.AutoFight.OneKeyFightTask.TickMode;
        Named<NumericUpDown>("EnhanceWaitDelayBox")!.Value = 120;
        await Settle();
        Check("设置写入配置", macro.CombatMacroHotkeyMode == GameTask.AutoFight.OneKeyFightTask.TickMode && macro.EnhanceWaitDelay == 120);

        // 宏配置编辑：格式错误不保存，正确时保存
        var path = ViewModels.MacroPageViewModel.AvatarMacroJsonPath();
        var original = File.ReadAllText(path);
        var editTask = page.EditAvatarMacroCommand.ExecuteAsync(null);
        var editor = await WaitFor(() => Services.JsonEditDialog.CurrentEditor);
        var dialog = Services.FluentMessageBox.Current;
        Check("宏配置编辑框显示文件内容", editor?.Text == original, path);
        Button? Primary() => dialog?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "PrimaryButton");
        if (editor is not null && dialog is not null)
        {
            editor.Text = "[ { 错误";
            await Click(Primary(), "保存（格式错误）");
            Check("格式错误时不保存不关闭", File.ReadAllText(path) == original && Services.JsonEditDialog.CurrentEditor is not null);
            var edited = original.TrimEnd() + "\n";
            editor.Text = edited;
            await Click(Primary(), "保存");
            await editTask;
            Check("保存宏配置", File.ReadAllText(path) == edited && Services.JsonEditDialog.CurrentEditor is null);
            File.WriteAllText(path, original);
        }

        // 绑定快捷键：转到快捷键页
        await Click(All<Button>().First(b => b.IsEffectivelyVisible && b.Content as string == "绑定快捷键"), "绑定快捷键");
        Check("绑定快捷键转到快捷键页", vm.SelectedNav?.Title == "快捷键" && vm.Settings.SelectedTab == 1, vm.SelectedNav?.Title);

        // —— 假游戏上的功能 ——
        var runtime = await WaitFor(() => vm.Host?.RuntimeService.Current);
        Check("截图器已启动（假游戏）", runtime is not null);
        if (runtime is null || dir is null) return;
        var gameLog = Path.Combine(dir, "fakegame.jsonl");
        List<System.Text.Json.JsonElement> Events(long since) => File.ReadAllLines(gameLog).Select(l => System.Text.Json.JsonDocument.Parse(l).RootElement)
            .Where(e => e.GetProperty("t").GetInt64() >= since).ToList();
        long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int KeyDowns(List<System.Text.Json.JsonElement> ev, string key) => ev.Count(e => e.GetProperty("ev").GetString() == "key"
            && string.Equals(e.GetProperty("key").GetString(), key, StringComparison.OrdinalIgnoreCase)
            && e.GetProperty("action").GetString() == "down" && !(e.TryGetProperty("repeat", out var r) && r.GetBoolean()));
        bool Xdo(string args)
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("xdotool", args) { UseShellExecute = false });
            proc?.WaitForExit(10000);
            return proc?.ExitCode == 0;
        }

        vm.Navigate("辅助操控");
        await Settle();
        foreach (var (toggle, key, field) in new[] { ("SpaceHoldToggle", "space", "Space"), ("FHoldToggle", "f", "F") })
        {
            Named<ToggleSwitch>(toggle)!.IsChecked = true;
            await Settle(300);
            Check($"{field} 连发监听已启用", page.HoldMonitor is not null, page.HoldMonitorStatus);
            var fired = page.HoldMonitor?.FiredCount ?? 0;
            var start = Now();
            Xdo($"keydown {key} sleep 1.5 keyup {key}");
            await Task.Delay(800);
            var ev = Events(start);
            var downs = KeyDowns(ev, field);
            var after = (page.HoldMonitor?.FiredCount ?? 0) - fired;
            Check($"长按 {field} 1.5 秒连续按下", downs >= 6 && after >= 5, $"假游戏按下 {downs} 次，连发 {after} 次");
            await Task.Delay(500);
            Check($"松开 {field} 后停止", KeyDowns(Events(start), field) == downs, $"{KeyDowns(Events(start), field)}");
            Named<ToggleSwitch>(toggle)!.IsChecked = false;
            await Settle(300);
        }

        Check("开关全部关闭后停止监听", page.HoldMonitor is null);
        var startOff = Now();
        Xdo("keydown space sleep 1 keyup space");
        await Task.Delay(500);
        Check("关闭时长按空格只按下一次", KeyDowns(Events(startOff), "Space") == 1, KeyDowns(Events(startOff), "Space").ToString());

        // 那维莱特转圈圈：按住快捷键时按配置平移鼠标
        Named<NumericUpDown>("RunaroundDistanceBox")!.Value = 40;
        Named<NumericUpDown>("RunaroundIntervalBox")!.Value = 20;
        await Settle();
        var settings = vm.Settings;
        settings.Hotkeys.First(h => h.Property == "TurnAroundHotkey").Value = "F9";
        settings.RegisterHotkeys();
        var item = settings.Hotkeys.First(h => h.Property == "TurnAroundHotkey");
        Check("注册转圈圈快捷键", item.IsSupported && item.Status.StartsWith("已注册"), item.Status);
        var startTurn = Now();
        Xdo("keydown F9 sleep 1 keyup F9");
        await Task.Delay(600);
        var moves = Events(startTurn).Where(e => e.GetProperty("ev").GetString() == "rawmouse").ToList();
        var dx = moves.Sum(e => e.GetProperty("rawdx").GetInt32());
        Check("按住快捷键时水平平移鼠标", moves.Count >= 10 && dx >= 400 && moves.All(e => e.GetProperty("rawdy").GetInt32() == 0), $"{moves.Count} 次，dx {dx}");
        var settled = Events(startTurn).Count(e => e.GetProperty("ev").GetString() == "rawmouse");
        await Task.Delay(500);
        Check("松开后停止平移", Events(startTurn).Count(e => e.GetProperty("ev").GetString() == "rawmouse") == settled);
        settings.Hotkeys.First(h => h.Property == "TurnAroundHotkey").Value = "";
        settings.RegisterHotkeys();
        Shot("avalonia-macro.png");
    }

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
