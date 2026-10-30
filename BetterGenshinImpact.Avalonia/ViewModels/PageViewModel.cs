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

/// <summary>导航项</summary>
public sealed class NavItem(string title, string iconData, PageViewModel page, bool isAvailable = true)
{
    public string Title => title;

    /// <summary>图标的路径数据（24×24）</summary>
    public string IconData => iconData;

    public PageViewModel Page => page;

    /// <summary>已在 Avalonia 宿主中实现（未实现的显示为灰色，点开为说明页）</summary>
    public bool IsAvailable => isAvailable;

    public double Opacity => isAvailable ? 1.0 : 0.5;
}
