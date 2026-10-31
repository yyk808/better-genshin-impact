using System.ComponentModel;
using Avalonia.Controls;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Avalonia.ViewModels;
using FluentAvalonia.UI.Controls;

namespace BetterGenshinImpact.Avalonia.Views;

public partial class MainWindow : Window
{
    private readonly Dictionary<NavItem, FANavigationViewItem> _navItems = new();
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
            IconSource = new FAFontIconSource
            {
                Glyph = nav.Glyph,
                FontFamily = (global::Avalonia.Media.FontFamily)global::Avalonia.Application.Current!.FindResource("FluentSystemIcons")!,
            },
        };
        _navItems[nav] = item;
        return item;
    }

    private void SyncSelection(MainWindowViewModel vm)
    {
        if (vm.SelectedNav is { } nav && _navItems.TryGetValue(nav, out var item) && !ReferenceEquals(RootNavigation.SelectedItem, item))
        {
            _syncing = true;
            RootNavigation.SelectedItem = item;
            _syncing = false;
        }
    }
}
