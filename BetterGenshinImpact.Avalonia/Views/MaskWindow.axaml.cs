using Avalonia.Controls;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>遮罩窗口：透明、置顶、不抢焦点；点击穿透由 <see cref="Services.MaskWindowController"/> 用 X11 SHAPE 设置</summary>
public partial class MaskWindow : Window
{
    public MaskWindow()
    {
        InitializeComponent();
    }

    public void SetStatus(string text, double height)
    {
        StatusText.Text = text;
        Canvas.SetTop(StatusBox, Math.Max(0, height - 60));
    }
}
