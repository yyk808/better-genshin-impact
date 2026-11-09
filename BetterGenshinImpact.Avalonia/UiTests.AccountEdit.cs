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
    /// 狛荷屋“新增库存记录…”与行右键“修改数量 / 修订字典”（--ui-test account-edit，同 WPF AccountRecordsInventoryEditDialog）：
    /// 未收录名称须填分类并确认加入补充字典、数量校验失败时对话框不关闭、已收录名称不可改名、保存后列表刷新。
    /// 只写 User/AccountRecords；结束时删除本测试建的目录，已有目录则恢复补充字典与来源字典并清掉测试 UID
    /// </summary>
    private async Task AccountEditAsync()
    {
        var page = vm.AccountRecords;
        var root = Global.Absolute(@"User\AccountRecords");
        var createdRoot = !Directory.Exists(root);
        string? Keep(string file) => File.Exists(Path.Combine(root, file)) ? File.ReadAllText(Path.Combine(root, file)) : null;
        var overrides0 = Keep("item-overrides.json");
        var sources0 = Keep("sources.json");
        var store = new InventoryStore(new InventoryCatalog(), root);
        const string uid = "100000008";
        const string newName = "界面自测物品甲";
        try
        {
            await store.AddUidAsync(uid);
            await store.ReportAsync(uid, new AccountRecordSource { Reference = "ui-test-scan", DisplayName = "ui-test-scan" },
                [new InventoryReport { Name = "原石", Quantity = 100, RecordedAt = DateTimeOffset.Now.AddMinutes(-2).ToString("O") }]);

            vm.Navigate("狛荷屋(test)");
            await Settle(800);
            await page.LoadCommand.ExecuteAsync(null);
            page.SelectedUid = uid;
            await Settle(800);
            var more = Named<DropDownButton>("AccountMoreButton");
            var headers = ((more?.Flyout as MenuFlyout)?.Items.OfType<MenuItem>() ?? []).Select(i => UiText(i.Header)).ToList();
            Check("“更多”菜单：事件记录后是“新增库存记录…”（同 WPF）", headers.IndexOf("新增库存记录…") == headers.IndexOf("事件记录…") + 1, string.Join(",", headers));

            // 新增：未收录名称
            var last = Services.FluentMessageBox.Current;
            var run = page.AddInventoryCommand.ExecuteAsync(null);
            var dialog = await WaitFor(() => Services.FluentMessageBox.Current is { } c && c != last ? c : null);
            await Settle(600);
            var ed = page.LastEdit;
            T? Find<T>(string name) where T : Control => (dialog?.Content as Control)?.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Name == name);
            Button? Primary() => dialog?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "PrimaryButton");
            Check("打开新增对话框", dialog is { Title: "新增库存记录", PrimaryButtonText: "确认当前库存", CloseButtonText: "取消" } && ed is { IsNew: true, CanEditName: true });
            if (ed is null || dialog is null) return;
            var name = Find<TextBox>("EditNameBox"); var qty = Find<TextBox>("EditQuantityBox"); var cat = Find<TextBox>("EditCategoryBox");
            if (name is not null) name.Text = newName;
            if (qty is not null) qty.Text = "5";
            await Settle(200);
            Check("未收录名称：可填分类、可勾选补充字典", ed.NeedsDefinition && ed.CanEditCategory && Find<CheckBox>("EditAddToDictionaryBox")?.IsEnabled == true);
            Shot("account-edit-new.png");
            await Click(Primary(), "确认当前库存（未确认补充字典）");
            await Settle(300);
            Check("未确认补充字典时不保存、不关闭并提示", Services.FluentMessageBox.Current == dialog && ed.Message.Contains("尚未填写分类") &&
                Find<TextBlock>("EditMessageText")?.Text == ed.Message, ed.Message);
            if (cat is not null) cat.Text = "界面自测分类";
            var add = Find<CheckBox>("EditAddToDictionaryBox");
            if (add is not null) add.IsChecked = true;
            await Click(Primary(), "确认当前库存");
            await run;
            await Settle(500);
            var created = page.Rows.FirstOrDefault(r => r.Name == newName);
            var ws = await store.LoadAsync(uid, false);
            Check("保存后关闭并刷新：新物品数量 5、分类写入补充字典", created is { Quantity: 5, Category: "界面自测分类" } &&
                ws.Definitions.TryGetValue(newName, out var c0) && c0 == "界面自测分类", $"{created?.Quantity} {created?.Category}");

            // 编辑：已收录名称
            var row = page.Rows.FirstOrDefault(r => r.Name == "原石");
            last = Services.FluentMessageBox.Current;
            run = page.EditInventoryCommand.ExecuteAsync(row);
            dialog = await WaitFor(() => Services.FluentMessageBox.Current is { } c && c != last ? c : null);
            await Settle(600);
            ed = page.LastEdit;
            // 原石的分类来自预置补充字典（内置字典无分类），同 WPF 允许修订
            Check("打开编辑对话框：已收录名称不可改名、补充字典分类可修订、数量预填 100", dialog is { Title: "编辑库存记录" } && ed is { IsNew: false, CanEditName: false, NeedsDefinition: false, CanEditCategory: true, QuantityText: "100", Category: "珍稀货币" } &&
                Find<TextBox>("EditNameBox")?.IsEnabled == false && Find<TextBox>("EditCategoryBox")?.IsEnabled == true,
                $"{dialog?.Title} {ed?.IsNew} {ed?.CanEditName} {ed?.NeedsDefinition} {ed?.CanEditCategory} {ed?.QuantityText} {ed?.Category} name={Find<TextBox>("EditNameBox")?.IsEnabled} cat={Find<TextBox>("EditCategoryBox")?.IsEnabled}");
            if (ed is null || dialog is null) return;
            qty = Find<TextBox>("EditQuantityBox");
            if (qty is not null) qty.Text = "1,30";
            await Click(Primary(), "确认当前库存（数量格式错误）");
            await Settle(300);
            Check("数量格式错误时不保存、不关闭", Services.FluentMessageBox.Current == dialog && ed.Message.StartsWith("当前实际库存数量须为"), ed.Message);
            if (qty is not null) qty.Text = "130";
            Shot("account-edit.png");
            await Click(Primary(), "确认当前库存");
            await run;
            await Settle(500);
            row = page.Rows.FirstOrDefault(r => r.Name == "原石");
            Check("修改数量后列表为 130，来源为人工编辑", row is { Quantity: 130 } && row.SourceReference.Contains("SetLocalQuantityAsync"), $"{row?.Quantity} {row?.SourceReference}");

            // 取消不写入
            last = Services.FluentMessageBox.Current;
            run = page.EditInventoryCommand.ExecuteAsync(row);
            dialog = await WaitFor(() => Services.FluentMessageBox.Current is { } c && c != last ? c : null);
            await Settle(400);
            if (page.LastEdit is { } e2) e2.QuantityText = "999";
            dialog?.Hide();
            await run;
            await Settle(300);
            Check("取消后不写入", (await store.LoadAsync(uid, false)).Inventory.Items["原石"][0].Quantity == 130);
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
                Put("item-overrides.json", overrides0);
                Put("sources.json", sources0);
            }
        }
    }
}
