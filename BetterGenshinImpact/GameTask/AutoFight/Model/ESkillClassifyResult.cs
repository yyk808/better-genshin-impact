using BetterGenshinImpact.Core.Mask;
using System;

namespace BetterGenshinImpact.GameTask.AutoFight.Model;

/// <summary>
/// 一次 E 技能分类识别的结果快照（纯数据，不依赖 View）
/// 坐标均为捕获区域物理像素（绘制域），由写入方（Avatar）完成 1080p 识别域 → 捕获像素域的换算，
/// 消费者直接绘制即可，无需再做坐标域换算
/// </summary>
public class ESkillClassifyResult
{
    /// <summary>识别出的技能状态</summary>
    public SkillCdState State { get; init; }

    /// <summary>分类器输出的编号段（"01"/"02"...），置信度不足或角色不匹配时为 null</summary>
    public string? Code { get; init; }

    /// <summary>识别结果所属角色中文名，供显示端按角色元素着色等用途，可能为 null</summary>
    public string? AvatarName { get; init; }

    /// <summary>E 技能分类裁剪框（捕获像素坐标），可绘制用于对齐检测</summary>
    public MaskRect ClassifyRect { get; init; }

    /// <summary>状态文本的绘制锚点（分类框上方，捕获像素坐标）</summary>
    public MaskPoint TextPosition { get; init; }
}

/// <summary>
/// E 技能分类结果的发布入口：识别流程调用 <see cref="Publish"/>，显示端（主程序的 ESkillClassifyViewModel）由宿主设置 <see cref="Sink"/> 接收
/// </summary>
public static class ESkillClassifyMonitor
{
    /// <summary>结果接收方，未设置时丢弃</summary>
    public static Action<ESkillClassifyResult?>? Sink { get; set; }

    /// <summary>发布最新一次分类结果，null 表示清除显示</summary>
    public static void Publish(ESkillClassifyResult? result) => Sink?.Invoke(result);
}
