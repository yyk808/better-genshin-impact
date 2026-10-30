using Avalonia.Controls;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>遮罩窗口：透明、置顶、不抢焦点；点击穿透由 <see cref="Services.MaskWindowController"/> 用 X11 SHAPE 设置</summary>
public partial class MaskWindow : Window
{
    public MaskWindow()
    {
        InitializeComponent();
    }

    public MaskDrawingLayer DrawingLayer => Drawing;

    public void SetStatus(string text, double height)
    {
        StatusText.Text = text;
        Canvas.SetTop(StatusBox, Math.Max(0, height - 60));
    }

    /// <summary>日志框：显示最近几行日志，为空时隐藏</summary>
    public void SetLog(string? text, double height)
    {
        LogBox.IsVisible = !string.IsNullOrEmpty(text);
        LogText.Text = text;
        Canvas.SetTop(LogBox, Math.Max(0, height * 0.62));
    }
}
