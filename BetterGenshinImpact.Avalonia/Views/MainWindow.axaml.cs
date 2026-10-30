using Avalonia.Controls;
using BetterGenshinImpact.Avalonia.Services;

namespace BetterGenshinImpact.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        FontFamily = FontInfo.UiFontFamily;
        // 新日志加入后滚动到底部
        UiLogSink.Instance.Flushed += () =>
        {
            if (LogList.ItemCount > 0)
            {
                LogList.ScrollIntoView(LogList.ItemCount - 1);
            }
        };
    }
}
