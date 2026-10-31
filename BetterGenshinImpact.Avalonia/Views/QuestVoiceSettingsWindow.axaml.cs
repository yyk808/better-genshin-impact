using Avalonia.Controls;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.ViewModel.Pages;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>
/// 自动配音设置窗口（对应 WPF QuestVoiceSettingsWindow）：引擎与声线、角色、识别、高级四页；
/// DataContext 为 Core 的单例设置模型，窗口打开期间作为确认框 / 文件选择的所有者
/// </summary>
public partial class QuestVoiceSettingsWindow : Window
{
    /// <summary>最近打开的设置窗口（UI 自测用）</summary>
    public static QuestVoiceSettingsWindow? Current { get; private set; }

    public QuestVoiceSettingsWindow()
    {
        InitializeComponent();
    }

    public QuestVoiceSettingsWindow(QuestVoiceSettingsViewModel viewModel, AvaloniaQuestVoiceSettingsUi ui) : this()
    {
        DataContext = viewModel;
        Opened += (_, _) =>
        {
            ui.Owner = this;
            Current = this;
        };
        Closed += (_, _) =>
        {
            if (ReferenceEquals(ui.Owner, this)) ui.Owner = null;
            if (ReferenceEquals(Current, this)) Current = null;
        };
    }
}
