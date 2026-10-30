using System;
using System.Collections.Generic;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoCook;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation;
using BetterGenshinImpact.GameTask.AutoMusicGame;
using BetterGenshinImpact.GameTask.AutoWood;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.OneDragon;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Helpers.Extensions;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.Common.Hotkeys;

/// <summary>
/// 快捷键动作中与宿主界面无关的部分，供 WPF 快捷键页与 Linux 宿主共用：
/// 实时任务开关、遮罩显示开关、快捷点击原神内确认 / 取消按钮、独立任务快捷键与任务的对应关系、长按类快捷键清单
/// </summary>
public static class HotkeyActionCatalog
{
    /// <summary>一个配置开关类快捷键：配置属性名（HotKeyConfig 中的属性）、显示名、读写开关</summary>
    public sealed record ConfigToggle(string HotkeyProperty, string Name, Func<AllConfig, bool> Get, Action<AllConfig, bool> Set);

    /// <summary>“实时任务”分组中的开关（与 WPF 快捷键页顺序相同）</summary>
    public static IReadOnlyList<ConfigToggle> RealtimeToggles { get; } =
    [
        new(nameof(HotKeyConfig.AutoPickEnabledHotkey), "自动拾取", c => c.AutoPickConfig.Enabled, (c, v) => c.AutoPickConfig.Enabled = v),
        new(nameof(HotKeyConfig.AutoSkipEnabledHotkey), "自动剧情", c => c.AutoSkipConfig.Enabled, (c, v) => c.AutoSkipConfig.Enabled = v),
        new(nameof(HotKeyConfig.AutoSkipHangoutEnabledHotkey), "自动邀约", c => c.AutoSkipConfig.AutoHangoutEventEnabled,
            (c, v) => c.AutoSkipConfig.AutoHangoutEventEnabled = v),
        new(nameof(HotKeyConfig.AutoFishingEnabledHotkey), "自动钓鱼", c => c.AutoFishingConfig.Enabled, (c, v) => c.AutoFishingConfig.Enabled = v),
        new(nameof(HotKeyConfig.QuickTeleportEnabledHotkey), "快速传送", c => c.QuickTeleportConfig.Enabled, (c, v) => c.QuickTeleportConfig.Enabled = v),
        new(nameof(HotKeyConfig.SkillCdEnabledHotkey), "冷却提示", c => c.SkillCdConfig.Enabled, (c, v) => c.SkillCdConfig.Enabled = v),
        new(nameof(HotKeyConfig.MapMaskEnabledHotkey), "地图遮罩", c => c.MapMaskConfig.Enabled, (c, v) => c.MapMaskConfig.Enabled = v),
    ];

    /// <summary>切换开关并记录日志“切换{Name}状态为[开启/关闭]”，返回切换后的值</summary>
    public static bool Toggle(ConfigToggle toggle, AllConfig config, ILogger logger)
    {
        var value = !toggle.Get(config);
        toggle.Set(config, value);
        logger.LogInformation("切换{Name}状态为[{Enabled}]", toggle.Name, value.ToChinese());
        return value;
    }

    /// <summary>日志与状态窗口展示开关（状态窗口与日志窗口同步）</summary>
    public static void ToggleLogBox(MaskWindowConfig mask)
    {
        mask.ShowLogBox = !mask.ShowLogBox;
        mask.ShowStatus = mask.ShowLogBox;
    }

    /// <summary>遮罩指标栏展示开关</summary>
    public static void ToggleOverlayMetrics(MaskWindowConfig mask) => mask.ShowOverlayMetrics = !mask.ShowOverlayMetrics;

    /// <summary>快捷点击原神内确认（<paramref name="confirm"/>=true）或取消按钮，结果写入任务日志</summary>
    public static bool ClickGenshinButton(GameRuntime runtime, bool confirm)
    {
        using var capture = runtime.CaptureToRectArea();
        var ok = confirm ? Bv.ClickConfirmButton(capture) : Bv.ClickCancelButton(capture);
        TaskControl.Logger.LogInformation(ok ? "触发快捷点击原神内{Btn}按钮：成功" : "触发快捷点击原神内{Btn}按钮：未找到按钮图片", confirm ? "确认" : "取消");
        return ok;
    }

    /// <summary>“独立任务”分组：快捷键属性名 → 独立任务 Id（启动 / 停止）</summary>
    public static IReadOnlyDictionary<string, string> SoloTaskHotkeys { get; } = new Dictionary<string, string>
    {
        [nameof(HotKeyConfig.OnedragonHotkey)] = OneDragonTask.TaskId,
        [nameof(HotKeyConfig.AutoGeniusInvokationHotkey)] = AutoGeniusInvokationTask.TaskId,
        [nameof(HotKeyConfig.AutoWoodHotkey)] = AutoWoodTask.TaskId,
        [nameof(HotKeyConfig.AutoFightHotkey)] = AutoFightEntryTask.TaskId,
        [nameof(HotKeyConfig.AutoDomainHotkey)] = AutoDomainTask.TaskId,
        [nameof(HotKeyConfig.AutoMusicGameHotkey)] = AutoMusicGameTask.TaskId,
        [nameof(HotKeyConfig.AutoFishingGameHotkey)] = AutoFishingTask.TaskId,
        [nameof(HotKeyConfig.AutoCookGameHotkey)] = AutoCookTask.TaskId,
    };

    /// <summary>
    /// 长按类快捷键（WPF 中 IsHold=true，只能用键盘 / 鼠标监听方式）：按住期间反复执行按下动作，
    /// 或分别处理按下 / 抬起（一键领取奖励、一键战斗宏）
    /// </summary>
    public static IReadOnlySet<string> HoldHotkeys { get; } = new HashSet<string>
    {
        nameof(HotKeyConfig.QuickTeleportTickHotkey),
        nameof(HotKeyConfig.TurnAroundHotkey),
        nameof(HotKeyConfig.EnhanceArtifactHotkey),
        nameof(HotKeyConfig.QuickBuyHotkey),
        nameof(HotKeyConfig.OneKeyClaimRewardHotkey),
        nameof(HotKeyConfig.ClickGenshinConfirmButtonHotkey),
        nameof(HotKeyConfig.ClickGenshinCancelButtonHotkey),
        nameof(HotKeyConfig.OneKeyFightHotkey),
    };
}
