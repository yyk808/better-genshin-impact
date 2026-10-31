using Avalonia.Controls;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>按键绑定设置窗口（对应 WPF KeyBindingsWindow）</summary>
public partial class KeyBindingsWindow : Window
{
    /// <summary>最近打开的窗口（UI 自测用）</summary>
    public static KeyBindingsWindow? Current { get; private set; }

    public KeyBindingsWindow()
    {
        InitializeComponent();
        Opened += (_, _) => Current = this;
        Closed += (_, _) => Current = null;
    }
}
