using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.ViewModel;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>
/// 大地图点位选择（WPF MaskWindow.xaml 的 MapPointPickerRoot）：左下角地球按钮打开 / 关闭选择面板；面板（宽 540、高为遮罩高的 0.62，底边在按钮上方 12）
/// 顶部为数据源、HoYoLab 语言下拉框、搜索框、“当前显示/全部”与全部隐藏 / 全部显示 / 关闭；左侧分类列表（140 宽）、右侧当前分类的标签（160×54 按钮，
/// 图标、名称、点位数），加载中遮一层“加载中...”；面板右侧为已选标签列（重置选择 + 圆形图标，悬停出现红色减号）。
/// 只用基本控件拼出（Wayland 下遮罩内容离屏渲染，没有控件模板），按钮由遮罩命中测试分发（<see cref="MaskButton"/>），列表用 <see cref="MaskScroller"/> 滚动
/// </summary>
public sealed class MapPointPicker : Panel
{
    private static IBrush B(string c) => new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse(c));
    private static readonly IBrush Secondary = B("#C5FFFFFF");
    private static readonly IBrush Tertiary = B("#87FFFFFF");
    private static readonly IBrush PanelBg = B("#C81F232A");
    private static readonly IBrush PanelBorder = B("#26FFFFFF");
    private static readonly IBrush ListBg = B("#B2232831");
    private static readonly IBrush ListBorder = B("#1FFFFFFF");
    private static readonly IBrush ControlBg = B("#0FFFFFFF");
    private static readonly IBrush ControlHover = B("#15FFFFFF");
    private static readonly IBrush ControlBorder = B("#12FFFFFF");
    private static readonly IBrush IconButtonBg = B("#FF2B2F37");
    private static readonly IBrush IconButtonHover = B("#FF353A44");
    private static readonly IBrush Selected = B("#29FFFFFF");
    private static readonly IBrush Hover = B("#14FFFFFF");
    private static readonly BoxShadows Shadow = BoxShadows.Parse("0 0 28 0 #B3000000");

    /// <summary>当前打开的下拉框：null、"provider" 或 "language"</summary>
    private string? _dropDown;

    private string _itemsCategory = "";
    private double _categoriesOffset, _itemsOffset, _selectedOffset;

    public MapPointPicker()
    {
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
    }

    /// <summary>点击搜索框（打开搜索窗口，同 WPF MapLabelSearchTextBox_OnPreviewMouseLeftButtonDown）</summary>
    public Action<MaskButton>? SearchRequested { get; set; }

    // 用于验证与命中测试
    public Border? PanelBorderControl { get; private set; }
    public Border? SelectedColumn { get; private set; }
    public MaskButton? ProviderButton { get; private set; }
    public MaskButton? LanguageButton { get; private set; }
    public MaskButton? SearchBox { get; private set; }
    public MaskButton? HideAllButton { get; private set; }
    public MaskButton? ShowAllButton { get; private set; }
    public MaskButton? CloseButton { get; private set; }
    public MaskButton? ResetButton { get; private set; }
    public IReadOnlyList<MaskButton> DropDownOptions { get; private set; } = [];
    public IReadOnlyList<MaskButton> CategoryButtons { get; private set; } = [];
    public IReadOnlyList<MaskButton> ItemButtons { get; private set; } = [];
    public IReadOnlyList<MaskButton> SelectedButtons { get; private set; } = [];
    public MaskScroller? CategoriesScroller { get; private set; }
    public MaskScroller? ItemsScroller { get; private set; }
    public MaskScroller? SelectedScroller { get; private set; }
    public string SummaryText { get; private set; } = "";
    public string ItemsHeader { get; private set; } = "";
    public bool ShowsLoading { get; private set; }
    public string? OpenDropDown => _dropDown;

    /// <summary>下拉框开关（只影响界面，选中后写回模型）</summary>
    public void ToggleDropDown(string? name) => _dropDown = _dropDown == name ? null : name;

    /// <summary>界面状态（下拉框、滚动位置）的键：变化时需要重建</summary>
    public string StateKey => _dropDown ?? "";

    public void Rebuild(MaskWindowViewModel vm, double height)
    {
        // 记住滚动位置（换分类时标签列表回到顶部）
        if (CategoriesScroller is not null) _categoriesOffset = CategoriesScroller.Offset;
        if (ItemsScroller is not null) _itemsOffset = ItemsScroller.Offset;
        if (SelectedScroller is not null) _selectedOffset = SelectedScroller.Offset;
        var categoryId = vm.SelectedMapLabelCategory?.Id ?? "";
        if (categoryId != _itemsCategory)
        {
            _itemsCategory = categoryId;
            _itemsOffset = 0;
        }

        Children.Clear();
        Height = height;
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Height = height };
        row.Children.Add(BuildPanel(vm, height));
        if (vm.SelectedMapLabelItems.Count > 0)
        {
            row.Children.Add(BuildSelected(vm, height));
        }
        else
        {
            SelectedColumn = null;
            SelectedButtons = [];
            SelectedScroller = null;
            ResetButton = null;
        }

        Children.Add(row);
    }

    /// <summary>重建后恢复滚动位置（排好之后调用）</summary>
    public void RestoreScroll()
    {
        CategoriesScroller?.ScrollTo(_categoriesOffset);
        ItemsScroller?.ScrollTo(_itemsOffset);
        SelectedScroller?.ScrollTo(_selectedOffset);
    }

    private Control BuildPanel(MaskWindowViewModel vm, double height)
    {
        var dock = new DockPanel();
        // 顶部：数据源、HoYoLab 语言、搜索框、计数与按钮
        var head = new DockPanel { Margin = new Thickness(12, 12, 12, 8), Height = 32 };
        ProviderButton = Combo(vm.SelectedMapPointApiProviderOption?.DisplayName ?? "", "provider");
        DockPanel.SetDock(ProviderButton, Dock.Left);
        head.Children.Add(ProviderButton);
        LanguageButton = null;
        if (vm.IsHoYoLabProviderSelected)
        {
            LanguageButton = Combo(vm.SelectedHoYoLabLanguageOption?.DisplayName ?? "", "language");
            DockPanel.SetDock(LanguageButton, Dock.Left);
            head.Children.Add(LanguageButton);
        }

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(tools, Dock.Right);
        SummaryText = vm.MapPointVisibilitySummary;
        tools.Children.Add(new TextBlock { Text = SummaryText, Foreground = Secondary, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        HideAllButton = IconButton("EyeOff24", () => vm.HideAllMapPointsCommand.Execute(null), vm.HasMapPoints);
        HideAllButton.Margin = new Thickness(0, 0, 6, 0);
        ShowAllButton = IconButton("Eye24", () => vm.ShowAllMapPointsCommand.Execute(null), vm.HasHiddenMapPoints);
        ShowAllButton.Margin = new Thickness(0, 0, 6, 0);
        CloseButton = IconButton("Dismiss24", () => _ = vm.ToggleMapPointPickerCommand.ExecuteAsync(null), true);
        tools.Children.Add(HideAllButton);
        tools.Children.Add(ShowAllButton);
        tools.Children.Add(CloseButton);
        head.Children.Add(tools);

        var empty = string.IsNullOrEmpty(vm.MapLabelSearchText);
        SearchBox = new MaskButton
        {
            Height = 32, Background = ControlBg, HoverBackground = ControlHover, BorderBrush = ControlBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 0),
            Child = new TextBlock
            {
                Text = empty ? "搜索当前区域下的标点分类" : vm.MapLabelSearchText, Foreground = empty ? Tertiary : Brushes.White, FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            },
        };
        var search = SearchBox;
        SearchBox.Click = () => SearchRequested?.Invoke(search);
        head.Children.Add(SearchBox);
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);

        // 主体：分类（140）| 标签
        var body = new Grid { Margin = new Thickness(12, 0, 12, 12), ColumnDefinitions = new ColumnDefinitions("140,12,*") };
        var categories = new StackPanel();
        var categoryButtons = new List<MaskButton>();
        foreach (var category in vm.MapLabelCategories)
        {
            var selected = ReferenceEquals(category, vm.SelectedMapLabelCategory);
            var c = category;
            var button = new MaskButton
            {
                Margin = new Thickness(0, 1), Padding = new Thickness(10, 8), CornerRadius = new CornerRadius(4),
                Background = selected ? Selected : Brushes.Transparent, HoverBackground = selected ? Selected : Hover,
                Child = new TextBlock { Text = category.Name, Foreground = Brushes.White, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis },
                // 同 WPF：点已选中的分类不重新加载
                Click = selected ? null : () => _ = vm.SelectMapLabelCategoryCommand.ExecuteAsync(c),
            };
            if (selected && button.Child is { } text)
            {
                // WPF UI ListViewItem 选中时左侧的强调色竖条
                button.Child = null;
                button.Child = new DockPanel
                {
                    Children =
                    {
                        new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(2), Background = B("#FF60CDFF"), Margin = new Thickness(-6, 0, 3, 0), [DockPanel.DockProperty] = Dock.Left },
                        text,
                    },
                };
            }

            categoryButtons.Add(button);
            categories.Children.Add(button);
        }

        CategoryButtons = categoryButtons;
        CategoriesScroller = new MaskScroller { Content = categories };
        var left = new Border { Background = ListBg, BorderBrush = ListBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(8), Child = CategoriesScroller };
        body.Children.Add(left);

        var right = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        ItemsHeader = vm.SelectedMapLabelCategory?.Name ?? "";
        right.Children.Add(new TextBlock { Text = ItemsHeader, Foreground = Secondary, FontSize = 14, Margin = new Thickness(2, 0, 0, 8) });
        var wrap = new WrapPanel();
        var itemButtons = new List<MaskButton>();
        foreach (var item in vm.MapLabelItems)
        {
            var it = item;
            var icon = MapImageCache.TryGet(item.IconUrl);
            var cell = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
            var iconBox = new Border
            {
                Width = 36, Height = 36, CornerRadius = new CornerRadius(8), Background = B("#80232831"), Margin = new Thickness(0, 0, 4, 0),
                Child = icon is null ? null : new Image { Source = icon, Width = 28, Height = 28, Stretch = Stretch.Uniform },
            };
            DockPanel.SetDock(iconBox, Dock.Left);
            var count = new TextBlock { Text = item.PointCount.ToString(), Width = 42, TextAlignment = TextAlignment.Right, Foreground = Secondary, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(count, Dock.Right);
            cell.Children.Add(iconBox);
            cell.Children.Add(count);
            cell.Children.Add(new TextBlock { Text = item.Name, Foreground = Brushes.White, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var button = new MaskButton
            {
                Width = 160, Height = 54, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(8, 0), CornerRadius = new CornerRadius(4),
                Background = B("#B22C323C"), HoverBackground = B("#D23A424F"), BorderBrush = B("#403A424F"), BorderThickness = new Thickness(1),
                Child = cell, Tag = item.Id,
                Click = () => _ = vm.SelectMapLabelItemCommand.ExecuteAsync(it),
            };
            itemButtons.Add(button);
            wrap.Children.Add(button);
        }

        ItemButtons = itemButtons;
        ItemsScroller = new MaskScroller { Content = wrap };
        Grid.SetRow(ItemsScroller, 1);
        right.Children.Add(ItemsScroller);
        ShowsLoading = vm.IsMapLabelTreeLoading;
        if (ShowsLoading)
        {
            var loading = new Border
            {
                Background = B("#AA232831"),
                Child = new TextBlock { Text = "加载中...", Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            Grid.SetRow(loading, 1);
            right.Children.Add(loading);
        }

        var rightBorder = new Border { Background = ListBg, BorderBrush = ListBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(10), Child = right };
        Grid.SetColumn(rightBorder, 2);
        body.Children.Add(rightBorder);
        dock.Children.Add(body);

        // 下拉框展开时的选项（盖在面板上，位于下拉框下方）
        var layer = new Panel();
        layer.Children.Add(dock);
        DropDownOptions = [];
        if (_dropDown is { } open)
        {
            var options = open == "provider"
                ? vm.MapPointApiProviderOptions.Select(o => (o.DisplayName, ReferenceEquals(o, vm.SelectedMapPointApiProviderOption) || o == vm.SelectedMapPointApiProviderOption,
                    (Action)(() => vm.SelectedMapPointApiProviderOption = o))).ToList()
                : vm.HoYoLabLanguageOptions.Select(o => (o.DisplayName, o == vm.SelectedHoYoLabLanguageOption,
                    (Action)(() => vm.SelectedHoYoLabLanguageOption = o))).ToList();
            if (open == "language" && !vm.IsHoYoLabProviderSelected) options.Clear();
            var list = new StackPanel();
            var buttons = new List<MaskButton>();
            foreach (var (name, isSelected, select) in options)
            {
                var b = new MaskButton
                {
                    Height = 32, Padding = new Thickness(12, 0), Margin = new Thickness(0, 1), CornerRadius = new CornerRadius(4),
                    Background = isSelected ? Selected : Brushes.Transparent, HoverBackground = isSelected ? Selected : Hover,
                    Child = new TextBlock { Text = name, Foreground = Brushes.White, FontSize = 14, VerticalAlignment = VerticalAlignment.Center },
                };
                b.Click = () =>
                {
                    _dropDown = null;
                    select();
                };
                buttons.Add(b);
                list.Children.Add(b);
            }

            DropDownOptions = buttons;
            if (buttons.Count > 0)
            {
                layer.Children.Add(new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Width = 180,
                    Margin = new Thickness(open == "provider" ? 12 : 12 + 140 + 12, 12 + 32 + 4, 0, 0),
                    Background = B("#F02B2F37"), BorderBrush = B("#33FFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(4), BoxShadow = BoxShadows.Parse("0 4 16 0 #80000000"), Child = list,
                });
            }
        }

        PanelBorderControl = new Border
        {
            Width = 540, Height = height, Background = PanelBg, BorderBrush = PanelBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            BoxShadow = Shadow, Child = layer,
        };
        return PanelBorderControl;
    }

    private Control BuildSelected(MaskWindowViewModel vm, double height)
    {
        var dock = new DockPanel();
        ResetButton = new MaskButton
        {
            Width = 40, Height = 40, Margin = new Thickness(0, 0, 0, 8), CornerRadius = new CornerRadius(20), HorizontalAlignment = HorizontalAlignment.Center,
            Background = B("#C82C323C"), HoverBackground = B("#E03A424F"), BorderBrush = B("#403A424F"), BorderThickness = new Thickness(1),
            Child = Icon("Broom24", 22),
            Click = () => vm.ResetSelectedMapLabelSelectionCommand.Execute(null),
        };
        DockPanel.SetDock(ResetButton, Dock.Top);
        dock.Children.Add(ResetButton);
        var list = new StackPanel();
        var buttons = new List<MaskButton>();
        foreach (var item in vm.SelectedMapLabelItems)
        {
            var it = item;
            var ring = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1), BorderBrush = B("#4DFFFFFF") };
            if (MapImageCache.TryGet(item.IconUrl) is { } icon)
            {
                ring.Child = new Image { Source = icon, Width = 40, Height = 40, Stretch = Stretch.Uniform, Clip = new EllipseGeometry(new Rect(0, 0, 38, 38)) };
            }

            var minus = new Border
            {
                Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = B("#FF5E41"), HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -1, -1, 0), IsVisible = false,
                BoxShadow = BoxShadows.Parse("0 0 2 0 #80000000"),
                Child = new TextBlock { Text = "−", FontSize = 14, FontWeight = FontWeight.ExtraBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -3, 0, 0) },
            };
            var b = new MaskButton
            {
                Width = 40, Height = 48, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Center, Background = Brushes.Transparent,
                Child = new Panel { Width = 40, Height = 40, Children = { ring, minus } },
                HoverOnly = minus, Tag = item.Id,
                HoverChanged = h => ring.BorderBrush = B(h ? "#99FFFFFF" : "#4DFFFFFF"),
                Click = () => _ = vm.SelectMapLabelItemCommand.ExecuteAsync(it),
            };
            buttons.Add(b);
            list.Children.Add(b);
        }

        SelectedButtons = buttons;
        SelectedScroller = new MaskScroller { Content = list };
        dock.Children.Add(SelectedScroller);
        SelectedColumn = new Border
        {
            Width = 64, Height = height, Margin = new Thickness(12, 0, 0, 0), Background = PanelBg, BorderBrush = PanelBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(8), BoxShadow = Shadow, Child = dock,
        };
        return SelectedColumn;
    }

    private MaskButton Combo(string text, string name)
    {
        var b = new MaskButton
        {
            Width = 140, Height = 32, Margin = new Thickness(0, 0, 12, 0), Padding = new Thickness(12, 0, 8, 0), CornerRadius = new CornerRadius(4),
            Background = ControlBg, HoverBackground = ControlHover, BorderBrush = ControlBorder, BorderThickness = new Thickness(1),
            Child = new DockPanel
            {
                Children =
                {
                    new TextBlock { Text = FluentSymbols.Glyph("ChevronDown16"), FontFamily = FluentSymbols.Font, FontSize = 12, Foreground = Secondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), [DockPanel.DockProperty] = Dock.Right },
                    new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis },
                },
            },
        };
        b.Click = () => ToggleDropDown(name);
        return b;
    }

    private static MaskButton IconButton(string symbol, Action click, bool enabled) => new()
    {
        Width = 32, Height = 32, CornerRadius = new CornerRadius(4), Background = IconButtonBg, HoverBackground = IconButtonHover,
        Child = Icon(symbol, 16), Click = click, Enabled = enabled,
    };

    private static TextBlock Icon(string symbol, double size) => new()
    {
        Text = FluentSymbols.Glyph(symbol), FontFamily = FluentSymbols.Font, FontSize = size, Foreground = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };
}
