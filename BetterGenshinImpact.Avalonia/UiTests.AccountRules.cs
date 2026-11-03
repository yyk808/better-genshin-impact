using Avalonia.Controls;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;

namespace BetterGenshinImpact.Avalonia;

internal sealed partial class UiTests
{
    /// <summary>
    /// 狛荷屋“自定义提醒规则…”（--ui-test account-rules，同 WPF EditWarningRules）：首次打开生成全部禁用的示例规则并交给外部程序（自测替换为记录路径），
    /// 启用一条库存低于阈值的规则后分钟刷新出现提醒，规则文件无效时底部提示且库存照常显示。
    /// 只写 User/AccountRecords；结束时删除本测试建的目录，已有目录则恢复规则文件并清掉测试 UID
    /// </summary>
    private async Task AccountRulesAsync()
    {
        var page = vm.AccountRecords;
        var root = Global.Absolute(@"User\AccountRecords");
        var createdRoot = !Directory.Exists(root);
        var rulesPath = Path.Combine(root, "warning-rules.json");
        var rules0 = File.Exists(rulesPath) ? File.ReadAllText(rulesPath) : null;
        var store = new InventoryStore(new InventoryCatalog(), root);
        const string uid = "100000010";
        var open0 = page.OpenRulesFile;
        try
        {
            if (File.Exists(rulesPath)) File.Delete(rulesPath);
            await store.AddUidAsync(uid);
            await store.ReportAsync(uid, new AccountRecordSource { Reference = "ui-test-scan", DisplayName = "ui-test-scan" },
                [new InventoryReport { Name = "原石", Quantity = 100, RecordedAt = DateTimeOffset.Now.AddMinutes(-1).ToString("O") }]);
            vm.Navigate("狛荷屋(test)");
            await Settle(800);
            await page.LoadCommand.ExecuteAsync(null);
            page.SelectedUid = uid;
            await Until(() => page.Rows.Count == 1, 3000);

            var more = Named<DropDownButton>("AccountMoreButton");
            var advanced = (more?.Flyout as MenuFlyout)?.Items.OfType<MenuItem>().FirstOrDefault(i => i.Header as string == "高级");
            var sub = advanced?.Items.OfType<MenuItem>().ToList() ?? [];
            Check("“高级”首两项：追踪来源提醒（未建设）、自定义提醒规则…（同 WPF）", sub.Count > 2 && sub[0] is { Header: "追踪来源提醒（未建设）", IsEnabled: false } &&
                sub[1].Header as string == "自定义提醒规则…" && ToolTip.GetTip(sub[1]) is string tip && tip.StartsWith("InventoryRules"), string.Join(",", sub.Select(i => i.Header)));

            string? opened = null;
            page.OpenRulesFile = path => opened = path;
            await page.EditWarningRulesCommand.ExecuteAsync(null);
            var doc = File.Exists(rulesPath) ? Newtonsoft.Json.JsonConvert.DeserializeObject<InventoryWarningRule>(File.ReadAllText(rulesPath)) : null;
            Check("首次打开生成示例规则（全部禁用）并交给外部程序", opened == rulesPath && doc is { InventoryRules.Count: > 0 } && doc.InventoryRules.All(r => !r.Enabled),
                $"{opened} {doc?.InventoryRules.Count}");

            // 启用一条“原石低于 200”的规则，分钟刷新后出现提醒
            doc!.InventoryRules.Add(new InventoryThresholdRule { Id = "ui-test-low", Enabled = true, Name = "原石", Kind = "QuantityBelow", Threshold = 200, Message = "界面自测：原石低于 200。" });
            File.WriteAllText(rulesPath, Newtonsoft.Json.JsonConvert.SerializeObject(doc));
            await page.RefreshTimeWarningsCommand.ExecuteAsync(null);
            await Settle(300);
            var row = page.Rows.FirstOrDefault(r => r.Name == "原石");
            Check("刷新后出现阈值提醒", row?.Event is { Kind: "ThresholdWarning" } && row.StatusText.Contains("界面自测：原石低于 200。"), row?.StatusText.Replace("\n", " | "));

            // 规则文件无效：底部提示，库存照常显示
            File.WriteAllText(rulesPath, "{ 无效");
            await page.RefreshTimeWarningsCommand.ExecuteAsync(null);
            await Settle(300);
            Check("规则无效时底部提示且库存照常显示", page.StatusText.StartsWith("提醒规则无效：") && page.Rows.Count == 1, page.StatusText);
            Shot("account-rules.png");
        }
        finally
        {
            page.OpenRulesFile = open0;
            if (createdRoot)
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
                foreach (var dir in Directory.GetDirectories(Path.GetDirectoryName(root)!, "AccountRecords.backup-*")) Directory.Delete(dir, true);
            }
            else
            {
                if (rules0 is null) File.Delete(rulesPath); else File.WriteAllText(rulesPath, rules0);
                await store.ClearAsync(uid, true, true);
            }
        }
    }
}
