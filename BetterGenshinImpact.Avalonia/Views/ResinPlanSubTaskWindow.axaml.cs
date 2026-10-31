using Avalonia.Controls;
using Avalonia.Interactivity;
using BetterGenshinImpact.ViewModel.SoloTaskSettings;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>体力计划子任务设置窗口：确定时校验并给出完整参数，取消丢弃</summary>
public partial class ResinPlanSubTaskWindow : Window
{
    public ResinPlanSubTaskWindow()
    {
        InitializeComponent();
    }

    public ResinPlanSubTaskWindow(ResinPlanSubTaskSettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    /// <summary>当前打开的窗口（界面自测用）</summary>
    public static ResinPlanSubTaskWindow? Current { get; private set; }

    /// <summary>确认后的完整参数；取消时为 null</summary>
    public JObject? Result { get; private set; }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Current = this;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (Current == this) Current = null;
        base.OnClosed(e);
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ResinPlanSubTaskSettingsViewModel vm && vm.TryBuild() is { } result)
        {
            Result = result;
            Close();
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
