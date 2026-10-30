using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Simulator.Extensions;

namespace BetterGenshinImpact.GameTask.Common;

/// <summary>按用户键位设置映射脚本 / 任务中写死的默认按键（原 KeyBindingsSettingsPageViewModel.MappingKey）</summary>
public static class GameKeyMapping
{
    /// <summary>
    /// 根据默认键位，将脚本中写死的按键映射为实际的按键
    /// </summary>
    public static GameKey MappingKey(GameKey source)
    {
        // 静态方法没有创建方可以传配置，暂时读配置根（见 task-context-removal.md 第 5 节）
        if (BetterGenshinImpact.Core.Config.ConfigHost.Config!.KeyBindingsConfig.GlobalKeyMappingEnabled)
        {
            // NOTE: 普攻、鼠标的冲刺、元素视野、视角居中和派蒙菜单不支持改键，此处不会进行映射
            return source switch
            {
                GameKey.VK_W => GIActions.MoveForward.ToActionKey().ToVK(),
                GameKey.VK_S => GIActions.MoveBackward.ToActionKey().ToVK(),
                GameKey.VK_A => GIActions.MoveLeft.ToActionKey().ToVK(),
                GameKey.VK_D => GIActions.MoveRight.ToActionKey().ToVK(),
                GameKey.VK_LCONTROL => GIActions.SwitchToWalkOrRun.ToActionKey().ToVK(),
                GameKey.VK_E => GIActions.ElementalSkill.ToActionKey().ToVK(),
                GameKey.VK_Q => GIActions.ElementalBurst.ToActionKey().ToVK(),
                GameKey.VK_LSHIFT => GIActions.SprintKeyboard.ToActionKey().ToVK(),
                GameKey.VK_R => GIActions.SwitchAimingMode.ToActionKey().ToVK(),
                GameKey.VK_SPACE => GIActions.Jump.ToActionKey().ToVK(),
                GameKey.VK_X => GIActions.Drop.ToActionKey().ToVK(),
                GameKey.VK_F => GIActions.PickUpOrInteract.ToActionKey().ToVK(),
                GameKey.VK_Z => GIActions.QuickUseGadget.ToActionKey().ToVK(),
                GameKey.VK_T => GIActions.InteractionInSomeMode.ToActionKey().ToVK(),
                GameKey.VK_V => GIActions.QuestNavigation.ToActionKey().ToVK(),
                GameKey.VK_P => GIActions.AbandonChallenge.ToActionKey().ToVK(),
                GameKey.VK_1 => GIActions.SwitchMember1.ToActionKey().ToVK(),
                GameKey.VK_2 => GIActions.SwitchMember2.ToActionKey().ToVK(),
                GameKey.VK_3 => GIActions.SwitchMember3.ToActionKey().ToVK(),
                GameKey.VK_4 => GIActions.SwitchMember4.ToActionKey().ToVK(),
                GameKey.VK_5 => GIActions.SwitchMember5.ToActionKey().ToVK(),
                GameKey.VK_TAB => GIActions.ShortcutWheel.ToActionKey().ToVK(),
                GameKey.VK_B => GIActions.OpenInventory.ToActionKey().ToVK(),
                GameKey.VK_C => GIActions.OpenCharacterScreen.ToActionKey().ToVK(),
                GameKey.VK_M => GIActions.OpenMap.ToActionKey().ToVK(),
                GameKey.VK_F1 => GIActions.OpenAdventurerHandbook.ToActionKey().ToVK(),
                GameKey.VK_F2 => GIActions.OpenCoOpScreen.ToActionKey().ToVK(),
                GameKey.VK_F3 => GIActions.OpenWishScreen.ToActionKey().ToVK(),
                GameKey.VK_F4 => GIActions.OpenBattlePassScreen.ToActionKey().ToVK(),
                GameKey.VK_F5 => GIActions.OpenTheEventsMenu.ToActionKey().ToVK(),
                GameKey.VK_F6 => GIActions.OpenTheSettingsMenu.ToActionKey().ToVK(),
                GameKey.VK_F7 => GIActions.OpenTheFurnishingScreen.ToActionKey().ToVK(),
                GameKey.VK_F8 => GIActions.OpenStellarReunion.ToActionKey().ToVK(),
                GameKey.VK_J => GIActions.OpenQuestMenu.ToActionKey().ToVK(),
                GameKey.VK_Y => GIActions.OpenNotificationDetails.ToActionKey().ToVK(),
                GameKey.VK_RETURN => GIActions.OpenChatScreen.ToActionKey().ToVK(),
                GameKey.VK_U => GIActions.OpenSpecialEnvironmentInformation.ToActionKey().ToVK(),
                GameKey.VK_G => GIActions.CheckTutorialDetails.ToActionKey().ToVK(),
                GameKey.VK_LMENU => GIActions.ShowCursor.ToActionKey().ToVK(),
                GameKey.VK_L => GIActions.OpenPartySetupScreen.ToActionKey().ToVK(),
                GameKey.VK_O => GIActions.OpenFriendsScreen.ToActionKey().ToVK(),
                GameKey.VK_OEM_2 => GIActions.HideUI.ToActionKey().ToVK(),
                // 其他按键（保留的？）不作转换
                _ => source,
            };
        }
        return source;
    }
}
