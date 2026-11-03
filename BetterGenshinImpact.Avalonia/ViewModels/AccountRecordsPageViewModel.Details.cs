using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>某来源最近一次确认的库存（同 WPF AccountRecordsSourceRecord）</summary>
public sealed record AccountRecordsSourceRecord(string Label, long Quantity, string RecordedAt, string SourceName, string Source, string? ExpirationDetails);

/// <summary>
/// 狛荷屋“查看物品详情…”（同 WPF AccountRecordsItemDetailsDialog）：物品名称、分类与各来源最近一次确认记录，首条为当前库存。
/// 行右键菜单与双击打开
/// </summary>
public partial class AccountRecordsPageViewModel
{
    /// <summary>最近一次打开的物品详情（自测读取）</summary>
    public IReadOnlyList<AccountRecordsSourceRecord>? LastDetails { get; private set; }

    /// <summary>按 WPF 规则生成来源记录：首条“当前库存”，其余“最近来源记录”；按“优先显示来源名称”显示来源</summary>
    public static IReadOnlyList<AccountRecordsSourceRecord> BuildSourceRecords(InventoryRowSnapshot row, IReadOnlyDictionary<string, AccountRecordSource> sources, bool readable) =>
        row.Recent.Select((record, index) => new AccountRecordsSourceRecord(
            index == 0 ? "当前库存" : "最近来源记录", record.Quantity,
            record.RecordedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            readable && sources.TryGetValue(record.Source, out var source) && !string.IsNullOrWhiteSpace(source.DisplayName) ? source.DisplayName : record.Source,
            record.Source, record.Expirations is not { Count: > 0 } ? null : string.Join("\n", record.Expirations.Select(batch =>
                $"{batch.Quantity} 个：{batch.Time.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}")))).ToArray();

    /// <summary>查看物品详情：读取含历史的最新数据再按名称取行（列表只加载当前值）</summary>
    [RelayCommand]
    private async Task ShowDetails(InventoryRowSnapshot? row)
    {
        var uid = SelectedUid;
        if (row == null || uid == null) return;
        try
        {
            var workspace = await _store.ReadAsync(uid);
            var full = (await Task.Run(() => InventoryRowBuilder.Build(workspace, _catalog))).FirstOrDefault(r => r.Name == row.Name) ?? row;
            var settings = await _store.GetSettingsAsync();
            var records = BuildSourceRecords(full, workspace.Sources, settings.PreferReadableSource);
            LastDetails = records;
            if (AvaloniaPickers.MainWindow is not { } owner) return;
            await FluentMessageBox.ShowCustomAsync(new FAContentDialog { Title = "物品详情", Content = DetailsContent(full, records), CloseButtonText = "关闭" }, owner);
        }
        catch (Exception ex)
        {
            StatusText = AccountRecordsErrorMessages.Describe(ex);
        }
    }

    private static Control DetailsContent(InventoryRowSnapshot row, IReadOnlyList<AccountRecordsSourceRecord> records)
    {
        var panel = new StackPanel { Name = "ItemDetailsPanel", Spacing = 8, MinWidth = 460 };
        panel.Children.Add(new TextBlock { Text = row.Name, FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = row.Category, Opacity = 0.7 });
        panel.Children.Add(new TextBlock { Text = "各来源最近一次确认记录，首条为当前库存；不包含待核实增量。", TextWrapping = TextWrapping.Wrap, Opacity = 0.7 });
        if (records.Count == 0) panel.Children.Add(new TextBlock { Text = "尚无有效库存记录。" });
        var list = new StackPanel { Spacing = 8 };
        foreach (var r in records)
        {
            var card = new StackPanel { Spacing = 4 };
            var head = new DockPanel();
            var time = new TextBlock { Text = r.RecordedAt, Opacity = 0.7 };
            DockPanel.SetDock(time, Dock.Right);
            head.Children.Add(time);
            head.Children.Add(new TextBlock { Text = r.Label, FontWeight = FontWeight.SemiBold });
            card.Children.Add(head);
            card.Children.Add(new TextBlock { Text = $"库存：{r.Quantity}" });
            if (!string.IsNullOrEmpty(r.ExpirationDetails)) card.Children.Add(new TextBlock { Text = r.ExpirationDetails, TextWrapping = TextWrapping.Wrap });
            card.Children.Add(new TextBlock { Text = $"来源：{r.SourceName}", TextWrapping = TextWrapping.Wrap });
            card.Children.Add(new SelectableTextBlock { Text = r.Source, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
            list.Children.Add(new Border { Padding = new global::Avalonia.Thickness(12), CornerRadius = new global::Avalonia.CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(0x18, 0x80, 0x80, 0x80)), Child = card });
        }

        panel.Children.Add(new ScrollViewer { MaxHeight = 420, Content = list, HorizontalAlignment = HorizontalAlignment.Stretch });
        return panel;
    }
}
