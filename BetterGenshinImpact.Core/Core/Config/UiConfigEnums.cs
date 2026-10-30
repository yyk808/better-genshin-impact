namespace BetterGenshinImpact.Core.Config;

// 配置中原来直接使用 WPF / WPF-UI 枚举的属性改用下面的平台无关枚举。
// 成员名与数值与原枚举完全相同：配置文件按数值序列化，读写结果不变；界面侧按数值转换（见主程序 EnumValueConverter）。

/// <summary>图片拉伸方式，与 System.Windows.Media.Stretch 相同</summary>
public enum ImageStretchMode
{
    None = 0,
    Fill = 1,
    Uniform = 2,
    UniformToFill = 3,
}

/// <summary>窗口状态，与 System.Windows.WindowState 相同</summary>
public enum WindowDisplayState
{
    Normal = 0,
    Minimized = 1,
    Maximized = 2,
}

/// <summary>窗口背景材质，与 Wpf.Ui.Controls.WindowBackdropType 相同</summary>
public enum WindowBackdropKind
{
    None = 0,
    Auto = 1,
    Mica = 2,
    Acrylic = 3,
    Tabbed = 4,
}
