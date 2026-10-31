namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

/// <summary>任务代码用到的秘境选项文字（选择框数据 TrainingGuideDomainOptions 留在界面侧）</summary>
public static class TrainingGuideDomainNames
{
    /// <summary>
    /// “跟随培养计划”的显示文字，用于体力计划的执行记录。
    /// 旧版本选择框曾以它作为选项值，读取移涌活动秘境时仍按跟随培养计划处理。
    /// </summary>
    public const string FollowTrainingPlanOption = "跟随培养计划";

    /// <summary>选择框类型：备选秘境，列出全部秘境。</summary>
    public const string FallbackPicker = "fallback";

    /// <summary>选择框类型：盛材移涌秘境，只列出精通秘境与炼武秘境。</summary>
    public const string MaterialOverflowPicker = "material_overflow";

    /// <summary>选择框类型：祝圣移涌秘境，只列出祝圣秘境。</summary>
    public const string BlessingOverflowPicker = "blessing_overflow";

    /// <summary>选择框类型是否带“秘境来源”（跟随培养计划 / 指定秘境）：移涌活动秘境为空时跟随培养计划。</summary>
    public static bool HasTrainingPlanSource(string? picker) => picker is MaterialOverflowPicker or BlessingOverflowPicker;
}
