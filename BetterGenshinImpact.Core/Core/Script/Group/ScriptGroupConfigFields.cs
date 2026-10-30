using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight;

namespace BetterGenshinImpact.Core.Script.Group;

/// <summary>配置组设置项的编辑方式</summary>
public enum ScriptGroupConfigFieldKind
{
    Toggle,
    Text,
    Number,
    Choice,
}

/// <summary>设置项适用的界面</summary>
public enum ScriptGroupConfigScope
{
    All,
    PuloniaOnly,
    LegacySchedulingOnly,
}

/// <summary>下拉选项：存储值与显示文本</summary>
public sealed record ScriptGroupConfigOption(object Value, string Text);

/// <summary>
/// 配置组设置的一项：分区、相对 <see cref="ScriptGroupConfig"/> 的属性路径、标题、说明、编辑方式和选项
/// </summary>
public sealed record ScriptGroupConfigField(
    string Section,
    string Path,
    string Label,
    string? Description,
    ScriptGroupConfigFieldKind Kind,
    IReadOnlyList<ScriptGroupConfigOption>? Options = null)
{
    /// <summary>只在某种界面中显示（任务计划节点 / 旧调度器）</summary>
    public ScriptGroupConfigScope Scope { get; init; }

    /// <summary>依赖其它设置的显示条件（与 WPF 界面相同，例如赶路角色为玛薇卡时才显示跳飞设置）</summary>
    public Func<ScriptGroupConfig, bool>? VisibleWhen { get; init; }

    /// <summary>在给定界面中当前是否显示</summary>
    public bool IsVisible(ScriptGroupConfig config, bool isPulonia)
        => Scope switch
           {
               ScriptGroupConfigScope.PuloniaOnly => isPulonia,
               ScriptGroupConfigScope.LegacySchedulingOnly => !isPulonia,
               _ => true,
           }
           && (VisibleWhen?.Invoke(config) ?? true);

    /// <summary>本项是否属于 Shell 执行配置</summary>
    public bool IsShell => Path.StartsWith("ShellConfig.", StringComparison.Ordinal) || Path == "EnableShellConfig";

    /// <summary>读取当前值</summary>
    public object? GetValue(ScriptGroupConfig config) => ScriptGroupConfigFields.Resolve(config, Path, out var target, out var prop) ? prop!.GetValue(target) : null;

    /// <summary>写入值，按属性类型转换（文本转数字、选项值等），转换失败返回 false 且不修改</summary>
    public bool TrySetValue(ScriptGroupConfig config, object? value)
    {
        if (!ScriptGroupConfigFields.Resolve(config, Path, out var target, out var prop) || !prop!.CanWrite)
        {
            return false;
        }

        if (!ScriptGroupConfigFields.TryConvert(value, prop.PropertyType, out var converted))
        {
            return false;
        }

        prop.SetValue(target, converted);
        return true;
    }
}

/// <summary>
/// 配置组设置（原 WPF ScriptGroupConfigView）的字段目录。WPF 与 Avalonia 使用同一套标题、说明和下拉选项
/// </summary>
public static class ScriptGroupConfigFields
{
    public static readonly IReadOnlyList<ScriptGroupConfigOption> RecoverTimings =
    [
        new(RecoverTiming.AnyWaypoint, "任何路径点"),
        new(RecoverTiming.OnlyTeleport, "只在传送点"),
        new(RecoverTiming.Never, "不回复"),
    ];

    public static readonly IReadOnlyList<ScriptGroupConfigOption> OnlyPickEliteDropsModes =
    [
        new("Closed", "关闭功能"),
        new("AllowAutoPickupForNonElite", "非精英允许自动拾取"),
        new("DisableAutoPickupForNonElite", "非精英关闭自动拾取"),
    ];

    public static readonly IReadOnlyList<ScriptGroupConfigOption> DamageNumberRecognitionModes =
    [
        new(DamageNumberRecognitionMode.Disabled, "禁用"),
        new(DamageNumberRecognitionMode.Ocr, "OCR识别"),
        new(DamageNumberRecognitionMode.Color, "颜色识别"),
    ];

    private static readonly PathingPartyConfig ListSource = new();

    public static readonly IReadOnlyList<ScriptGroupConfigOption> AvatarIndexes = Plain(ListSource.AvatarIndexList);
    public static readonly IReadOnlyList<ScriptGroupConfigOption> HurryOnAvatars = Plain(ListSource.HurryOnAvatarList);
    public static readonly IReadOnlyList<ScriptGroupConfigOption> TravelModes = Plain(ListSource.TravelModeList);

    private static IReadOnlyList<ScriptGroupConfigOption> Plain(IEnumerable<string> values)
        => values.Select(v => new ScriptGroupConfigOption(v, v == "" ? "（空）" : v)).ToList();

    private static readonly IReadOnlyList<ScriptGroupConfigField> Raw =
    [
        new("地图追踪行走配置（不启用则使用地图追踪内的条件配置）", "PathingConfig.Enabled", "地图追踪行走配置（不启用则使用地图追踪内的条件配置）", "强制修改行走途中配置开盾/回血等角色在队伍内的编号", ScriptGroupConfigFieldKind.Toggle),
        new("地图追踪行走配置（不启用则使用地图追踪内的条件配置）", "PathingConfig.AutoPickEnabled", "是否开启自动拾取", "推荐开启以拾取路线中的掉落物、采集物", ScriptGroupConfigFieldKind.Toggle),
        new("地图追踪行走配置（不启用则使用地图追踪内的条件配置）", "PathingConfig.AutoEatEnabled", "是否开启自动吃药", "检测到红血状态时自动使用便携营养袋", ScriptGroupConfigFieldKind.Toggle),
        new("地图追踪行走配置（不启用则使用地图追踪内的条件配置）", "PathingConfig.AutoSkipEnabled", "自动跳过剧情", "行走途中意外进入剧情时自动脱离", ScriptGroupConfigFieldKind.Toggle),
        new("地图追踪行走配置（不启用则使用地图追踪内的条件配置）", "PathingConfig.PartyName", "切换到队伍的名称（为空则不切换）", "队伍名称是你在游戏中手动设置的队伍名称文字", ScriptGroupConfigFieldKind.Text),
        new("地图追踪行走配置（不启用则使用地图追踪内的条件配置）", "PathingConfig.SkipPartySwitch", "跳过队伍切换", "开启后不切换队伍，直接使用当前队伍执行", ScriptGroupConfigFieldKind.Toggle),
        new("地图追踪行走配置（不启用则使用地图追踪内的条件配置）", "PathingConfig.IsVisitStatueBeforeSwitchParty", "切换队伍前强制前往安全的神像区域", "前往七天神像切换队伍可以避免切换队伍时位于异常状态导致切换失败", ScriptGroupConfigFieldKind.Toggle),
        new("地图追踪行走配置（不启用则使用地图追踪内的条件配置）", "PathingConfig.GuardianAvatarIndex", "【生存位】在队伍内的编号", "开盾/回血的角色位置，推荐琦良良、莱依拉", ScriptGroupConfigFieldKind.Choice, AvatarIndexes),
        new("地图追踪行走配置（不启用则使用地图追踪内的条件配置）", "PathingConfig.GuardianElementalSkillSecondInterval", "【生存位】释放元素战技的间隔（秒）", "可能要填的时间略大于实际技能CD时间", ScriptGroupConfigFieldKind.Text),
        new("地图追踪行走配置（不启用则使用地图追踪内的条件配置）", "PathingConfig.GuardianElementalSkillLongPress", "【生存位】元素战技是否长按", "钟离长按开盾、琦良良长按变猫等", ScriptGroupConfigFieldKind.Toggle),
        new("地图追踪行走配置（不启用则使用地图追踪内的条件配置）", "PathingConfig.MainAvatarIndex", "【行走位】主要行走的角色在队伍内的编号", "推荐和生存位相同，没有特殊需求无需配置", ScriptGroupConfigFieldKind.Choice, AvatarIndexes),
        new("地图追踪行走配置（不启用则使用地图追踪内的条件配置）", "PathingConfig.UseGadgetIntervalMs", "使用小道具的间隔时间（毫秒）", "在移动期间自动使用小道具，填0为不使用", ScriptGroupConfigFieldKind.Text),
        new("地图追踪行走配置（不启用则使用地图追踪内的条件配置）", "PathingConfig.RecoverTiming", "低血量回复时机", "选择低血量时的回复策略：任何路径点/只在传送点/不回复", ScriptGroupConfigFieldKind.Choice, RecoverTimings),
        new("地图追踪行走配置（不启用则使用地图追踪内的条件配置）", "PathingConfig.SkipDuring", "不在某时执行", "当执行完一个路线后，如果时间为当前配置的时间（范围：0-23），则此地图追踪任务后续都将都跳过，适用于连续执行的兜底任务，例如想通宵挂机，并且在4点后，开始执行新的任务。", ScriptGroupConfigFieldKind.Text),
        new("地图追踪行走配置（不启用则使用地图追踪内的条件配置）", "PathingConfig.HideOnRepeat", "不在连续任务中显示", "点击连续执行时，不显示该配置组", ScriptGroupConfigFieldKind.Toggle),
        new("自动赶路设置", "PathingConfig.HurryOnAvatar", "赶路角色", "空选：不进行赶路动作。选择具体角色：使用对应角色。选择自动：优先选择行走位跑图，行走位不支持时尝试使用其他角色。支持的角色列表：玛薇卡、闲云、桑多涅、恰斯卡、流浪者、伊法、希诺宁、法尔伽、夜兰", ScriptGroupConfigFieldKind.Choice, HurryOnAvatars),
        new("自动赶路设置", "PathingConfig.HurryOnFrameInterval", "覆写赶路帧间隔", "启用赶路逻辑时移速较快，设备性能富余时可选择适当降低帧间隔以提高稳定性，默认值100（ms）", ScriptGroupConfigFieldKind.Text),
        new("自动赶路设置", "PathingConfig.TravelMode", "赶路模式", "精准靠近：适合采集，节点间减速 / 连续赶路：适合锄地", ScriptGroupConfigFieldKind.Choice, TravelModes),
        new("自动赶路设置", "PathingConfig.Distance", "赶路启用距离", "节点距离大于此值时启用赶路技能，填0则关闭", ScriptGroupConfigFieldKind.Text),
        new("自动赶路设置", "PathingConfig.ApproachStopDistance", "接近停车距离", "到达节点前开始减速/下车/切人的距离，超出自定赶路距离时以前者为准", ScriptGroupConfigFieldKind.Text),
        new("自动赶路设置", "PathingConfig.SwitchToWalkEnabled", "接近时切人步行", "接近节点时切换步行角色", ScriptGroupConfigFieldKind.Toggle),
        new("自动赶路设置", "PathingConfig.MwkJumpFlyEnabled", "启用跳飞赶路", "开启后赶路时启用跳飞", ScriptGroupConfigFieldKind.Toggle),
        new("自动赶路设置", "PathingConfig.MwkJumpFlyDistance", "跳飞启用距离", "节点距离大于此值时启用跳飞，需大于赶路启用距离", ScriptGroupConfigFieldKind.Text),
        new("自动赶路设置", "PathingConfig.MwkJumpFlyIntervalSeconds", "跳飞/跳跃间隔（秒）", "跳飞/跳跃赶路间隔，闲云实际使用一半值", ScriptGroupConfigFieldKind.Text),
        new("自动赶路设置", "PathingConfig.MwkJumpFlySprintCount", "跳飞前额外冲刺次数", "6命玛薇卡可选，每次上车后前若干次跳飞改为冲刺跳飞，速度更快，夜魂值消耗更高，推荐3次", ScriptGroupConfigFieldKind.Text),
        new("自动赶路设置", "PathingConfig.MwkDisableSprintEnabled", "玛薇卡在车上禁用冲刺", "0命玛薇卡酌情勾选，节约夜魂值", ScriptGroupConfigFieldKind.Toggle),
        new("执行周期配置", "PathingConfig.TaskCycleConfig.Enable", "执行周期配置", "以天为单位来决定是否执行此配置组", ScriptGroupConfigFieldKind.Toggle),
        new("执行周期配置", "PathingConfig.TaskCycleConfig.BoundaryTime", "分界时间", "分界时间，一般填0或4，如果填0则当天从0点开始算，如果填4，则当天从凌晨4点开始算，小于4点算前一天。", ScriptGroupConfigFieldKind.Text),
        new("执行周期配置", "PathingConfig.TaskCycleConfig.IsBoundaryTimeBasedOnServerTime", "分界时间参考服务器时区", "启用后，上面的分界时间将基于“服务器时区设置”计算。不启用则使用电脑本地时间（旧版行为）。", ScriptGroupConfigFieldKind.Toggle),
        new("执行周期配置", "PathingConfig.TaskCycleConfig.Cycle", "周期", "不同材料有不同的周期，按需配置，如矿石类是3、突破材料是2，或按照自已想几天执行一次配置即可。", ScriptGroupConfigFieldKind.Text),
        new("执行周期配置", "PathingConfig.TaskCycleConfig.Index", "执行序号", "执行周期序号，按时间戳对应的天数(会根据分界时间修正)，对周期求余值加1，得出的值和配置执一致就会执行，否则跳过任务。可以理解为周期内第几天执行。", ScriptGroupConfigFieldKind.Text),
        new("战斗配置", "PathingConfig.AutoFightEnabled", "战斗配置", "需开启上方地图追踪配置，开启此项可制定战斗配置，关闭此项，则用于独立任务中的配置", ScriptGroupConfigFieldKind.Toggle),
        new("战斗配置", "PathingConfig.AutoFightConfig.ActionSchedulerByCd", "根据技能CD优化出招人员", "根据填入人或人和cd，来决定当此人元素战技cd未结束时，跳过此人出招，来优化战斗流程，可填入人名或人名数字（用英文逗号分隔），多种用分号分隔，例如：白术；钟离与12。如果人名，则用内置cd检查（或填入数字也小于0），如果是人名和数字，则把数字当做出招cd(秒)。", ScriptGroupConfigFieldKind.Text),
        new("自动检测战斗结束", "PathingConfig.AutoFightConfig.FightFinishDetectEnabled", "自动检测战斗结束", "检测到战斗已经结束的情况下，停止自动战斗功能", ScriptGroupConfigFieldKind.Toggle),
        new("更快触发战斗结束检查", "PathingConfig.AutoFightConfig.FinishDetectConfig.FastCheckEnabled", "更快触发战斗结束检查", "快速检查战斗结束( 默认关闭 )，完成一轮动作后，如果满足条件，则触发一次战斗结束检查。", ScriptGroupConfigFieldKind.Toggle),
        new("更快触发战斗结束检查", "PathingConfig.AutoFightConfig.FinishDetectConfig.FastCheckParams", "更快触发战斗结束检查参数", "快速检查战斗结束的参数，填写数字（秒）时距离上次检查超过该时间则触发检查，填写人名时对应角色动作后触发检查。多项时使用分号分隔，格式如5或5;白术;", ScriptGroupConfigFieldKind.Text),
        new("更快触发战斗结束检查", "PathingConfig.AutoFightConfig.FinishDetectConfig.CheckAfterSwitchAvatar", "切人后再执行战斗结束检查", "将触发战斗结束检查的时机调整为切人后，无需等待上一个动作后摇。目前仅 JSON 策略下生效。", ScriptGroupConfigFieldKind.Toggle),
        new("自动检测战斗结束", "PathingConfig.AutoFightConfig.FinishDetectConfig.SkipFightEndCheckWhenEnemyVisible", "敌人可见时跳过战斗结束检查", "检测到敌人血条时跳过战斗结束检查。与“旋转寻找敌人位置”互斥。", ScriptGroupConfigFieldKind.Toggle),
        new("自动检测战斗结束", "PathingConfig.AutoFightConfig.FinishDetectConfig.BlockCheckBeforeBattleSeconds", "开战后一段时间阻断战斗结束检查（秒）", "开战后该时间内的战斗结束检查直接视为战斗未结束，默认为0表示不阻断。", ScriptGroupConfigFieldKind.Number),
        new("自动检测战斗结束", "PathingConfig.AutoFightConfig.FinishDetectConfig.PaimonEndCheckEnabled", "派蒙辅助检测", "按L后当派蒙头像可见时提前跳出战斗结束检测。默认开启。", ScriptGroupConfigFieldKind.Toggle),
        new("自动检测战斗结束", "PathingConfig.AutoFightConfig.FinishDetectConfig.PaimonEndCheckDelay", "派蒙检测延时（秒）", "按L后等待该时间再检测派蒙头像，默认为0.1秒", ScriptGroupConfigFieldKind.Number),
        new("自动检测战斗结束", "PathingConfig.AutoFightConfig.FinishDetectConfig.RotateFindEnemyEnabled", "旋转寻找敌人位置（实验性功能）", null, ScriptGroupConfigFieldKind.Toggle),
        new("自动检测战斗结束", "PathingConfig.AutoFightConfig.FinishDetectConfig.RotaryFactor", "旋转速度", "建议单次360°左右", ScriptGroupConfigFieldKind.Number),
        new("自动检测战斗结束", "PathingConfig.AutoFightConfig.FinishDetectConfig.CheckBeforeBurst", "Q前检测", "释放Q技能前检测是否结束战斗", ScriptGroupConfigFieldKind.Toggle),
        new("自动检测战斗结束", "PathingConfig.AutoFightConfig.FinishDetectConfig.IsFirstCheck", "尝试面敌", "开战寻敌，战斗尝试面向敌人", ScriptGroupConfigFieldKind.Toggle),
        new("自动检测战斗结束", "PathingConfig.AutoFightConfig.FinishDetectConfig.CheckEndDelay", "检查战斗结束的延时", "触发战斗结束检查时，先等待该延时以确保角色动作后摇结束。也可为角色单独指定延时，格式如 0.4 或 0.4;钟离与1.5", ScriptGroupConfigFieldKind.Text),
        new("自动检测战斗结束", "PathingConfig.AutoFightConfig.FinishDetectConfig.BeforeDetectDelay", "按键触发后检查延时", "按下切换队伍后去检查屏幕色块的延时，默认为0.45秒。若出现无法结束战斗可以适当提高这个值，比如0.75。但不要太大，确保这个延时不会真的把队伍配置界面切出来。", ScriptGroupConfigFieldKind.Text),
        new("战斗中视觉识别配置", "PathingConfig.AutoFightConfig.EnableCombatTargeting", "战斗中持续索敌", "战斗过程中情况允许时持续尝试面朝敌人", ScriptGroupConfigFieldKind.Toggle),
        new("战斗中视觉识别配置", "PathingConfig.AutoFightConfig.LockLostWaitTime", "脱锁等待时间（秒）", "敌人不可见时等待一定时间后开始旋转索敌", ScriptGroupConfigFieldKind.Text),
        new("战斗中视觉识别配置", "PathingConfig.AutoFightConfig.DamageNumberRecognitionMode", "伤害数字识别模式", "当血条不可见或传奇等固定位置血条时使用伤害数字辅助定位敌人", ScriptGroupConfigFieldKind.Choice, DamageNumberRecognitionModes),
        new("战斗中视觉识别配置", "PathingConfig.AutoFightConfig.TargetingDetectionInterval", "索敌识别间隔（毫秒）", "每次索敌识别之间的间隔时间", ScriptGroupConfigFieldKind.Text),
        new("战斗中视觉识别配置", "PathingConfig.AutoFightConfig.DrawRecognitionResults", "绘制识别结果位置", "在遮罩窗口上显示血条、伤害数字等识别结果的边框", ScriptGroupConfigFieldKind.Toggle),
        new("盾奶位角色优先释放技能", "PathingConfig.AutoFightConfig.GuardianAvatar", "盾奶位角色在队伍中的位置", "实时检测盾奶位技能情况", ScriptGroupConfigFieldKind.Choice, AvatarIndexes),
        new("盾奶位角色优先释放技能", "PathingConfig.AutoFightConfig.GuardianCombatSkip", "盾奶位：释放E战技", null, ScriptGroupConfigFieldKind.Toggle),
        new("盾奶位角色优先释放技能", "PathingConfig.AutoFightConfig.BurstEnabled", "盾奶位：释放Q爆发", null, ScriptGroupConfigFieldKind.Toggle),
        new("盾奶位角色优先释放技能", "PathingConfig.AutoFightConfig.GuardianAvatarHold", "盾奶位战技长按", "检测盾奶位战技短按或长按，禁用:短按 / 启用:长按", ScriptGroupConfigFieldKind.Toggle),
        new("战斗后拾取动作", "PathingConfig.AutoFightConfig.PickDropsAfterFightEnabled", "扫描掉落物光柱", "战斗结束后旋转视角寻找掉落物光柱并靠近（仅在无万叶和琴时备用）", ScriptGroupConfigFieldKind.Toggle),
        new("战斗后拾取动作", "PathingConfig.AutoFightConfig.PickDropsAfterFightSeconds", "扫描掉落物光柱最大时长", null, ScriptGroupConfigFieldKind.Text),
        new("战斗后拾取动作", "PathingConfig.AutoFightConfig.KazuhaPickupEnabled", "聚集材料动作", "战斗结束后，如存在(万叶/琴)，则执行长E聚集材料动作", ScriptGroupConfigFieldKind.Toggle),
        new("战斗后拾取动作", "PathingConfig.AutoFightConfig.QinDoublePickUp", "二次拾取", "琴首次拾取空，再次拾取。即使万叶刚使用过技能，也会强制再次拾取。", ScriptGroupConfigFieldKind.Toggle),
        new("战斗后拾取动作", "PathingConfig.AutoFightConfig.ExpBasedPickupEnabled", "基于经验值判断拾取(经验值57/58/60，不含传奇120等数字)", "未能检测到经验值图标时跳过战斗后拾取动作", ScriptGroupConfigFieldKind.Toggle),
        new("战斗后拾取动作", "PathingConfig.AutoFightConfig.BattleThresholdForLoot", "拾取战斗人次阈值", "当战斗人次小于一定次数时，不触发拾取掉落物和万叶拾取配置，只有不小于2时才生效", ScriptGroupConfigFieldKind.Text),
        new("战斗后拾取动作", "PathingConfig.AutoFightConfig.OnlyPickEliteDropsMode", "只拾取精英掉落模式", "需要路径文件支持，需勾选区分怪物拾取才生效。只拾取标记为精英或传奇的点位", ScriptGroupConfigFieldKind.Choice, OnlyPickEliteDropsModes),
        new("战斗后拾取动作", "PathingConfig.AutoFightConfig.KazuhaPartyName", "万叶拾取队伍名", "战斗结束后，如果不存在万叶，则切换至存在万叶的队伍", ScriptGroupConfigFieldKind.Text),
        new("战斗配置", "PathingConfig.AutoFightConfig.Timeout", "自动战斗超时(秒)", "到达指定时间后，自动停止战斗", ScriptGroupConfigFieldKind.Text),
        new("战斗配置", "PathingConfig.AutoFightConfig.SwimmingEnabled", "游泳检测(自动战斗过程中)", "先回战斗节点，失败则去七天神像", ScriptGroupConfigFieldKind.Toggle),
        new("供JS脚本调用Buff类食物配置；食物名称会忽略“美味的”等前缀，请填不带前缀的名称", "PathingConfig.AutoEatConfig.DefaultAtkBoostingDishName", "默认的攻击类料理名称", "入参 foodEffectType 为 1 时使用", ScriptGroupConfigFieldKind.Text),
        new("供JS脚本调用Buff类食物配置；食物名称会忽略“美味的”等前缀，请填不带前缀的名称", "PathingConfig.AutoEatConfig.DefaultAdventurersDishName", "默认的冒险类料理名称", "入参 foodEffectType 为 2 时使用", ScriptGroupConfigFieldKind.Text),
        new("供JS脚本调用Buff类食物配置；食物名称会忽略“美味的”等前缀，请填不带前缀的名称", "PathingConfig.AutoEatConfig.DefaultDefBoostingDishName", "默认的防御类料理名称", "入参 foodEffectType 为 3 时使用", ScriptGroupConfigFieldKind.Text),
        new("Shell执行配置", "EnableShellConfig", "Shell执行配置", "在本组中执行Shell命令会使用这里的配置。如关闭此项，则使用默认配置。", ScriptGroupConfigFieldKind.Toggle),
        new("Shell执行配置", "ShellConfig.Disable", "禁用Shell任务", "如果打开，则Shell任务会被跳过执行", ScriptGroupConfigFieldKind.Toggle),
        new("Shell执行配置", "ShellConfig.Timeout", "Shell执行超时", null, ScriptGroupConfigFieldKind.Text),
        new("Shell执行配置", "ShellConfig.NoWindow", "隐藏命令执行窗口", "推荐启用，不然会有一个大黑框。", ScriptGroupConfigFieldKind.Toggle),
        new("Shell执行配置", "ShellConfig.Output", "在Log中记录命令输出", "打开的话若命令有输出会在Log中显示，关闭则不显示。(只有命令执行成功且有输出才会显示)", ScriptGroupConfigFieldKind.Toggle),
    ];

    /// <summary>全部字段（含显示条件）</summary>
    public static readonly IReadOnlyList<ScriptGroupConfigField> All = Raw.Select(Decorate).ToList();

    // —— 显示条件（WPF ScriptGroupConfigViewModel 使用同一组判断）——

    /// <summary>赶路角色为 自动 或 玛薇卡 时显示跳飞相关配置</summary>
    public static bool IsHurryOnMwkOrAuto(PathingPartyConfig c) => c.HurryOnAvatar is "自动" or "玛薇卡";

    /// <summary>赶路角色为 自动/玛薇卡/闲云 时显示跳飞间隔配置</summary>
    public static bool IsHurryOnMwkOrAutoOrXianyun(PathingPartyConfig c) => c.HurryOnAvatar is "自动" or "玛薇卡" or "闲云";

    /// <summary>跳飞启用距离、额外冲刺次数：角色为 自动/玛薇卡 且启用跳飞时显示</summary>
    public static bool IsJumpFlyDetailVisible(PathingPartyConfig c) => IsHurryOnMwkOrAuto(c) && c.MwkJumpFlyEnabled;

    /// <summary>切人步行：角色为 自动 或实际应用了该选项的角色（玛薇卡/希诺宁）时显示</summary>
    public static bool IsSwitchToWalkVisible(PathingPartyConfig c) => c.HurryOnAvatar is "自动" or "玛薇卡" or "希诺宁";

    /// <summary>接近停车距离：角色为 自动 或存在接近停车逻辑的角色（闲云/法尔伽无此逻辑）时显示</summary>
    public static bool IsApproachStopDistanceVisible(PathingPartyConfig c) => c.HurryOnAvatar is "自动"
        or "玛薇卡" or "希诺宁" or "桑多涅" or "恰斯卡" or "伊法" or "流浪者" or "夜兰";

    private static ScriptGroupConfigField Decorate(ScriptGroupConfigField f)
    {
        return f.Path switch
        {
            "PathingConfig.AutoSkipEnabled" or "PathingConfig.SkipPartySwitch" => f with { Scope = ScriptGroupConfigScope.PuloniaOnly },
            "PathingConfig.SkipDuring" or "PathingConfig.HideOnRepeat" => f with { Scope = ScriptGroupConfigScope.LegacySchedulingOnly },
            _ when f.Path.StartsWith("PathingConfig.TaskCycleConfig.", StringComparison.Ordinal) => f with { Scope = ScriptGroupConfigScope.LegacySchedulingOnly },
            "PathingConfig.ApproachStopDistance" => f with { VisibleWhen = c => IsApproachStopDistanceVisible(c.PathingConfig) },
            "PathingConfig.SwitchToWalkEnabled" => f with { VisibleWhen = c => IsSwitchToWalkVisible(c.PathingConfig) },
            "PathingConfig.MwkJumpFlyEnabled" or "PathingConfig.MwkDisableSprintEnabled" => f with { VisibleWhen = c => IsHurryOnMwkOrAuto(c.PathingConfig) },
            "PathingConfig.MwkJumpFlyDistance" or "PathingConfig.MwkJumpFlySprintCount" => f with { VisibleWhen = c => IsJumpFlyDetailVisible(c.PathingConfig) },
            "PathingConfig.MwkJumpFlyIntervalSeconds" => f with { VisibleWhen = c => IsHurryOnMwkOrAutoOrXianyun(c.PathingConfig) },
            "PathingConfig.AutoFightConfig.FinishDetectConfig.PaimonEndCheckDelay" => f with { VisibleWhen = c => c.PathingConfig.AutoFightConfig.FinishDetectConfig.PaimonEndCheckEnabled },
            "PathingConfig.AutoFightConfig.FinishDetectConfig.RotaryFactor" => f with { VisibleWhen = c => c.PathingConfig.AutoFightConfig.FinishDetectConfig.RotateFindEnemyEnabled },
            "PathingConfig.AutoFightConfig.PickDropsAfterFightSeconds" => f with { VisibleWhen = c => c.PathingConfig.AutoFightConfig.PickDropsAfterFightEnabled },
            "PathingConfig.AutoFightConfig.QinDoublePickUp" => f with { VisibleWhen = c => c.PathingConfig.AutoFightConfig.KazuhaPickupEnabled },
            _ => f,
        };
    }

    /// <summary>按分区分组，保持原界面顺序</summary>
    public static IEnumerable<IGrouping<string, ScriptGroupConfigField>> Sections => All.GroupBy(f => f.Section);

    public static bool Resolve(object root, string path, out object? target, out PropertyInfo? prop)
    {
        target = root;
        prop = null;
        var parts = path.Split('.');
        for (var i = 0; i < parts.Length; i++)
        {
            if (target == null)
            {
                return false;
            }

            prop = target.GetType().GetProperty(parts[i], BindingFlags.Public | BindingFlags.Instance);
            if (prop == null)
            {
                return false;
            }

            if (i < parts.Length - 1)
            {
                target = prop.GetValue(target);
            }
        }

        return target != null && prop != null;
    }

    public static bool TryConvert(object? value, Type type, out object? converted)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        converted = null;
        if (value == null || (value is string s0 && s0.Length == 0 && underlying != null))
        {
            return underlying != null || !type.IsValueType;
        }

        var t = underlying ?? type;
        try
        {
            if (t.IsInstanceOfType(value))
            {
                converted = value;
            }
            else if (t.IsEnum)
            {
                converted = value is string es ? Enum.Parse(t, es) : Enum.ToObject(t, value);
            }
            else if (t == typeof(string))
            {
                converted = Convert.ToString(value, CultureInfo.InvariantCulture);
            }
            else
            {
                converted = Convert.ChangeType(value is string str ? str.Trim() : value, t, CultureInfo.InvariantCulture);
            }

            return true;
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            converted = null;
            return false;
        }
    }
}
