using Avalonia.Controls;
using Avalonia.LogicalTree;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;

namespace BetterGenshinImpact.Avalonia;

internal sealed partial class UiTests
{
    /// <summary>
    /// 狛荷屋“事件记录…”（--ui-test account-events，同 WPF AccountRecordsEventsDialog）：在测试 UID 上写入一条识别失败与两次库存上报，
    /// 检查默认只看错误、取消后显示全部、关键词筛选、详情文本、批量归档同类完成事件。只写 User/AccountRecords，结束时删除本测试建的目录
    /// </summary>
    private async Task AccountEventsAsync()
    {
        var page = vm.AccountRecords;
        var root = Global.Absolute(@"User\AccountRecords");
        var createdRoot = !Directory.Exists(root);
        var store = new InventoryStore(new InventoryCatalog(), root);
        const string uid = "100000007";
        try
        {
            await store.AddUidAsync(uid);
            AccountRecordSource Src(string r) => new() { Reference = r, DisplayName = r };
            var now = DateTimeOffset.Now;
            await store.ReportFailureAsync(uid, Src("ui-test-a"), "摩拉", "识别失败");
            await store.ReportAsync(uid, Src("ui-test-scan"), [new InventoryReport { Name = "原石", Quantity = 100, RecordedAt = now.AddMinutes(-3).ToString("O") }]);
            await store.ReportAsync(uid, Src("ui-test-scan"), [new InventoryReport { Name = "原石", Quantity = 120, RecordedAt = now.AddMinutes(-1).ToString("O") }]);
            await store.ReportAsync(uid, Src("ui-test-scan"), [new InventoryReport { Name = "须臾树脂", Quantity = 2, RecordedAt = now.AddMinutes(-1).ToString("O") }]);
            // 库存上报的完成事件随即归档；补两条未归档的同类完成增量事件（同 WPF 单测 BatchArchive 的做法），用来检查批量归档
            var doc0 = (await store.ReadAsync(uid)).Inventory;
            doc0.Events.AddRange([
                new InventoryEvent { Id = "ui-test-delta-a", Name = "原石", Kind = "Delta", State = "Completed", Source = "ui-test-scan", Message = "增量已核实。" },
                new InventoryEvent { Id = "ui-test-delta-b", Name = "须臾树脂", Kind = "Delta", State = "Completed", Source = "ui-test-scan", Message = "增量已核实。" },
            ]);
            File.WriteAllText(Path.Combine(root, uid, "inventory.json"), Newtonsoft.Json.JsonConvert.SerializeObject(doc0));

            vm.Navigate("狛荷屋(test)");
            await Settle(800);
            await page.LoadCommand.ExecuteAsync(null);
            page.SelectedUid = uid;
            await Settle(800);
            var more = Named<DropDownButton>("AccountMoreButton");
            var headers = ((more?.Flyout as MenuFlyout)?.Items.OfType<MenuItem>() ?? []).Select(i => i.Header as string).ToList();
            Check("“更多”菜单：刷新库存后是“事件记录…”（同 WPF）", headers.IndexOf("事件记录…") == headers.IndexOf("刷新库存") + 1, string.Join(",", headers));

            var last = Services.FluentMessageBox.Current;
            var run = page.OpenEventsCommand.ExecuteAsync(null);
            var dialog = await WaitFor(() => Services.FluentMessageBox.Current is { } c && c != last ? c : null);
            await Settle(600);
            var ev = page.LastEvents;
            Check("打开事件记录对话框", dialog is { Title: "事件记录" } && ev is not null);
            if (ev is null) return;
            T? Find<T>(string name) where T : Control => (dialog?.Content as Control)?.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Name == name);
            Check("默认仅看错误：只列出识别失败", ev.ErrorsOnly && ev.Events.Count > 0 && ev.Events.All(e => e.State == "Error") && ev.Selected?.Name == "摩拉",
                string.Join(",", ev.Events.Select(e => $"{e.Name}:{e.State}")));
            Check("详情：事件 ID、物品、来源", ev.Details.Contains("物品：摩拉") && ev.Details.Contains("来源：ui-test-a") && ev.Details.Contains("事件 ID："), ev.Details.Replace("\n", " | "));
            Check("原始追溯记录", ev.HistoryDetails.StartsWith("RepeatCount="), ev.HistoryDetails.Split('\n')[0]);
            var errorsBox = Find<CheckBox>("EventErrorsOnlyBox");
            if (errorsBox is not null) errorsBox.IsChecked = false;
            await Settle(400);
            var all = ev.Events.Select(e => $"{e.Name}:{e.State}:{e.Kind}").ToList();
            Check("取消“仅看错误”后显示全部事件（倒序）", !ev.ErrorsOnly && ev.Events.Any(e => e.Name == "原石") && ev.Events.Count > 1, string.Join(",", all));
            Shot("account-events.png");
            var search = Find<TextBox>("EventSearchBox");
            if (search is not null) search.Text = "原石";
            Check("关键词筛选", await Until(() => ev.Events.Count > 0 && ev.Events.All(e => e.Name == "原石"), 3000), string.Join(",", ev.Events.Select(e => e.Name)));
            if (search is not null) search.Text = "";
            await Until(() => ev.Events.Any(e => e.Name == "须臾树脂"), 3000);
            ev.Selected = ev.Events.FirstOrDefault(e => e is { State: "Completed", Archived: false });
            await Settle(200);
            if (ev.HasCompletedEvents)
            {
                var ids = ev.Events.Where(e => e is { State: "Completed", Archived: false }).Select(e => e.Id).ToList();
                await ev.ArchiveCompletedEventsCommand.ExecuteAsync(null);
                var doc = (await store.ReadAsync(uid)).Inventory;
                Check("批量归档同类完成事件并写入存储", ids.Count >= 2 && ids.All(id => doc.Events.Any(e => e.Id == id && e.Archived)) && ev.ErrorText.Length == 0,
                    $"{ids.Count} {ev.ErrorText}");
            }
            else
            {
                Check("批量归档入口：两条同类未归档完成事件", false, string.Join(",", ev.Events.Select(e => $"{e.Name}:{e.State}:{e.Kind}:{e.Archived}")));
            }

            dialog?.Hide();
            await run;
            await Settle(300);

            // 查看物品详情（行右键 / 双击）
            await Settle(500);
            var row = page.Rows.FirstOrDefault(r => r.Name == "原石");
            Check("库存列表有原石", row is not null && row.Quantity == 120, row?.QuantityText);
            last = Services.FluentMessageBox.Current;
            var details = page.ShowDetailsCommand.ExecuteAsync(row);
            var dd = await WaitFor(() => Services.FluentMessageBox.Current is { } c && c != last ? c : null);
            await Settle(500);
            var recs = page.LastDetails ?? [];
            Check("物品详情：首条为当前库存 120，来源显示名称", dd is { Title: "物品详情" } && recs.Count >= 1 && recs[0] is { Label: "当前库存", Quantity: 120, SourceName: "ui-test-scan" },
                string.Join(" | ", recs.Select(r => $"{r.Label}:{r.Quantity}:{r.SourceName}")));
            Shot("account-item-details.png");
            dd?.Hide();
            await details;
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
            }
        }
    }
}
