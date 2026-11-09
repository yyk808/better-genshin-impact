using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;

namespace BetterGenshinImpact.Avalonia;

internal sealed partial class UiTests
{
    /// <summary>
    /// 狛荷屋库存行右键其余项与显示偏好（--ui-test account-display，同 WPF AccountRecordsInventoryView / AccountRecordsPage）：
    /// 右键菜单顺序、置顶与分组行号、显示过期时间、优先显示来源名称、拖动栏宽与恢复默认、查看异常、删除此物品记录（含取消）。
    /// 只写 User/AccountRecords；结束时删除本测试建的目录，已有目录则恢复公共设置与来源字典并清掉测试 UID
    /// </summary>
    private async Task AccountDisplayAsync()
    {
        var page = vm.AccountRecords;
        var root = Global.Absolute(@"User\AccountRecords");
        var createdRoot = !Directory.Exists(root);
        string? Keep(string file) => File.Exists(Path.Combine(root, file)) ? File.ReadAllText(Path.Combine(root, file)) : null;
        var settings0 = Keep("settings.json");
        var sources0 = Keep("sources.json");
        var store = new InventoryStore(new InventoryCatalog(), root);
        const string uid = "100000009";
        var last = Services.FluentMessageBox.Current;
        async Task<FluentAvalonia.UI.Controls.FAContentDialog?> NextDialog()
        {
            var d = await WaitFor(() => Services.FluentMessageBox.Current is { } c && c != last ? c : null);
            last = d;
            await Settle(200);
            return d;
        }
        try
        {
            await store.AddUidAsync(uid);
            // 显示设置从默认值开始（到期列隐藏、来源显示名称、不显示行号）
            await store.UpdateSettingsAsync(s => { s.PreferReadableSource = true; s.ShowExpirationColumn = false; s.ShowMissingIconRowNumbers = false; });
            var src = new AccountRecordSource { Reference = "ui-test-ref", DisplayName = "界面自测来源" };
            var now = DateTimeOffset.Now.AddMinutes(-1).ToString("O");
            await store.ReportAsync(uid, src, [
                new InventoryReport { Name = "原石", Quantity = 100, RecordedAt = now },
                new InventoryReport { Name = "摩拉", Quantity = 500, RecordedAt = now },
                new InventoryReport { Name = "须臾树脂", Quantity = 2, RecordedAt = now },
            ]);

            vm.Navigate("狛荷屋(test)");
            await Settle(800);
            await page.LoadCommand.ExecuteAsync(null);
            page.SelectedUid = uid;
            await Until(() => page.Rows.Count == 3, 3000);
            await Settle(500);
            var header = Named<Grid>("InventoryHeader");
            List<Grid> RowGrids() => Named<ItemsControl>("InventoryList")?.GetVisualDescendants().OfType<Grid>().Where(g => g.Classes.Contains("inventoryRow")).ToList() ?? [];
            Grid? RowOf(string name) => RowGrids().FirstOrDefault(g => (g.DataContext as InventoryRowSnapshot)?.Name == name);

            var more = Named<DropDownButton>("AccountMoreButton");
            var items = (more?.Flyout as MenuFlyout)?.Items.OfType<MenuItem>().ToList() ?? [];
            var all = items.Concat(items.SelectMany(i => i.Items.OfType<MenuItem>())).Select(i => UiText(i.Header) ?? "").ToList();
            Check("“更多”菜单：优先显示来源名称、恢复所有分页默认栏宽、高级里的无图像时显示行号",
                new[] { "优先显示来源名称", "恢复所有分页默认栏宽", "无图像时显示行号" }.All(all.Contains), string.Join(",", all));

            // 右键菜单打开后才在行上解析绑定
            async Task<List<string>> MenuOf(string name)
            {
                var grid = RowOf(name);
                if (grid?.ContextMenu is not { } cm) return [];
                cm.Open(grid);
                await Settle(300);
                var headers = cm.Items.OfType<MenuItem>().Select(i => UiText(i.Header) ?? "").ToList();
                cm.Close();
                await Settle(150);
                return headers;
            }
            var menu = await MenuOf("原石");
            Check("行右键菜单顺序同 WPF（图像两项标注未提供）", menu.Count == 9 && menu[0] == "置顶" && menu[1] == "查看物品详情…" && menu[2] == "修改数量 / 修订字典" &&
                menu[3] == "归档同类未受阻的完成事件" && menu[4] == "查看异常" && menu[5] == "显示过期时间" && menu[6].StartsWith("删除图像缓存") && menu[7].StartsWith("上传图标…") &&
                menu[8] == "删除此物品记录", string.Join(",", menu));

            // 显示过期时间（默认隐藏）
            Check("过期时间列默认隐藏", !page.ShowExpirationColumn && header?.ColumnDefinitions[7].Width.Value == 0 && All<TextBlock>().FirstOrDefault(t => t.Name == "ExpirationHeaderText")?.IsVisible == false);
            await page.ToggleExpirationColumnCommand.ExecuteAsync(null);
            await Settle(300);
            Check("开启后显示过期时间列并写入设置", page.ShowExpirationColumn && header?.ColumnDefinitions[7].Width.Value == 150 && RowOf("原石")?.ColumnDefinitions[7].Width.Value == 150 &&
                (await store.GetSettingsAsync()).ShowExpirationColumn, $"{header?.ColumnDefinitions[7].Width} {RowOf("原石")?.ColumnDefinitions[7].Width}");

            // 优先显示来源名称
            Check("默认来源列显示来源名称", page.Rows.All(r => r.SourceName == "界面自测来源"), string.Join(",", page.Rows.Select(r => r.SourceName)));
            await page.ToggleSourceNamesCommand.ExecuteAsync(null);
            await Settle(300);
            Check("关闭后显示来源引用并写入设置", !page.PreferReadableSource && page.Rows.All(r => r.SourceName == "ui-test-ref") && !(await store.GetSettingsAsync()).PreferReadableSource,
                string.Join(",", page.Rows.Select(r => r.SourceName)));
            await page.ToggleSourceNamesCommand.ExecuteAsync(null);
            await Settle(300);

            // 置顶与行号
            await page.TogglePriorCommand.ExecuteAsync(page.Rows.First(r => r.Name == "须臾树脂"));
            await Settle(300);
            await page.ToggleMissingIconRowNumbersCommand.ExecuteAsync(null);
            await Settle(400);
            string Number(Grid g) => g.Children.OfType<TextBlock>().FirstOrDefault(t => t.Classes.Contains("rowNumber"))?.Text ?? "";
            var numbers = page.Rows.Select(r => RowOf(r.Name) is { } g ? Number(g) : "?").ToList();
            Check("置顶后排在最前，置顶行单独编号（1 / 1. 2.）", page.Rows[0] is { Name: "须臾树脂", Prior: true } && string.Join(" ", numbers) == "1 1. 2." &&
                header?.ColumnDefinitions[0].Width.Value == 40, string.Join(" ", numbers));
            var prior = (await MenuOf("须臾树脂")).FirstOrDefault();
            Check("置顶行菜单显示“取消置顶”", prior == "取消置顶", prior);
            Shot("account-display.png");
            await page.TogglePriorCommand.ExecuteAsync(page.Rows.First(r => r.Name == "须臾树脂"));
            await page.ToggleMissingIconRowNumbersCommand.ExecuteAsync(null);
            await Settle(300);
            Check("取消置顶、关闭行号", page.Rows.All(r => !r.Prior) && header?.ColumnDefinitions[0].Width.Value == 0);

            // 栏宽：拖动（直接改表头列宽）同步到行，恢复默认
            if (header is not null) header.ColumnDefinitions[1].Width = new GridLength(260);
            await Settle(300);
            Check("表头栏宽变化同步到行", RowOf("原石")?.ColumnDefinitions[1].Width.Value == 260, RowOf("原石")?.ColumnDefinitions[1].Width.ToString());
            page.ResetColumnWidthsCommand.Execute(null);
            await Settle(300);
            Check("恢复所有分页默认栏宽（保持过期时间列显示）", header?.ColumnDefinitions[1].Width.Value == 180 && RowOf("原石")?.ColumnDefinitions[1].Width.Value == 180 &&
                header?.ColumnDefinitions[7].Width.Value == 150, $"{header?.ColumnDefinitions[1].Width} {header?.ColumnDefinitions[7].Width}");
            await page.ToggleExpirationColumnCommand.ExecuteAsync(null);
            await Settle(200);

            // 查看异常
            page.ShowStatusCommand.Execute(page.Rows.First(r => r.Name == "原石"));
            Check("查看异常：底部提示显示该物品说明", page.StatusText.StartsWith("原石：") && page.StatusText.Contains("核实来源"), page.StatusText.Replace("\n", " | "));

            // 删除此物品记录：取消保留，确认删除
            var del = page.DeleteItemCommand.ExecuteAsync(page.Rows.First(r => r.Name == "摩拉"));
            var confirm = await NextDialog();
            var text = confirm is null ? "" : string.Join("\n", (confirm.Content as Control)?.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text) ?? []);
            Check("删除确认文本同 WPF", confirm is { Title: "删除库存记录" } && text.Contains("删除“摩拉”的库存、异常和快照中的记录？"), text);
            confirm?.Hide(FluentAvalonia.UI.Controls.FAContentDialogResult.None);
            await del;
            await Settle(200);
            Check("取消后保留", page.Rows.Any(r => r.Name == "摩拉"));
            del = page.DeleteItemCommand.ExecuteAsync(page.Rows.First(r => r.Name == "摩拉"));
            (await NextDialog())?.Hide(FluentAvalonia.UI.Controls.FAContentDialogResult.Primary);
            await del;
            await Settle(300);
            Check("确认后删除并写入存储", page.Rows.All(r => r.Name != "摩拉") && !(await store.LoadAsync(uid, false)).Inventory.Items.ContainsKey("摩拉"),
                string.Join(",", page.Rows.Select(r => r.Name)));
        }
        finally
        {
            if (createdRoot)
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
                foreach (var dir in Directory.GetDirectories(Path.GetDirectoryName(root)!, "AccountRecords.backup-*")) Directory.Delete(dir, true);
            }
            else
            {
                await store.ClearAsync(uid, true, true);
                void Put(string file, string? text) { var p = Path.Combine(root, file); if (text is null) File.Delete(p); else File.WriteAllText(p, text); }
                Put("settings.json", settings0);
                Put("sources.json", sources0);
            }
            await page.LoadMaintenanceSettingsAsync();
        }
    }
}
