using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPick;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.ViewModel.Pages.Trigger;
using BetterGenshinImpact.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// “实时触发”页：直接绑定 Core 的 AllConfig（与 WPF 的 TriggerSettingsPageViewModel 绑定同一批配置属性），
/// 触发器每帧读取 IsEnabledByConfig，开关立即生效。名单规则、角色 CD 配置用 FAContentDialog（选项与读写在 Core TriggerSettingsChoices）
/// </summary>
public sealed partial class TriggerPageViewModel : PageViewModel
{
    [ObservableProperty] private bool _isBlacklistAutoPickMode;
    [ObservableProperty] private bool _isWhitelistAutoPickMode;

    public TriggerPageViewModel(AllConfig? config, QuestVoiceService? questVoice = null, IServiceProvider? services = null)
    {
        Config = config ?? new AllConfig();
        QuestVoice = config is not null && questVoice is not null ? new QuestVoiceCardViewModel(config, questVoice, services) : null;
        IsAvailable = config is not null;
        PickButtonNames = TriggerSettingsChoices.PickButtonNames(Config.AutoPickConfig);
        try
        {
            HangoutBranches = TriggerSettingsChoices.HangoutBranches();
        }
        catch
        {
            HangoutBranches = [];
        }

        UpdateAutoPickModeVisibility();
    }

    public override string Title => "实时触发";

    public AllConfig Config { get; }

    /// <summary>自动配音卡片（宿主初始化失败时为空，不显示）</summary>
    public QuestVoiceCardViewModel? QuestVoice { get; }

    /// <summary>配置来自正在运行的宿主（平台初始化失败时为占位配置，修改无效）</summary>
    public bool IsAvailable { get; }

    public List<string> PickButtonNames { get; }

    public string[] ClickChatOptionNames { get; } = TriggerSettingsChoices.ClickChatOptionNames;

    public string[] SelectChatOptionTypeNames { get; } = TriggerSettingsChoices.SelectChatOptionTypeNames;

    public string[] PickOcrEngineNames { get; } = TriggerSettingsChoices.PickOcrEngineNames;

    public List<string> HangoutBranches { get; }

    /// <summary>跳转页面（由主窗口设置，用于“快捷键”链接）</summary>
    public Action<string>? NavigateTo { get; set; }

    [RelayCommand]
    private void GoToHotkeys() => NavigateTo?.Invoke("快捷键");

    private static global::Avalonia.Controls.TextBox RuleBox(string name, string text, string watermark) => new()
    {
        Name = name, Text = text, Watermark = watermark, AcceptsReturn = true, TextWrapping = global::Avalonia.Media.TextWrapping.NoWrap,
        Height = 120, MinWidth = 460,
    };

    private static global::Avalonia.Controls.TextBlock Desc(string text, bool bold = false) => new()
    {
        Text = text, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, MaxWidth = 460, Opacity = bold ? 1 : 0.75,
        FontWeight = bold ? global::Avalonia.Media.FontWeight.SemiBold : global::Avalonia.Media.FontWeight.Normal, Margin = new global::Avalonia.Thickness(0, 6, 0, 2),
    };

    private static async Task<bool> ShowRulesDialog(string title, global::Avalonia.Controls.Control content)
    {
        var owner = Services.AvaloniaPickers.MainWindow;
        if (owner is null) return false;
        var dialog = new FluentAvalonia.UI.Controls.FAContentDialog
        {
            Title = title,
            Content = new global::Avalonia.Controls.ScrollViewer { MaxHeight = 520, Content = content },
            PrimaryButtonText = "保存", CloseButtonText = "取消", DefaultButton = FluentAvalonia.UI.Controls.FAContentDialogButton.Primary,
        };
        return await Services.FluentMessageBox.ShowCustomAsync(dialog, owner) == FluentAvalonia.UI.Controls.FAContentDialogResult.Primary;
    }

    private static void SaveRules(Action save)
    {
        try
        {
            save();
            // 名单文件已修改，自动拾取在下一次拾取时重读（与 WPF 相同）
            AutoPickTrigger.ReloadLists();
            UiHost.Dialogs.Toast(DialogKind.Success, "已保存，自动拾取规则立即刷新");
        }
        catch (Exception e)
        {
            UiHost.Dialogs.Toast(DialogKind.Error, AutoPickRuleFiles.SaveFailedText + e.Message);
        }
    }

    /// <summary>黑名单模式规则（对应 WPF AutoPickBlacklistConfigWindow）</summary>
    [RelayCommand]
    private async Task OpenBlacklistModeConfig()
    {
        var cfg = Config.AutoPickConfig;
        var doNot = RuleBox("BlacklistDoNotPickBox", AutoPickRuleFiles.Read(AutoPickRuleFiles.BlacklistDoNotPickPath), "每行一条记录，例如：精致的宝箱");
        var fuzzy = RuleBox("BlacklistFuzzyBox", AutoPickRuleFiles.Read(AutoPickRuleFiles.BlacklistFuzzyDoNotPickPath), "每行一个关键词，OCR 文本包含该关键词时不拾取");
        var pick = RuleBox("BlacklistPickBox", AutoPickRuleFiles.Read(AutoPickRuleFiles.BlacklistPickPath), "每行一条记录，例如：调查");
        var pickOn = new global::Avalonia.Controls.CheckBox { Name = "BlacklistPickEnabledBox", Content = "启用拾取规则", IsChecked = cfg.BlacklistModePickEnabled };
        var panel = new global::Avalonia.Controls.StackPanel { Name = "BlacklistRulesPanel", Spacing = 2 };
        panel.Children.Add(Desc("默认拾取未被排除的内容", true));
        panel.Children.Add(Desc("“拾取”规则优先级最高，可放行聊天/设置图标和“不拾取”规则中的同名内容。每行填写一条完整的 OCR 文本。"));
        panel.Children.Add(Desc("不拾取（主要规则）", true));
        panel.Children.Add(Desc("黑名单模式启用期间，默认、精确和模糊不拾取规则始终生效。"));
        panel.Children.Add(Desc("精确匹配"));
        panel.Children.Add(doNot);
        panel.Children.Add(Desc("模糊匹配"));
        panel.Children.Add(fuzzy);
        panel.Children.Add(Desc("拾取（优先放行）", true));
        panel.Children.Add(pickOn);
        panel.Children.Add(Desc("精确命中后直接拾取，并优先于图标排除和不拾取规则。"));
        panel.Children.Add(pick);
        panel.Children.Add(Desc("保存后立即刷新自动拾取规则。"));
        if (!await ShowRulesDialog("黑名单模式配置", panel)) return;
        SaveRules(() => AutoPickRuleFiles.SaveBlacklist(cfg, doNot.Text ?? "", fuzzy.Text ?? "", pick.Text ?? "", pickOn.IsChecked == true));
    }

    /// <summary>白名单模式规则（对应 WPF AutoPickWhitelistConfigWindow）</summary>
    [RelayCommand]
    private async Task OpenWhitelistModeConfig()
    {
        var cfg = Config.AutoPickConfig;
        var pick = RuleBox("WhitelistPickBox", AutoPickRuleFiles.Read(AutoPickRuleFiles.WhitelistPickPath), "每行一条需要额外拾取的完整 OCR 文本");
        var doNot = RuleBox("WhitelistDoNotPickBox", AutoPickRuleFiles.Read(AutoPickRuleFiles.WhitelistDoNotPickPath), "每行一条需要从最终拾取列表排除的完整 OCR 文本");
        var doNotOn = new global::Avalonia.Controls.CheckBox { Name = "WhitelistDoNotPickEnabledBox", Content = "启用不拾取规则", IsChecked = cfg.WhitelistModeDoNotPickEnabled };
        var panel = new global::Avalonia.Controls.StackPanel { Name = "WhitelistRulesPanel", Spacing = 2 };
        panel.Children.Add(Desc("仅拾取最终列表中的内容", true));
        panel.Children.Add(Desc("最终拾取列表为（默认拾取列表 ∪ 用户拾取列表）− 用户不拾取列表。聊天/设置图标及安全排除规则仍然优先。"));
        panel.Children.Add(Desc("拾取（基础列表）", true));
        panel.Children.Add(Desc("白名单模式启用期间，默认拾取列表和用户拾取列表始终生效。"));
        panel.Children.Add(pick);
        panel.Children.Add(Desc("不拾取（排除项）", true));
        panel.Children.Add(doNotOn);
        panel.Children.Add(Desc("精确命中的内容会从默认和用户拾取列表中排除。"));
        panel.Children.Add(doNot);
        panel.Children.Add(Desc("保存后立即刷新自动拾取规则。"));
        if (!await ShowRulesDialog("白名单模式配置", panel)) return;
        SaveRules(() => AutoPickRuleFiles.SaveWhitelist(cfg, pick.Text ?? "", doNot.Text ?? "", doNotOn.IsChecked == true));
    }

    /// <summary>编辑中的角色 CD 规则（对话框打开期间）</summary>
    public System.Collections.ObjectModel.ObservableCollection<BetterGenshinImpact.GameTask.SkillCd.SkillCdRule>? EditingSkillCdRules { get; private set; }

    /// <summary>角色 CD 配置（对应 WPF SkillCdConfigWindow）：角色名 + 默认 CD，保存时只保留填写了角色名的行</summary>
    [RelayCommand]
    private async Task EditSkillCdConfig()
    {
        var rules = TriggerSettingsChoices.CopySkillCdRules(Config.SkillCdConfig.CustomCdList);
        EditingSkillCdRules = rules;
        var list = new global::Avalonia.Controls.StackPanel { Name = "SkillCdRulesPanel", Spacing = 6 };
        void AddRow(BetterGenshinImpact.GameTask.SkillCd.SkillCdRule rule)
        {
            var row = new global::Avalonia.Controls.StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
            var name = new global::Avalonia.Controls.TextBox { Width = 180, Watermark = "角色名", Text = rule.RoleName };
            name.TextChanged += (_, _) => rule.RoleName = name.Text ?? "";
            var cd = new global::Avalonia.Controls.TextBox { Width = 120, Watermark = "CD（秒）", Text = rule.CdValueText };
            cd.TextChanged += (_, _) => rule.CdValueText = cd.Text ?? "";
            var del = new global::Avalonia.Controls.Button { Content = "删除" };
            del.Click += (_, _) => { rules.Remove(rule); list.Children.Remove(row); };
            row.Children.Add(name);
            row.Children.Add(cd);
            row.Children.Add(del);
            list.Children.Add(row);
        }
        foreach (var r in rules) AddRow(r);
        var add = new global::Avalonia.Controls.Button { Name = "SkillCdAddRuleButton", Content = "添加" };
        add.Click += (_, _) => { var r = new BetterGenshinImpact.GameTask.SkillCd.SkillCdRule(); rules.Add(r); AddRow(r); };
        var panel = new global::Avalonia.Controls.StackPanel { Spacing = 8, MinWidth = 420 };
        panel.Children.Add(Desc("为指定的角色配置未识别到 CD 数字时的默认值。如玛薇卡开 E 会显示摩托车，识别不到CD。"));
        panel.Children.Add(list);
        panel.Children.Add(add);
        var ok = await ShowRulesDialog("角色 CD 配置", panel);
        EditingSkillCdRules = null;
        if (!ok) return;
        // 技能 CD 每次都实时读取 CustomCdList，不需要刷新触发器（与 WPF 相同）
        Config.SkillCdConfig.CustomCdList = TriggerSettingsChoices.ValidSkillCdRules(rules);
    }

    [RelayCommand]
    private void SetAutoPickMode(AutoPickMode mode)
    {
        // 拾取模式是名单缓存键的一部分，自动拾取下一次拾取时按新模式重读名单（与 WPF 相同）
        Config.AutoPickConfig.Mode = mode;
        UpdateAutoPickModeVisibility();
    }

    private void UpdateAutoPickModeVisibility()
    {
        IsBlacklistAutoPickMode = Config.AutoPickConfig.Mode == AutoPickMode.Blacklist;
        IsWhitelistAutoPickMode = Config.AutoPickConfig.Mode == AutoPickMode.Whitelist;
    }
}
