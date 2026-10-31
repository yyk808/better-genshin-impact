using Avalonia.Controls;
using Avalonia.Interactivity;
using BetterGenshinImpact.ViewModel.Resin;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>树脂设置对话框：确定时返回 true，取消或关闭丢弃草稿</summary>
public partial class ResinSettingsWindow : Window
{
    public ResinSettingsWindow()
    {
        InitializeComponent();
    }

    public ResinSettingsWindow(IResinSettingsDialogContent content) : this()
    {
        DataContext = content;
        Title = content.Title;
    }

    /// <summary>当前打开的对话框（界面自测用）</summary>
    public static ResinSettingsWindow? Current { get; private set; }

    public bool Confirmed { get; private set; }

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
        if (DataContext is IResinSettingsDialogContent { CanConfirm: true })
        {
            Confirmed = true;
            Close();
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
