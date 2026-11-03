using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using BetterGenshinImpact.Avalonia.ViewModels;
using BetterGenshinImpact.Service.AccountRecords;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>狛荷屋(test)（对应 WPF AccountRecordsPage 的库存记录分页）</summary>
public partial class AccountRecordsPage : UserControl
{
    /// <summary>表头列：0 行号，1 名称，3 分类，5 数量，7 到期，9 记录时间，11 来源；偶数列 2～10 为拖动栏宽的分隔条</summary>
    internal const int RowNumberColumn = 0, ExpirationColumn = 7;
    /// <summary>初次显示时的声明栏宽（“恢复所有分页默认栏宽”恢复到这里，同 WPF AccountRecordsColumnWidthsBehavior）</summary>
    private GridLength[] _defaults = [];
    /// <summary>隐藏“到期”列前的栏宽，重新显示时恢复</summary>
    private GridLength _expirationWidth;
    private AccountRecordsPageViewModel? _vm;
    private bool _syncing;

    public AccountRecordsPage()
    {
        InitializeComponent();
        // 同 WPF：双击物品行查看物品详情
        InventoryList.DoubleTapped += (_, e) =>
        {
            if ((e.Source as Control)?.DataContext is InventoryRowSnapshot row && DataContext is AccountRecordsPageViewModel vm)
                vm.ShowDetailsCommand.Execute(row);
        };
        _defaults = InventoryHeader.ColumnDefinitions.Select(c => c.Width).ToArray();
        _expirationWidth = _defaults[ExpirationColumn];
        foreach (var column in InventoryHeader.ColumnDefinitions)
            column.PropertyChanged += (_, e) => { if (e.Property == ColumnDefinition.WidthProperty) SyncRows(); };
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.PropertyChanged -= OnViewModelChanged;
            _vm = DataContext as AccountRecordsPageViewModel;
            if (_vm != null) _vm.PropertyChanged += OnViewModelChanged;
            ApplyColumnVisibility();
        };
    }

    /// <summary>当前表头栏宽（自测读取）</summary>
    internal IReadOnlyList<GridLength> HeaderWidths => InventoryHeader.ColumnDefinitions.Select(c => c.Width).ToArray();

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AccountRecordsPageViewModel.ShowExpirationColumn):
            case nameof(AccountRecordsPageViewModel.ShowMissingIconRowNumbers):
                ApplyColumnVisibility();
                break;
            case nameof(AccountRecordsPageViewModel.ColumnWidthResetVersion):
                ResetColumnWidths();
                break;
        }
    }

    /// <summary>恢复初始栏宽，保持当前“到期”列与行号列的显示状态</summary>
    private void ResetColumnWidths()
    {
        _syncing = true;
        for (var i = 0; i < _defaults.Length; i++) InventoryHeader.ColumnDefinitions[i].Width = _defaults[i];
        _expirationWidth = _defaults[ExpirationColumn];
        _syncing = false;
        ApplyColumnVisibility();
    }

    /// <summary>“到期”列默认隐藏（同 WPF）；行号列仅在“无图像时显示行号”开启时占位（Linux 版不显示物品图像）</summary>
    private void ApplyColumnVisibility()
    {
        var showExpiration = _vm?.ShowExpirationColumn == true;
        var columns = InventoryHeader.ColumnDefinitions;
        _syncing = true;
        if (!showExpiration && columns[ExpirationColumn].Width.Value > 0) _expirationWidth = columns[ExpirationColumn].Width;
        columns[ExpirationColumn].Width = showExpiration ? _expirationWidth : new GridLength(0);
        columns[RowNumberColumn].Width = _vm?.ShowMissingIconRowNumbers == true ? _defaults[RowNumberColumn] : new GridLength(0);
        _syncing = false;
        ExpirationHeaderText.IsVisible = showExpiration;
        ExpirationSplitter.IsVisible = showExpiration;
        RowNumberHeaderText.IsVisible = _vm?.ShowMissingIconRowNumbers == true;
        SyncRows();
    }

    /// <summary>表头栏宽变化（拖动分隔条、恢复默认、显隐列）后同步到已生成的行</summary>
    private void SyncRows()
    {
        if (_syncing) return;
        foreach (var grid in InventoryList.GetVisualDescendants().OfType<Grid>().Where(g => g.Classes.Contains("inventoryRow")))
            ApplyWidths(grid);
    }

    private void ApplyWidths(Grid row)
    {
        var header = InventoryHeader.ColumnDefinitions;
        for (var i = 0; i < header.Count && i < row.ColumnDefinitions.Count; i++)
            if (row.ColumnDefinitions[i].Width != header[i].Width) row.ColumnDefinitions[i].Width = header[i].Width;
        var showExpiration = _vm?.ShowExpirationColumn == true;
        foreach (var child in row.Children)
            if (Grid.GetColumn(child) == ExpirationColumn) child.IsVisible = showExpiration;
    }

    /// <summary>新生成的库存行按表头栏宽布局</summary>
    private void OnInventoryRowLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is Grid grid) ApplyWidths(grid);
    }
}
