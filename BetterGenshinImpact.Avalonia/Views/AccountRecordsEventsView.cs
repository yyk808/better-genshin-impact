using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using BetterGenshinImpact.Avalonia.ViewModels;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>
/// 狛荷屋“事件记录”对话框内容（同 WPF AccountRecordsEventsDialog）：搜索与“仅看错误”、事件列表（右键批量归档）、
/// 选中事件的详情与“原始追溯记录”
/// </summary>
public sealed class AccountRecordsEventsView : UserControl
{
    public AccountRecordsEventsView(AccountRecordsEventsViewModel vm)
    {
        DataContext = vm;
        Width = 820;
        var search = new TextBox
        {
            Name = "EventSearchBox", PlaceholderText = "搜索事件关键词", [!TextBox.TextProperty] = new Binding(nameof(vm.SearchText)) { Mode = BindingMode.TwoWay, Delay = 250 },
        };
        ToolTip.SetTip(search, "搜索物品名、来源、说明、事件 ID 和追溯记录");
        var errorsOnly = new CheckBox
        {
            Name = "EventErrorsOnlyBox", Content = "仅看错误", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            [!CheckBox.IsCheckedProperty] = new Binding(nameof(vm.ErrorsOnly)) { Mode = BindingMode.TwoWay },
        };
        var top = new DockPanel { Margin = new Thickness(4, 0, 4, 8) };
        DockPanel.SetDock(errorsOnly, Dock.Right);
        top.Children.Add(errorsOnly);
        top.Children.Add(search);

        var archive = new MenuItem { Header = "归档列表内同类未受阻的完成事件", Command = vm.ArchiveCompletedEventsCommand };
        ToolTip.SetTip(archive, "以选中事件的类型为准，只处理当前筛选列表内的合法目标。");
        var list = new ListBox
        {
            Name = "EventList", Height = 260,
            [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(vm.Events)),
            [!ListBox.SelectedItemProperty] = new Binding(nameof(vm.Selected)) { Mode = BindingMode.TwoWay },
            ContextMenu = new ContextMenu { Items = { archive } },
            ItemTemplate = new FuncDataTemplate<InventoryEvent>((e, _) =>
            {
                if (e is null) return new TextBlock();
                var panel = new StackPanel();
                var title = new TextBlock { TextWrapping = TextWrapping.Wrap };
                title.Inlines!.Add(new global::Avalonia.Controls.Documents.Run(e.Name) { FontWeight = FontWeight.SemiBold });
                title.Inlines.Add(new global::Avalonia.Controls.Documents.Run("  " + e.TimeRange) { Foreground = Brushes.Gray });
                panel.Children.Add(title);
                panel.Children.Add(new TextBlock { Text = AccountRecordsErrorMessages.ForDisplay(e.Message), TextTrimming = TextTrimming.CharacterEllipsis });
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Opacity = 0.8 };
                row.Children.Add(new TextBlock { Text = AccountRecordsEventsViewModel.StateText(e) });
                row.Children.Add(new TextBlock { Text = e.Archived ? "已归档" : "未归档" });
                panel.Children.Add(row);
                return panel;
            }),
        };

        var details = new TextBox
        {
            Name = "EventDetailsBox", IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            [!TextBox.TextProperty] = new Binding(nameof(vm.Details)) { Mode = BindingMode.OneWay },
        };
        var history = new Expander
        {
            Header = "原始追溯记录", Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new TextBox
            {
                Name = "EventHistoryBox", IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
                [!TextBox.TextProperty] = new Binding(nameof(vm.HistoryDetails)) { Mode = BindingMode.OneWay },
            },
        };
        var error = new TextBlock
        {
            Name = "EventErrorText", Foreground = new SolidColorBrush(Color.Parse("#E05050")), TextWrapping = TextWrapping.Wrap,
            [!TextBlock.TextProperty] = new Binding(nameof(vm.ErrorText)),
            [!IsVisibleProperty] = new Binding(nameof(vm.ErrorText)) { Converter = global::Avalonia.Data.Converters.StringConverters.IsNotNullOrEmpty },
        };

        var root = new StackPanel { Spacing = 8 };
        root.Children.Add(top);
        root.Children.Add(list);
        root.Children.Add(error);
        root.Children.Add(new ScrollViewer { MaxHeight = 260, Content = new StackPanel { Children = { details, history } } });
        Content = root;
    }
}
