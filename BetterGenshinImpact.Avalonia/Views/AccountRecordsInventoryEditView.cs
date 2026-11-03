using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using BetterGenshinImpact.Host.Common.AccountRecords;
using BetterGenshinImpact.ViewModel.Controls;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>
/// 狛荷屋“新增库存记录 / 修改数量 / 修订字典”对话框内容（同 WPF AccountRecordsInventoryEditDialog）：
/// 名称、分类、补充字典确认、当前实际库存、到期批次（已有到期信息时）、标记上次上报有错误与错误提示
/// </summary>
public sealed class AccountRecordsInventoryEditView : UserControl
{
    public AccountRecordsInventoryEditView(AccountRecordsInventoryEditModel vm)
    {
        DataContext = vm;
        Width = 420;
        static TextBlock Label(string text, double top = 12) => new() { Text = text, Margin = new Thickness(0, top, 0, 6) };
        var panel = new StackPanel { Name = "InventoryEditPanel" };
        panel.Children.Add(Label("物品名称", 0));
        panel.Children.Add(new TextBox
        {
            Name = "EditNameBox", MaxLength = 128, IsEnabled = vm.CanEditName,
            [!TextBox.TextProperty] = new Binding(nameof(vm.Name)) { Mode = BindingMode.TwoWay },
        });
        panel.Children.Add(Label("物品类型（缺失时可补充）"));
        panel.Children.Add(new TextBox
        {
            Name = "EditCategoryBox", MaxLength = 128,
            [!TextBox.TextProperty] = new Binding(nameof(vm.Category)) { Mode = BindingMode.TwoWay },
            [!global::Avalonia.Input.InputElement.IsEnabledProperty] = new Binding(nameof(vm.CanEditCategory)),
        });
        panel.Children.Add(new CheckBox
        {
            Name = "EditAddToDictionaryBox", Content = "确认将此名称添加到补充字典", Margin = new Thickness(0, 10, 0, 0),
            [!ToggleButton.IsCheckedProperty] = new Binding(nameof(vm.AddToDictionary)) { Mode = BindingMode.TwoWay },
            [!global::Avalonia.Input.InputElement.IsEnabledProperty] = new Binding(nameof(vm.NeedsDefinition)),
        });
        panel.Children.Add(Label("游戏中当前实际库存数量"));
        panel.Children.Add(new TextBox
        {
            Name = "EditQuantityBox", MaxLength = 19,
            [!TextBox.TextProperty] = new Binding(nameof(vm.QuantityText)) { Mode = BindingMode.TwoWay },
        });

        var expirations = new StackPanel { Name = "EditExpirationPanel", Margin = new Thickness(0, 12, 0, 0), IsVisible = vm.ShowExpirationEditor };
        expirations.Children.Add(new TextBlock { Text = "到期批次", FontWeight = FontWeight.SemiBold });
        expirations.Children.Add(new TextBlock
        {
            Text = "请核对实际批次（可能包含增量推算）。数量合计不得超过总库存；差额表示未注明到期时间的数量。删除全部批次表示不再保留已知到期信息。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8),
        });
        expirations.Children.Add(new TextBlock { Text = "到期时间格式：2026-11-02T04:00:00+08:00（须带时区）", TextWrapping = TextWrapping.Wrap });
        expirations.Children.Add(new ItemsControl
        {
            Name = "EditExpirationList", ItemsSource = vm.ExpirationRows,
            ItemTemplate = new FuncDataTemplate<AccountRecordsExpirationEditRow>((row, _) =>
            {
                var item = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
                if (row is null) return item;
                item.Children.Add(new TextBlock { Text = "批次数量", Margin = new Thickness(0, 0, 0, 4) });
                var line = new DockPanel();
                var remove = new Button { Content = "删除", Margin = new Thickness(8, 0, 0, 0), Command = vm.RemoveExpirationCommand, CommandParameter = row };
                DockPanel.SetDock(remove, Dock.Right);
                line.Children.Add(remove);
                line.Children.Add(new TextBox { MaxLength = 19, [!TextBox.TextProperty] = new Binding(nameof(row.QuantityText)) { Mode = BindingMode.TwoWay } });
                item.Children.Add(line);
                item.Children.Add(new TextBlock { Text = "到期时间", Margin = new Thickness(0, 4, 0, 4) });
                item.Children.Add(new TextBox { [!TextBox.TextProperty] = new Binding(nameof(row.ExpiresAtText)) { Mode = BindingMode.TwoWay } });
                return item;
            }),
        });
        expirations.Children.Add(new Button { Content = "添加批次", Margin = new Thickness(0, 8, 0, 0), Command = vm.AddExpirationCommand });
        panel.Children.Add(expirations);

        panel.Children.Add(new CheckBox
        {
            Name = "EditMarkIncorrectBox", Content = "修改后，标记上次上报有错误", Margin = new Thickness(0, 12, 0, 0), IsEnabled = vm.CanMarkScanIncorrect,
            [!ToggleButton.IsCheckedProperty] = new Binding(nameof(vm.MarkScanIncorrect)) { Mode = BindingMode.TwoWay },
        });
        panel.Children.Add(new TextBlock
        {
            Text = "请填写游戏中当前实际库存总数，保存后将以此数量为准，并结束相关待处理异常与核实。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Opacity = 0.7,
        });
        panel.Children.Add(new TextBlock
        {
            Name = "EditMessageText", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = Brushes.IndianRed,
            [!TextBlock.TextProperty] = new Binding(nameof(vm.Message)),
        });
        Content = new ScrollViewer { MaxHeight = 520, VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = panel };
    }
}
