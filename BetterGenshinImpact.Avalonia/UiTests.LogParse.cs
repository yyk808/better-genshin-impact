using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using BetterGenshinImpact.Avalonia.ViewModels;
using BetterGenshinImpact.Avalonia.Views;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.LogParse;
using FluentAvalonia.UI.Controls;

namespace BetterGenshinImpact.Avalonia;

internal sealed partial class UiTests
{
    /// <summary>
    /// 调度器“更多功能 → 日志分析”（--ui-test log-parse，同 WPF OpenLogParse）：菜单项可用；选项对话框的控件与默认值；
    /// “?”打开 Cookie 获取说明；确定后按配置组保存选项、解析日志并在“日志分析”窗口中显示（当前配置组 / 所有）；
    /// 未填 Cookie 时提示不启用锄地统计；填写后先从米游社更新（自测换成假数据，不联网）并缓存角色、同步到米游社设置；
    /// 获取失败时提示；取消不保存；没有日志时提示“未解析出日志记录！”。
    /// 日志放在临时目录；结束时恢复 log/logparse/config.json 与米游社 Cookie 设置
    /// </summary>
    private async Task LogParseAsync()
    {
        vm.Navigate("调度器");
        await Settle(600);
        var page = vm.Auto.Scheduler;
        page.Reload();
        await Settle();
        var group = page.SelectedScriptGroup ?? page.ScriptGroups.FirstOrDefault();
        Check("有配置组", group is not null);
        if (group is null) return;
        page.SelectedScriptGroup = group;
        await Settle();

        var tmp = Path.Combine(Path.GetTempPath(), $"bgi-logparse-{Environment.ProcessId}");
        var logs = Path.Combine(tmp, "log");
        var empty = Path.Combine(tmp, "empty");
        Directory.CreateDirectory(logs);
        Directory.CreateDirectory(empty);
        var day = DateTime.Now;
        File.WriteAllLines(Path.Combine(logs, $"better-genshin-impact{day:yyyyMMdd}.log"),
        [
            "[10:00:00.000] [INF] BetterGenshinImpact.Service.ScriptService", $"配置组 \"{group.Name}\" 加载完成，共1个脚本，开始执行",
            "[10:00:01.000] [INF] BetterGenshinImpact.Service.ScriptService", "→ 开始执行地图追踪任务: \"m64路线甲\"",
            "[10:00:30.000] [INF] BetterGenshinImpact.GameTask.AutoPathing.PathExecutor", "前往七天神像复活",
            "[10:05:00.000] [INF] BetterGenshinImpact.Service.ScriptService", "→ 脚本执行结束: \"m64路线甲\"",
            "[10:05:01.000] [INF] BetterGenshinImpact.Service.ScriptService", $"配置组 \"{group.Name}\" 执行结束",
            "[11:00:00.000] [INF] BetterGenshinImpact.Service.ScriptService", "配置组 \"m64其他组\" 加载完成，共1个脚本，开始执行",
            "[11:00:01.000] [INF] BetterGenshinImpact.Service.ScriptService", "→ 开始执行JS脚本: \"m64脚本乙\"",
            "[11:02:00.000] [INF] BetterGenshinImpact.Service.ScriptService", "→ 脚本执行结束: \"m64脚本乙\"",
            "[11:02:01.000] [INF] BetterGenshinImpact.Service.ScriptService", "配置组 \"m64其他组\" 执行结束",
        ]);

        var configPath = Global.Absolute(@"log\logparse\config.json");
        var logparseDir = Path.GetDirectoryName(configPath)!;
        var createdDir = !Directory.Exists(logparseDir);
        var config0 = File.Exists(configPath) ? File.ReadAllBytes(configPath) : null;
        var mcfg = ConfigHost.Config!.OtherConfig.MiyousheConfig;
        var (cookie0, sync0) = (mcfg.Cookie, mcfg.LogSyncCookie);
        var dir0 = ScriptControlPageViewModel.LogParseDirectory;
        var fetch0 = ScriptControlPageViewModel.FetchTravelsDiary;
        var fetched = new List<string>();
        var fail = false;
        ScriptControlPageViewModel.LogParseDirectory = logs;
        ScriptControlPageViewModel.FetchTravelsDiary = c =>
        {
            fetched.Add(c);
            return fail
                ? Task.FromException<GameInfo>(new HttpRequestException("假服务不可用"))
                : Task.FromResult(new GameInfo { GameUid = "m64fake01", Nickname = "测试", Region = "cn_gf01", GameBiz = "hk4e_cn", Level = 60 });
        };
        var toasts = new List<string>();
        void OnToast(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(vm.ToastText) && vm.ToastText is { Length: > 0 } t) toasts.Add(t);
        }

        vm.PropertyChanged += OnToast;
        mcfg.Cookie = "";
        mcfg.LogSyncCookie = true;
        if (File.Exists(configPath)) File.Delete(configPath);
        try
        {
            var more = await WaitFor(() => Named<Button>("GroupMoreButton"));
            more?.Flyout?.ShowAt(more!);
            await Settle(400);
            var item = (more?.Flyout as MenuFlyout)?.Items.OfType<MenuItem>().FirstOrDefault(i => i.Header as string == "日志分析");
            Check("“日志分析”菜单项可用（同 WPF）", item is { IsEnabled: true, Command: not null });
            more?.Flyout?.Hide();

            // 1. 选项对话框；“?”说明；当前配置组、全部天数、异常统计、锄地但未填 Cookie
            var run = page.OpenLogParseCommand.ExecuteAsync(null);
            var dialog = await WaitFor(() => ScriptControlPageViewModel.CurrentLogParseDialog);
            await Settle(300);
            T? C<T>(string name) where T : Control => (dialog?.Content as Panel)?.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Name == name);
            var range = C<ComboBox>("LogParseRangeBox");
            var days = C<ComboBox>("LogParseDayRangeBox");
            var delay = C<TextBox>("LogParseHoeingDelayBox");
            var cookieBox = C<TextBox>("LogParseCookieBox");
            Check("对话框“日志分析”：确定 / 取消", dialog?.Title as string == "日志分析" && dialog.PrimaryButtonText == "确定" && dialog.CloseButtonText == "取消");
            Check("默认：当前配置组、7天、三个开关关闭、锄地延时 0（同 WPF）",
                range?.SelectedItem?.ToString() == "当前配置组" && days?.SelectedItem?.ToString() == "7天" && delay?.Text == "0"
                && new[] { "LogParseMergeBox", "LogParseFaultBox", "LogParseHoeingBox" }.All(n => C<CheckBox>(n) is { IsChecked: false }),
                $"{range?.SelectedItem} {days?.SelectedItem} {delay?.Text}");
            Check("范围与天数选项同 WPF", range?.Items.Cast<object>().Select(o => o.ToString()).SequenceEqual(["当前配置组", "所有"]) == true
                && days?.Items.Cast<object>().Select(o => o.ToString()).SequenceEqual(["1天", "3天", "7天", "15天", "31天", "61天", "92天", "所有"]) == true);
            Shot("avalonia-log-parse-options.png");
            C<Button>("LogParseHelpButton")?.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            var helpWin = await WaitFor(() => LogParseWindow.Current);
            Check("“?”打开米游社 Cookie 获取说明", helpWin is { Title: "日志分析" } && helpWin.Html.Contains("如何获取米游社Cookie"), helpWin?.Html.Length.ToString());
            helpWin?.Close();
            await Settle();
            days!.SelectedIndex = 7;
            C<CheckBox>("LogParseFaultBox")!.IsChecked = true;
            C<CheckBox>("LogParseHoeingBox")!.IsChecked = true;
            delay!.Text = "5";
            dialog!.Hide(FAContentDialogResult.Primary);
            var win = await WaitFor(() => LogParseWindow.Current);
            await Settle(800);
            Check("确定后显示“日志分析”窗口：只含当前配置组的记录", win is { Title: "日志分析" } && win.Html.Contains(group.Name) && win.Html.Contains("m64路线甲") && !win.Html.Contains("m64其他组"),
                win?.Html.Length.ToString() ?? string.Join(" | ", toasts));
            Check("窗口为模态（同 WPF ShowDialog）", !run.IsCompleted);
            if (win is null) return;
            await Settle(1500);
            _ = ShotX11(win, "log-parse-result.png"); // 网页为原生控件，仅在 X11 上截图留档（Wayland 上不可用，不作为检查项）
            win?.Close();
            await run;
            await Settle();
            Check("未填写 Cookie 时提示并不访问米游社", toasts.Contains("未填写cookie，此次将不启用锄地统计！") && fetched.Count == 0, string.Join(" | ", toasts));
            var saved = LogParse.LoadConfig();
            Check("选项按配置组保存到 log/logparse/config.json", saved.ScriptGroupLogDictionary.TryGetValue(group.Name, out var s1)
                && s1 is { DayRangeValue: "All", RangeValue: "CurrentConfig", FaultStatsSwitch: true, HoeingStatsSwitch: true, MergerStatsSwitch: false, HoeingDelay: "5" });

            // 2. 所有配置组 + Cookie：先更新旅行札记（假数据），缓存角色并同步到米游社设置
            toasts.Clear();
            run = page.OpenLogParseCommand.ExecuteAsync(null);
            dialog = await WaitFor(() => ScriptControlPageViewModel.CurrentLogParseDialog);
            await Settle(300);
            Check("再次打开时沿用已保存的选项", C<ComboBox>("LogParseDayRangeBox")?.SelectedItem?.ToString() == "所有" && C<TextBox>("LogParseHoeingDelayBox")?.Text == "5");
            C<ComboBox>("LogParseRangeBox")!.SelectedIndex = 1;
            C<TextBox>("LogParseCookieBox")!.Text = "m64cookie";
            dialog!.Hide(FAContentDialogResult.Primary);
            win = await WaitFor(() => LogParseWindow.Current);
            await Settle(800);
            Check("范围“所有”包含其他配置组", win is not null && win.Html.Contains(group.Name) && win.Html.Contains("m64其他组"));
            win?.Close();
            await run;
            await Settle();
            saved = LogParse.LoadConfig();
            Check("填写 Cookie 后先从米游社更新（假服务）并缓存角色", fetched.SequenceEqual(["m64cookie"]) && saved.CookieDictionary.TryGetValue("m64cookie", out var gi) && gi.GameUid == "m64fake01"
                && toasts.Contains("米游社数据获取成功，开始进行解析，请耐心等待！"), string.Join(" | ", toasts));
            Check("Cookie 同步到米游社设置（LogSyncCookie）", mcfg.Cookie == "m64cookie" && saved.Cookie == "m64cookie");

            // 3. 获取失败：有缓存时沿用旧数据
            toasts.Clear();
            fail = true;
            run = page.OpenLogParseCommand.ExecuteAsync(null);
            dialog = await WaitFor(() => ScriptControlPageViewModel.CurrentLogParseDialog);
            Check("Cookie 从米游社设置带入", C<TextBox>("LogParseCookieBox")?.Text == "m64cookie");
            dialog!.Hide(FAContentDialogResult.Primary);
            win = await WaitFor(() => LogParseWindow.Current);
            await Settle(500);
            win?.Close();
            await run;
            await Settle();
            Check("获取失败时提示并沿用已缓存的数据（同 WPF）", toasts.Any(t => t.StartsWith("访问米游社失败：假服务不可用，此次锄地统计不更新最新数据")), string.Join(" | ", toasts));

            // 4. 取消：不保存
            var before = File.ReadAllText(configPath);
            run = page.OpenLogParseCommand.ExecuteAsync(null);
            dialog = await WaitFor(() => ScriptControlPageViewModel.CurrentLogParseDialog);
            C<TextBox>("LogParseHoeingDelayBox")!.Text = "99";
            dialog!.Hide(FAContentDialogResult.None);
            await run;
            Check("取消不保存、不打开窗口", File.ReadAllText(configPath) == before && LogParseWindow.Current is null);

            // 5. 没有日志
            toasts.Clear();
            ScriptControlPageViewModel.LogParseDirectory = empty;
            run = page.OpenLogParseCommand.ExecuteAsync(null);
            dialog = await WaitFor(() => ScriptControlPageViewModel.CurrentLogParseDialog);
            C<CheckBox>("LogParseHoeingBox")!.IsChecked = false;
            dialog!.Hide(FAContentDialogResult.Primary);
            await run;
            await Settle();
            Check("没有日志时提示“未解析出日志记录！”", toasts.Contains("未解析出日志记录！") && LogParseWindow.Current is null, string.Join(" | ", toasts));
        }
        finally
        {
            vm.PropertyChanged -= OnToast;
            ScriptControlPageViewModel.CurrentLogParseDialog?.Hide();
            LogParseWindow.Current?.Close();
            ScriptControlPageViewModel.LogParseDirectory = dir0;
            ScriptControlPageViewModel.FetchTravelsDiary = fetch0;
            (mcfg.Cookie, mcfg.LogSyncCookie) = (cookie0, sync0);
            if (config0 is not null) File.WriteAllBytes(configPath, config0);
            else if (File.Exists(configPath)) File.Delete(configPath);
            if (createdDir && Directory.Exists(logparseDir) && Directory.GetFileSystemEntries(logparseDir).Length == 0) Directory.Delete(logparseDir);
            Directory.Delete(tmp, recursive: true);
            await Settle();
        }
    }
}
