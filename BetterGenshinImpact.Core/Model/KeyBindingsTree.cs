using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Model;

/// <summary>
/// 按键绑定设置页的树（动作 / 菜单两组），WPF KeyBindingsSettingsPage 与 Avalonia 按键绑定页共用
/// </summary>
public static class KeyBindingsTree
{
    /// <summary>页头说明（与 WPF 相同）</summary>
    public const string Description = "此处的按键设置默认对除自动拾取、钓鱼、音游、伐木外的内置功能有效，在设置中打开“启用全局按键映射”开关可对JS脚本、战斗脚本和地图追踪内包含的简易策略脚本生效。请手动设置拾取按键、星之归还按键、隐藏主界面按键。";

    /// <summary>按配置构造按键绑定树</summary>
    public static List<KeyBindingSettingModel> Build(KeyBindingsConfig config)
    {
        // 动作按键
        var actionDirectory = new KeyBindingSettingModel("动作");

        // 菜单按键
        var menuDirectory = new KeyBindingSettingModel("菜单");

        // 添加动作按键的二级菜单
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "向前移动",
                    nameof(config.MoveForward),
                    config.MoveForward
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "向后移动",
                    nameof(config.MoveBackward),
                    config.MoveBackward
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "向左移动",
                    nameof(config.MoveLeft),
                    config.MoveLeft
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "向右移动",
                    nameof(config.MoveRight),
                    config.MoveRight
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "切换走/跑；特定操作模式下向下移动",
                    nameof(config.SwitchToWalkOrRun),
                    config.SwitchToWalkOrRun
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "普通攻击",
                    nameof(config.NormalAttack),
                    config.NormalAttack
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "元素战技",
                    nameof(config.ElementalSkill),
                    config.ElementalSkill
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "元素爆发",
                    nameof(config.ElementalBurst),
                    config.ElementalBurst
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "冲刺（键盘）",
                    nameof(config.SprintKeyboard),
                    config.SprintKeyboard
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "冲刺（鼠标）",
                    nameof(config.SprintMouse),
                    config.SprintMouse
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "切换瞄准模式",
                    nameof(config.SwitchAimingMode),
                    config.SwitchAimingMode
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "跳跃；特定操作模式下向上移动",
                    nameof(config.Jump),
                    config.Jump
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "落下",
                    nameof(config.Drop),
                    config.Drop
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "拾取/交互（自动拾取由AutoPick模块管理）",
                    nameof(config.PickUpOrInteract),
                    config.PickUpOrInteract
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "快捷使用小道具",
                    nameof(config.QuickUseGadget),
                    config.QuickUseGadget
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "特定玩法内交互操作",
                    nameof(config.InteractionInSomeMode),
                    config.InteractionInSomeMode
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "开启任务追踪",
                    nameof(config.QuestNavigation),
                    config.QuestNavigation
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "中断挑战",
                    nameof(config.AbandonChallenge),
                    config.AbandonChallenge
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "切换小队角色1",
                    nameof(config.SwitchMember1),
                    config.SwitchMember1
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "切换小队角色2",
                    nameof(config.SwitchMember2),
                    config.SwitchMember2
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "切换小队角色3",
                    nameof(config.SwitchMember3),
                    config.SwitchMember3
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "切换小队角色4",
                    nameof(config.SwitchMember4),
                    config.SwitchMember4
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "切换小队角色5",
                    nameof(config.SwitchMember5),
                    config.SwitchMember5
                ));
        actionDirectory.Children.Add(new KeyBindingSettingModel(
                    "呼出快捷轮盘",
                    nameof(config.ShortcutWheel),
                    config.ShortcutWheel
                ));

        // 添加菜单按键的二级菜单
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开背包",
                    nameof(config.OpenInventory),
                    config.OpenInventory
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开角色界面",
                    nameof(config.OpenCharacterScreen),
                    config.OpenCharacterScreen
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开地图",
                    nameof(config.OpenMap),
                    config.OpenMap
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开派蒙界面",
                    nameof(config.OpenPaimonMenu),
                    config.OpenPaimonMenu
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开冒险之证界面",
                    nameof(config.OpenAdventurerHandbook),
                    config.OpenAdventurerHandbook
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开多人游戏界面",
                    nameof(config.OpenCoOpScreen),
                    config.OpenCoOpScreen
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开祈愿界面",
                    nameof(config.OpenWishScreen),
                    config.OpenWishScreen
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开纪行界面",
                    nameof(config.OpenBattlePassScreen),
                    config.OpenBattlePassScreen
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开活动面板",
                    nameof(config.OpenTheEventsMenu),
                    config.OpenTheEventsMenu
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开玩法系统界面（尘歌壶内猫尾酒馆内）",
                    nameof(config.OpenTheSettingsMenu),
                    config.OpenTheSettingsMenu
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开摆设界面（尘歌壶内）",
                    nameof(config.OpenTheFurnishingScreen),
                    config.OpenTheFurnishingScreen
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开星之归还（条件符合期间生效）",
                    nameof(config.OpenStellarReunion),
                    config.OpenStellarReunion
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "开关任务菜单",
                    nameof(config.OpenQuestMenu),
                    config.OpenQuestMenu
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开通知详情",
                    nameof(config.OpenNotificationDetails),
                    config.OpenNotificationDetails
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开聊天界面",
                    nameof(config.OpenChatScreen),
                    config.OpenChatScreen
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开特殊环境说明",
                    nameof(config.OpenSpecialEnvironmentInformation),
                    config.OpenSpecialEnvironmentInformation
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "查看教程详情",
                    nameof(config.CheckTutorialDetails),
                    config.CheckTutorialDetails
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "长按打开元素视野",
                    nameof(config.ElementalSight),
                    config.ElementalSight
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "呼出鼠标",
                    nameof(config.ShowCursor),
                    config.ShowCursor
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开队伍配置界面",
                    nameof(config.OpenPartySetupScreen),
                    config.OpenPartySetupScreen
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "打开好友界面",
                    nameof(config.OpenFriendsScreen),
                    config.OpenFriendsScreen
                ));
        menuDirectory.Children.Add(new KeyBindingSettingModel(
                    "隐藏主界面",
                    nameof(config.HideUI),
                    config.HideUI
                ));

        return [actionDirectory, menuDirectory];
    }

    /// <summary>所有非目录节点</summary>
    public static List<KeyBindingSettingModel> GetAllNonDirectoryKeyBinding(IEnumerable<KeyBindingSettingModel> modelList)
    {
        var list = new List<KeyBindingSettingModel>();
        foreach (var model in modelList)
        {
            if (!model.IsDirectory)
            {
                list.Add(model);
            }

            list.AddRange(GetAllNonDirectoryChildren(model));
        }

        return list;
    }

    /// <summary>子节点中的所有非目录节点（递归）</summary>
    public static List<KeyBindingSettingModel> GetAllNonDirectoryChildren(KeyBindingSettingModel model)
    {
        var result = new List<KeyBindingSettingModel>();
        foreach (var child in model.Children)
        {
            if (!child.IsDirectory)
            {
                result.Add(child);
            }

            result.AddRange(GetAllNonDirectoryChildren(child));
        }

        return result;
    }

    /// <summary>把节点的按键写回配置中同名属性（与 WPF 相同使用反射）</summary>
    public static void WriteBack(KeyBindingsConfig config, KeyBindingSettingModel model)
    {
        var pi = config.GetType().GetProperty(model.ConfigPropertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (pi != null && pi.CanWrite)
        {
            pi.SetValue(config, model.KeyValue, null);
        }
    }

    /// <summary>可选按键（按键编辑下拉用）</summary>
    public static IReadOnlyList<KeyChoice> KeyChoices { get; } =
        System.Enum.GetValues<KeyId>().Where(k => k != KeyId.Unknown).Distinct().Select(k => new KeyChoice(k, k.ToName())).ToList();
}

/// <summary>按键选项</summary>
public sealed record KeyChoice(KeyId Value, string Name);
