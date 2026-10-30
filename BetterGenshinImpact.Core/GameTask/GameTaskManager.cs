using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Assets;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.GameLoading;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Placeholder;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
using OpenCvSharp;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGenshinImpact.GameTask.MapMask;
using BetterGenshinImpact.GameTask.SkillCd;
using BetterGenshinImpact.GameTask.Runtime;
using System;

namespace BetterGenshinImpact.GameTask;

internal class GameTaskManager
{
    /// <summary>
    /// 创建一次截图会话用的全部触发器，按优先级从高到低排列。由 TaskTriggerDispatcher.Start 调用
    /// </summary>
    /// <param name="runtime">本次截图会话绑定的运行环境，触发器在整个会话内只使用它</param>
    /// <param name="config">配置根</param>
    public static List<ITaskTrigger> CreateTriggers(GameRuntime runtime, AllConfig config)
    {
        List<ITaskTrigger> triggers =
        [
            new TestTrigger(runtime),
            new GameLoadingTrigger(runtime, config),
            new AutoPick.AutoPickTrigger(runtime, config),
            new QuickTeleport.QuickTeleportTrigger(runtime, config),
            new AutoSkip.AutoSkipTrigger(runtime, config),
            new AutoFishing.AutoFishingTrigger(runtime, config),
            new AutoEat.AutoEatTrigger(runtime, config),
            new MapMaskTrigger(runtime, config),
            new SkillCdTrigger(runtime, config),
        ];

        return [.. triggers.OrderByDescending(t => t.Priority)];
    }

    /// <summary>获取素材图片并缩放，见 <see cref="GameTaskAssets.LoadAssetImage(string, string, ISystemInfo, ImreadModes)"/></summary>
    public static Mat LoadAssetImage(string featName, string assertName, ISystemInfo systemInfo, ImreadModes flags = ImreadModes.Color) =>
        GameTaskAssets.LoadAssetImage(featName, assertName, systemInfo, flags);

    /// <summary>根据捕获区域宽高加载素材图片并缩放，见 <see cref="GameTaskAssets.LoadAssetImage(string, string, int, int, ImreadModes)"/></summary>
    public static Mat LoadAssetImage(string featName, string assertName, int captureWidth, int captureHeight, ImreadModes flags = ImreadModes.Color) =>
        GameTaskAssets.LoadAssetImage(featName, assertName, captureWidth, captureHeight, flags);
}
