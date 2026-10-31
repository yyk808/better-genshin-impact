using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>左侧导航的一个页面</summary>
public abstract class PageViewModel : ObservableObject
{
    public abstract string Title { get; }

    /// <summary>页面切换到前台时（UI 线程）</summary>
    public virtual void OnNavigatedTo()
    {
    }
}

/// <summary>尚未迁移到 Avalonia 宿主的页面：只显示说明</summary>
public sealed class PlaceholderPageViewModel(string title, string description) : PageViewModel
{
    public override string Title => title;

    public string Description => description;
}

/// <summary>导航项（与 WPF 版 MainWindow.xaml 的 NavigationViewItem 对应）</summary>
/// <param name="title">标题</param>
/// <param name="icon">Fluent System Icons 名称（与 WPF 版 SymbolIcon 相同，如 Play24）</param>
/// <param name="page">页面</param>
/// <param name="onSelected">选中后执行（同一页面的不同选项卡，如“快捷键”“通知”属于设置页）</param>
/// <param name="isAvailable">已在 Avalonia 宿主中实现（未实现的显示为灰色，点开为说明页）</param>
public sealed class NavItem(string title, string icon, PageViewModel page, Action? onSelected = null, bool isAvailable = true)
{
    public string Title => title;

    public string Icon => icon;

    /// <summary>图标字符（Fluent System Icons，与 WPF 版 WPF-UI 的 SymbolIcon 使用同一字体与码位）</summary>
    public string Glyph => BetterGenshinImpact.Avalonia.Services.FluentSymbols.Glyph(icon);

    public PageViewModel Page => page;

    public Action? OnSelected => onSelected;

    public bool IsAvailable => isAvailable;

    public double Opacity => isAvailable ? 1.0 : 0.5;

    /// <summary>子项（WPF 版“全自动”下的调度器、JS 脚本等）</summary>
    public List<NavItem> Children { get; init; } = [];
}
