using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BetterGenshinImpact.ViewModel;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>
/// 点位标签搜索窗口（WPF View/Windows/MapLabelSearchWindow）：遮罩不接收键盘，所以点选择面板的搜索框时弹出这个可获得焦点的小窗口
/// （280×64、置顶、无边框），输入框与模型的 MapLabelSearchText 双向绑定（输入即过滤）；Esc 或“X”隐藏，关闭也只是隐藏
/// </summary>
public sealed class MapLabelSearchWindow : Window
{
    private readonly TextBox _box;

    public MapLabelSearchWindow()
    {
        Title = "搜索标点分类";
        Width = 280;
        Height = 64;
        MinWidth = 120;
        MinHeight = 64;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        _box = new TextBox { Height = 32, VerticalContentAlignment = VerticalAlignment.Center, PlaceholderText = "搜索当前区域下的标点分类" };
        _box[!TextBox.TextProperty] = new Binding(nameof(MaskWindowViewModel.MapLabelSearchText)) { Mode = BindingMode.TwoWay };
        CloseButton = new Button { Content = "X", Width = 34, Height = 28, Margin = new Thickness(8, 2, 0, 2), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        CloseButton.Click += (_, _) => Hide();
        var grid = new Grid { Margin = new Thickness(8, 4), ColumnDefinitions = new ColumnDefinitions("*,Auto"), VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(_box);
        Grid.SetColumn(CloseButton, 1);
        grid.Children.Add(CloseButton);
        var border = new Border { CornerRadius = new CornerRadius(8), Child = grid };
        // 同 WPF ApplicationBackgroundBrush：跟随主题的窗口底色
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("SolidBackgroundFillColorBaseBrush"));
        Content = border;
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Hide();
            e.Handled = true;
        }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>“X”按钮（用于验证）</summary>
    public Button CloseButton { get; }

    public void AttachViewModel(MaskWindowViewModel vm) => DataContext = vm;

    /// <summary>聚焦输入框并全选（同 WPF FocusSearch）</summary>
    public void FocusSearch()
    {
        Activate();
        _box.Focus();
        _box.SelectAll();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // 同 WPF：关闭只是隐藏（程序退出时由遮罩控制器真正关闭）
        if (!e.IsProgrammatic || !_allowClose)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    private bool _allowClose;

    /// <summary>真正关闭（遮罩控制器释放时）</summary>
    public void CloseForGood()
    {
        _allowClose = true;
        Close();
    }
}
