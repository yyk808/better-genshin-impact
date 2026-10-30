using BetterGenshinImpact.GameTask.AutoFight.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Windows;

/// <summary>
/// E 技能识别结果显示的数据源：AutoFight 识别流程在后台线程写入最新分类结果，
/// 需要显示的模块（如 AutoComboRunTask）订阅 INPC 变更后经 DrawContent 绘制（数据与显示解耦，GameTask 不直接依赖 View）
/// </summary>
public partial class ESkillClassifyViewModel : ObservableObject
{
    /// <summary>全局唯一实例：数据由 AutoFight 识别流程写入，遮罩窗口只读</summary>
    public static ESkillClassifyViewModel Instance { get; } = new();

    /// <summary>最新一次 E 技能分类结果，null 表示无内容（显示端移除文本）</summary>
    [ObservableProperty]
    private ESkillClassifyResult? _result;
}
