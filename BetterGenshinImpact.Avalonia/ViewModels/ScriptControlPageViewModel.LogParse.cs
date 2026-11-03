using Avalonia.Controls;
using Avalonia.Layout;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.LogParse;
using BetterGenshinImpact.Platform;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using Thickness = Avalonia.Thickness;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// 调度器“更多功能 → 日志分析”（对照 WPF ScriptControlViewModel.OpenLogParse）：选项对话框（范围、天数、合并、异常统计、锄地统计、
/// 米游社 Cookie 与说明、锄地延时）按配置组保存到 log/logparse/config.json；解析 log/ 下的 BetterGI 日志，
/// 由 Core LogParse 生成 HTML 并在网页窗口中显示；开启锄地统计且填写 Cookie 时先从米游社更新旅行札记
/// </summary>
public sealed partial class ScriptControlPageViewModel
{
    /// <summary>解析的日志目录（界面自测替换为临时目录）</summary>
    public static string LogParseDirectory { get; set; } = Global.Absolute("log");

    /// <summary>从米游社更新旅行札记并返回角色（界面自测替换为假数据，不联网）</summary>
    public static Func<string, Task<GameInfo>> FetchTravelsDiary { get; set; } = TravelsDiaryDetailManager.UpdateTravelsDiaryDetailManager;

    /// <summary>当前打开的日志分析选项对话框（界面自测用）</summary>
    public static FAContentDialog? CurrentLogParseDialog { get; private set; }

    private sealed record LogParseOption(string Text, string Value)
    {
        public override string ToString() => Text;
    }

    private static readonly LogParseOption[] LogParseRanges = [new("当前配置组", "CurrentConfig"), new("所有", "All")];

    private static readonly LogParseOption[] LogParseDayRanges =
    [
        new("1天", "1"), new("3天", "3"), new("7天", "7"), new("15天", "15"), new("31天", "31"), new("61天", "61"), new("92天", "92"), new("所有", "All"),
    ];

    [RelayCommand]
    private async Task OpenLogParseAsync()
    {
        if (SelectedScriptGroup is not { } group || Services.AvaloniaPickers.MainWindow is not { } owner) return;
        var config = LogParse.LoadConfig();
        var mcfg = ConfigHost.Config?.OtherConfig.MiyousheConfig;
        if (mcfg is { LogSyncCookie: true } && !string.IsNullOrEmpty(mcfg.Cookie))
        {
            config.Cookie = mcfg.Cookie;
        }

        GameInfo? gameInfo = null;
        if (!string.IsNullOrEmpty(config.Cookie))
        {
            config.CookieDictionary.TryGetValue(config.Cookie, out gameInfo);
        }

        if (!config.ScriptGroupLogDictionary.TryGetValue(group.Name, out var sgpc))
        {
            sgpc = new LogParseConfig.ScriptGroupLogParseConfig();
        }

        // 选项（同 WPF 控件顺序与默认值）
        var range = new ComboBox { Name = "LogParseRangeBox", Width = 200, Margin = new Thickness(0, 0, 0, 10), ItemsSource = LogParseRanges };
        range.SelectedItem = LogParseRanges.FirstOrDefault(o => o.Value == sgpc.RangeValue) ?? LogParseRanges[0];
        var dayRange = new ComboBox { Name = "LogParseDayRangeBox", Width = 200, Margin = new Thickness(0, 0, 0, 10), ItemsSource = LogParseDayRanges };
        dayRange.SelectedItem = LogParseDayRanges.FirstOrDefault(o => o.Value == sgpc.DayRangeValue) ?? LogParseDayRanges[0];
        var merge = new CheckBox { Name = "LogParseMergeBox", Content = "合并相邻同名配置组", IsChecked = sgpc.MergerStatsSwitch };
        var fault = new CheckBox { Name = "LogParseFaultBox", Content = "异常情况统计", IsChecked = sgpc.FaultStatsSwitch };
        var hoeing = new CheckBox { Name = "LogParseHoeingBox", Content = "统计锄地摩拉怪物数", IsChecked = sgpc.HoeingStatsSwitch };
        var cookie = new TextBox { Name = "LogParseCookieBox", Width = 200, Margin = new Thickness(0, 0, 10, 0), Text = config.Cookie };
        var help = new Button { Name = "LogParseHelpButton", Content = "?", Width = 30, Height = 30 };
        ToolTip.SetTip(help, "如何获取米游社 Cookie");
        var delay = new TextBox { Name = "LogParseHoeingDelayBox", Width = 100, Text = sgpc.HoeingDelay, VerticalContentAlignment = VerticalAlignment.Center };
        var panel = new StackPanel { Margin = new Thickness(10), Spacing = 2 };
        panel.Children.Add(range);
        panel.Children.Add(dayRange);
        panel.Children.Add(merge);
        panel.Children.Add(fault);
        panel.Children.Add(hoeing);
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10), Children = { cookie, help } });
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10),
            Children = { new TextBlock { Text = "锄地延时(秒)：", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) }, delay },
        });
        var dialog = new FAContentDialog
        {
            Title = "日志分析", Content = panel, PrimaryButtonText = "确定", CloseButtonText = "取消", DefaultButton = FAContentDialogButton.Primary,
        };
        help.Click += async (_, _) => await Views.LogParseWindow.ShowAsync("日志分析", TravelsDiaryDetailManager.generHtmlMessage(), owner, modal: false);

        CurrentLogParseDialog = dialog;
        FAContentDialogResult result;
        try
        {
            result = await Services.FluentMessageBox.ShowCustomAsync(dialog, owner);
        }
        finally
        {
            CurrentLogParseDialog = null;
        }

        if (result != FAContentDialogResult.Primary) return;
        var rangeValue = (range.SelectedItem as LogParseOption ?? LogParseRanges[0]).Value;
        var dayRangeValue = (dayRange.SelectedItem as LogParseOption ?? LogParseDayRanges[0]).Value;
        var cookieValue = cookie.Text ?? "";
        var hoeingOn = hoeing.IsChecked ?? false;

        // 保存配置文件
        sgpc.DayRangeValue = dayRangeValue;
        sgpc.RangeValue = rangeValue;
        sgpc.HoeingStatsSwitch = hoeingOn;
        sgpc.FaultStatsSwitch = fault.IsChecked ?? false;
        sgpc.MergerStatsSwitch = merge.IsChecked ?? false;
        sgpc.HoeingDelay = delay.Text ?? "";
        config.Cookie = cookieValue;
        config.ScriptGroupLogDictionary[group.Name] = sgpc;
        if (mcfg is { LogSyncCookie: true } && !string.IsNullOrEmpty(cookieValue))
        {
            mcfg.Cookie = cookieValue;
        }

        LogParse.WriteConfigFile(config);

        void OnStatus(string status) => UiHost.Dialogs.Toast(DialogKind.Information, status);
        LogParse.HtmlGenerationStatusChanged += OnStatus;
        try
        {
            UiHost.Dialogs.Toast(DialogKind.Information, "正在准备数据...");
            var files = LogParse.GetLogFiles(LogParseDirectory);
            if (dayRangeValue != "All" && int.TryParse(dayRangeValue, out var n) && n < files.Count)
            {
                files = files.GetRange(files.Count - n, n);
            }

            if (hoeingOn && string.IsNullOrEmpty(cookieValue))
            {
                UiHost.Dialogs.Toast(DialogKind.Warning, "未填写cookie，此次将不启用锄地统计！");
            }

            // 真正使用的角色：先用缓存，获取成功后更新
            var realGameInfo = gameInfo;
            if (hoeingOn && !string.IsNullOrEmpty(cookieValue))
            {
                try
                {
                    UiHost.Dialogs.Toast(DialogKind.Information, "正在从米游社获取旅行札记数据，请耐心等待！");
                    gameInfo = await FetchTravelsDiary(cookieValue);
                    UiHost.Dialogs.Toast(DialogKind.Success, "米游社数据获取成功，开始进行解析，请耐心等待！");
                }
                catch (Exception e)
                {
                    _log.Warning(e, "日志分析获取米游社数据失败：{Msg}", e.Message);
                    UiHost.Dialogs.Toast(DialogKind.Warning, realGameInfo is not null
                        ? $"访问米游社失败：{e.Message}，此次锄地统计不更新最新数据"
                        : $"访问米游社失败：{e.Message}，此次不启用锄地统计");
                }
            }

            if (gameInfo is not null)
            {
                realGameInfo = gameInfo;
                config.CookieDictionary[cookieValue] = realGameInfo;
                LogParse.WriteConfigFile(config);
            }

            var hoeingStats = hoeingOn && realGameInfo is not null;
            var groupName = group.Name;
            // 解析与生成在后台线程进行（WPF 在界面线程），日志多时界面不卡住
            var html = await Task.Run(() =>
            {
                var entities = LogParse.ParseFile(files);
                if (rangeValue == "CurrentConfig")
                {
                    entities = entities.Where(item => item.Name == groupName).ToList();
                }

                if (entities.Count == 0) return null;
                entities.Reverse();
                return LogParse.GenerHtmlByConfigGroupEntity(entities, hoeingStats ? realGameInfo : null, sgpc);
            });
            if (html is null)
            {
                UiHost.Dialogs.Toast(DialogKind.Warning, "未解析出日志记录！");
                return;
            }

            if (await Views.LogParseWindow.ShowAsync("日志分析", html, owner, modal: true) is { } reason)
            {
                UiHost.Dialogs.Toast(DialogKind.Error, reason);
            }
        }
        catch (Exception ex)
        {
            UiHost.Dialogs.Toast(DialogKind.Error, $"生成日志分析时出错: {ex.Message}");
        }
        finally
        {
            LogParse.HtmlGenerationStatusChanged -= OnStatus;
        }
    }
}
