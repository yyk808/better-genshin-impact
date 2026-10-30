using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Config;

[Serializable]
public partial class PathingConditionConfig : ObservableObject
{
    [ObservableProperty]
    private string _mapMatchingMethod = "TemplateMatch";
    
    // 地图追踪条件配置
    [ObservableProperty]
    private ObservableCollection<Condition> _partyConditions = [];

    [ObservableProperty]
    private ObservableCollection<Condition> _avatarConditions = [];
    
    // 只在传送传送点时复活
    [ObservableProperty]
    private bool _onlyInTeleportRecover = false;

    // 低血量回复时机
    private RecoverTiming? _recoverTiming;

    public RecoverTiming RecoverTiming
    {
        get
        {
            if (_recoverTiming is null)
            {
                // 首次读取时从旧字段自动迁移
                _recoverTiming = RecoverTimingMigration.Migrate(_onlyInTeleportRecover);
            }
            return _recoverTiming.Value;
        }
        set => SetProperty(ref _recoverTiming, value);
    }

    // 使用小道具的间隔时间(ms)
    [ObservableProperty]
    private int _useGadgetIntervalMs = 0;
    
    // 启用自动吃药功能
    [ObservableProperty]
    private bool _autoEatEnabled = false;

    public static PathingConditionConfig Default => new()
    {
        AvatarConditions =
        [
            new Condition
            {
                Subject = "队伍中角色",
                Object = ["绮良良", "莱依拉", "茜特菈莉", "芭芭拉", "七七"],
                Result = "循环短E"
            },

            new Condition
            {
                Subject = "队伍中角色",
                Object = ["钟离"],
                Result = "循环长E"
            },

            new Condition
            {
                Subject = "队伍中角色",
                Object = ["迪希雅"],
                Result = "作为主要行走角色"
            }
        ]
    };

    /// <summary>
    /// 找出当前应该切换的队伍名称
    /// </summary>
    /// <param name="materialName">采集物名称</param>
    /// <param name="specialActions">特殊动作</param>
    /// <returns></returns>
    public string? FilterPartyName(string? materialName, List<string?> specialActions)
    {
        if (specialActions is { Count: > 0 })
        {
            // 特殊动作匹配队伍名
            foreach (var action in specialActions)
            {
                if (string.IsNullOrEmpty(action))
                {
                    continue;
                }

                if (!ConditionDefinitions.ActionCnDic.TryGetValue(action, out var actionCn))
                {
                    continue; // 不校验
                }

                var condition = PartyConditions.FirstOrDefault(c => c.Subject == "动作" && c.Object.Contains(actionCn));
                if (condition is { Result: not null })
                {
                    return condition.Result;
                }
            }
        }

        // 采集物匹配队伍名
        Condition? materialCondition = null;
        if (!string.IsNullOrEmpty(materialName))
        {
            materialCondition = PartyConditions.FirstOrDefault(c => c.Subject == "采集物" && c.Object.Contains(materialName));
        }
        if (materialCondition is { Result: not null })
        {
            return materialCondition.Result;
        }
        else
        {
            materialCondition = PartyConditions.FirstOrDefault(c => c.Subject == "采集物" && c.Object.Contains("全部"));
            if (materialCondition is { Result: not null })
            {
                return materialCondition.Result;
            }
        }

        return null;
    }

}
