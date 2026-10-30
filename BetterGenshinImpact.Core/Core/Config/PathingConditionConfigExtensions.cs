using BetterGenshinImpact.GameTask.AutoFight.Model;
using System.Globalization;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
/// 按当前队伍生成路径追踪队伍配置。原为 <see cref="PathingConditionConfig"/> 的实例方法，
/// 因依赖战斗场景识别（CombatScenes）改为扩展方法，调用写法与实现均未改变
/// </summary>
public static class PathingConditionConfigExtensions
{
    /// <summary>
    /// 通过条件配置生成队伍配置
    /// </summary>
    /// <returns></returns>
    public static PathingPartyConfig BuildPartyConfigByCondition(this PathingConditionConfig config, CombatScenes combatScenes)
    {
        PathingPartyConfig partyConfig = PathingPartyConfig.BuildDefault();
        // 使用最优先匹配上的条件
        foreach (var avatarCondition in config.AvatarConditions)
        {
            if (avatarCondition.Result == "循环短E" || avatarCondition.Result == "循环长E")
            {
                foreach (var avatar in combatScenes.GetAvatars())
                {
                    if (avatarCondition is { Object: not null } && avatarCondition.Object.Contains(avatar.Name))
                    {
                        partyConfig.GuardianAvatarIndex = avatar.Index.ToString();
                        if (avatarCondition.Result == "循环长E")
                        {
                            partyConfig.GuardianElementalSkillLongPress = true;
                            partyConfig.GuardianElementalSkillSecondInterval = avatar.CombatAvatar.SkillHoldCd.ToString(CultureInfo.CurrentCulture);
                        }
                        else
                        {
                            partyConfig.GuardianElementalSkillLongPress = false;
                            partyConfig.GuardianElementalSkillSecondInterval = avatar.CombatAvatar.SkillCd.ToString(CultureInfo.CurrentCulture);
                        }
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(partyConfig.GuardianAvatarIndex))
                {
                    break;
                }
            }
        }

        foreach (var avatarCondition in config.AvatarConditions)
        {
            if (avatarCondition.Result == "作为主要行走角色")
            {
                foreach (var avatar in combatScenes.GetAvatars())
                {
                    if (avatarCondition is not null && avatarCondition.Object.Contains(avatar.Name))
                    {
                        partyConfig.MainAvatarIndex = avatar.Index.ToString();
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(partyConfig.MainAvatarIndex))
                {
                    break;
                }
            }
        }

        // 默认元素力采集角色
        // var elementalCollectAvatars = ElementalCollectAvatarConfigs.Lists;
        // foreach (var avatar in combatScenes.Avatars)
        // {
        //     var elementalCollectAvatar = elementalCollectAvatars.FirstOrDefault(x => x.Name == avatar.Name);
        //     if (elementalCollectAvatar != null)
        //     {
        //         if (elementalCollectAvatar.ElementalType == ElementalType.Hydro)
        //         {
        //             partyConfig.HydroCollectAvatarIndex = avatar.Index.ToString();
        //         }
        //         else if (elementalCollectAvatar.ElementalType == ElementalType.Electro)
        //         {
        //             partyConfig.ElectroCollectAvatarIndex = avatar.Index.ToString();
        //         }
        //         else if (elementalCollectAvatar.ElementalType == ElementalType.Anemo)
        //         {
        //             partyConfig.AnemoCollectAvatarIndex = avatar.Index.ToString();
        //         }
        //     }
        // }

        return partyConfig;
    }
}
