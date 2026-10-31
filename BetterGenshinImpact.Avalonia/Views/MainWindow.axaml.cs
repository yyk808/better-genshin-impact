using System.ComponentModel;
using Avalonia.Controls;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Avalonia.ViewModels;
using FluentAvalonia.UI.Controls;

namespace BetterGenshinImpact.Avalonia.Views;

public partial class MainWindow : Window
{
    private readonly Dictionary<NavItem, FANavigationViewItem> _navItems = new();
    private readonly Dictionary<NavItem, FANavigationViewItem> _parents = new();
    private bool _syncing;

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
        RootNavigation.SelectionChanged += (_, e) =>
        {
            if (!_syncing && e.SelectedItem is FANavigationViewItem { Tag: NavItem nav } && DataContext is MainWindowViewModel vm)
            {
                vm.SelectedNav = nav;
            }
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        // 导航项：主菜单与底部菜单（对应 WPF 版 MenuItems / FooterMenuItems）
        _navItems.Clear();
        _parents.Clear();
        RootNavigation.MenuItems.Clear();
        RootNavigation.FooterMenuItems.Clear();
        foreach (var nav in vm.NavItems)
        {
            RootNavigation.MenuItems.Add(Create(nav));
        }

        foreach (var nav in vm.FooterItems)
        {
            RootNavigation.FooterMenuItems.Add(Create(nav));
        }

        SyncSelection(vm);
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.SelectedNav))
            {
                SyncSelection(vm);
            }
        };
    }

    private FANavigationViewItem Create(NavItem nav)
    {
        var item = new FANavigationViewItem
        {
            Content = nav.Title,
            Tag = nav,
            Opacity = nav.Opacity,
            IconSource = new FAFontIconSource { Glyph = nav.Glyph, FontFamily = FluentSymbols.Font },
        };
        // 子项（WPF 版“全自动”下的调度器、JS 脚本、地图追踪、录制回放）
        foreach (var child in nav.Children)
        {
            item.MenuItems.Add(Create(child));
            _parents[child] = item;
        }

        _navItems[nav] = item;
        return item;
    }

    private void SyncSelection(MainWindowViewModel vm)
    {
        if (vm.SelectedNav is { } nav && _navItems.TryGetValue(nav, out var item) && !ReferenceEquals(RootNavigation.SelectedItem, item))
        {
            _syncing = true;
            // 子项（如“JS 脚本”）选中时展开父项，与 WPF 版相同显示选中标记
            _parents.TryGetValue(nav, out var parent);
            foreach (var other in _parents.Values.Distinct())
            {
                other.IsExpanded = ReferenceEquals(other, parent) || (ReferenceEquals(other, item) && other.IsExpanded);
            }

            RootNavigation.SelectedItem = item;
            _syncing = false;
        }
    }
}
