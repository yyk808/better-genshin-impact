using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPick;
using BetterGenshinImpact.GameTask.AutoSkip;
using BetterGenshinImpact.GameTask.AutoSkip.Assets;
using BetterGenshinImpact.GameTask.AutoSkip.Model;
using BetterGenshinImpact.GameTask.SkillCd;

namespace BetterGenshinImpact.ViewModel.Pages.Trigger;

/// <summary>
/// 实时触发页的选项与非 UI 逻辑（WPF TriggerSettingsPage 与 Avalonia 实时触发页共用）
/// </summary>
public static class TriggerSettingsChoices
{
    public static string[] ClickChatOptionNames => ["优先选择第一个选项", "随机选择选项", "优先选择最后一个选项", "不选择选项"];

    public static string[] SelectChatOptionTypeNames => [SelectChatOptionTypes.UseMouse, SelectChatOptionTypes.UseInteractionKey];

    public static string[] PickOcrEngineNames => [PickOcrEngineEnum.Paddle.ToString(), PickOcrEngineEnum.Yap.ToString()];

    /// <summary>画中画画面来源（值 → 显示名）</summary>
    public static Dictionary<string, string> PictureInPictureSourceTypes => new()
    {
        { nameof(PictureSourceType.CaptureLoop), "60帧模式" },
        { nameof(PictureSourceType.TriggerDispatcher), "截图器供图" }
    };

    /// <summary>邀约分支</summary>
    public static List<string> HangoutBranches() => HangoutConfig.Instance.HangoutOptionsTitleList;

    /// <summary>拾取按键：F/E/G，配置里的单个大写字母自定义键追加在后</summary>
    public static List<string> PickButtonNames(AutoPickConfig config)
    {
        var names = new List<string> { "F", "E", "G" };
        if (!string.IsNullOrEmpty(config.PickKey)
            && config.PickKey.Length == 1
            && char.IsUpper(config.PickKey[0])
            && !names.Contains(config.PickKey))
        {
            names.Add(config.PickKey);
        }

        return names;
    }

    /// <summary>编辑用的角色 CD 规则副本（为空时给一行空规则）</summary>
    public static ObservableCollection<SkillCdRule> CopySkillCdRules(IEnumerable<SkillCdRule>? rules)
    {
        var copy = new ObservableCollection<SkillCdRule>();
        if (rules != null)
        {
            foreach (var rule in rules)
            {
                copy.Add(new SkillCdRule { RoleName = rule.RoleName, CdValueText = rule.CdValueText });
            }
        }

        if (copy.Count == 0)
        {
            copy.Add(new SkillCdRule());
        }

        return copy;
    }

    /// <summary>保存时只保留填写了角色名的规则，并去掉首尾空白</summary>
    public static List<SkillCdRule> ValidSkillCdRules(IEnumerable<SkillCdRule> rules) =>
        rules.Where(r => !string.IsNullOrWhiteSpace(r.RoleName))
            .Select(r => new SkillCdRule { RoleName = r.RoleName.Trim(), CdValueText = r.CdValueText?.Trim() })
            .ToList();
}

/// <summary>自动拾取黑 / 白名单模式的名单文件（WPF 名单配置窗口与 Avalonia 名单对话框共用）</summary>
public static class AutoPickRuleFiles
{
    public const string BlacklistDoNotPickPath = @"User\pick_black_lists.txt";
    public const string BlacklistFuzzyDoNotPickPath = @"User\pick_fuzzy_black_lists.txt";
    public const string BlacklistPickPath = @"User\pick_white_lists.txt";
    public const string WhitelistPickPath = @"User\pick_whitelist_mode_pick_lists.txt";
    public const string WhitelistDoNotPickPath = @"User\pick_whitelist_mode_do_not_pick_lists.txt";

    public const string SaveFailedText = "保存自动拾取名单配置失败：";

    public static string Read(string path) => Global.ReadAllTextIfExist(path) ?? string.Empty;

    /// <summary>保存黑名单模式名单（调用方随后调用 AutoPickTrigger.ReloadLists，下一次拾取时重读）</summary>
    public static void SaveBlacklist(AutoPickConfig config, string doNotPick, string fuzzyDoNotPick, string pick, bool pickEnabled)
    {
        Global.WriteAllText(BlacklistDoNotPickPath, doNotPick);
        Global.WriteAllText(BlacklistFuzzyDoNotPickPath, fuzzyDoNotPick);
        Global.WriteAllText(BlacklistPickPath, pick);
        config.BlacklistModePickEnabled = pickEnabled;
    }

    /// <summary>保存白名单模式名单</summary>
    public static void SaveWhitelist(AutoPickConfig config, string pick, string doNotPick, bool doNotPickEnabled)
    {
        Global.WriteAllText(WhitelistPickPath, pick);
        Global.WriteAllText(WhitelistDoNotPickPath, doNotPick);
        config.WhitelistModeDoNotPickEnabled = doNotPickEnabled;
    }
}
