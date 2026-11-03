using Avalonia.Controls;
using Avalonia.Input;
using BetterGenshinImpact.Avalonia.ViewModels;
using BetterGenshinImpact.Service.AccountRecords;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>狛荷屋(test)（对应 WPF AccountRecordsPage 的库存记录分页）</summary>
public partial class AccountRecordsPage : UserControl
{
    public AccountRecordsPage()
    {
        InitializeComponent();
        // 同 WPF：双击物品行查看物品详情
        InventoryList.DoubleTapped += (_, e) =>
        {
            if ((e.Source as Control)?.DataContext is InventoryRowSnapshot row && DataContext is AccountRecordsPageViewModel vm)
                vm.ShowDetailsCommand.Execute(row);
        };
    }
}
