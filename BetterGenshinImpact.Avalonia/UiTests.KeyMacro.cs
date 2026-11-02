using Avalonia.Controls;
using BetterGenshinImpact.Avalonia.ViewModels;
using BetterGenshinImpact.GameTask.KeyMacro;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.ViewModel.Pages.KeyMacro;

namespace BetterGenshinImpact.Avalonia;

/// <summary>
/// 按键宏页（--ui-test key-macro，对应 WPF KeyMacroPage）：宏目录 User/BgiMacro 的新建 / 改名 / 复制 / 外部修改载入 / 删除到回收站，
/// 脚本诊断，快捷键规则（键鼠监听不收组合键、与软件快捷键冲突），以及在假游戏上执行：单次、按住循环（松开即停）、
/// 切换循环（全局热键 XGrabKey）、带坐标点击、总开关
/// </summary>
internal sealed partial class UiTests
{
    private async Task KeyMacroAsync()
    {
        var page = vm.KeyMacro;
        var store = page.Store;
        var host = page.Host;
        Check("按键宏宿主已创建", store is not null && host is not null);
        if (store is null || host is null) return;
        foreach (var leftover in store.Macros.ToList()) store.Delete(leftover);
        var trash = Path.Combine(Path.GetTempPath(), "bgi-m64-trash");
        if (Directory.Exists(trash)) Directory.Delete(trash, true);
        // 删除走 freedesktop 回收站：自测里指到临时目录，不碰真实回收站
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", trash);

        vm.Navigate("辅助操控");
        await Settle(600);
        await Click(Named<Button>("GoToKeyMacroButton"), "辅助操控页的按键宏入口");
        await Settle(600);
        Check("从辅助操控页进入按键宏页", vm.SelectedNav?.Title == "按键宏" && Named<ListBox>("MacroList") is not null, vm.SelectedNav?.Title);
        Check("空目录显示提示", Named<TextBlock>("EmptyMacrosText") is not null && page.Macros.Count == 0);

        // —— 新建（模板）——
        page.CreateCommand.Execute(KeyMacroTemplates.All.First(t => t.Key == "hold"));
        await Settle();
        var item = page.SelectedMacro;
        var file = Path.Combine(store.DirectoryPath, "按住 E.txt");
        Check("按模板新建并写入 txt 文件", item?.Name == "按住 E" && File.Exists(file) && page.Macros.Count == 1, item?.Name);
        if (item is null) return;
        Check("文件头为 WPF 相同格式", File.ReadAllText(file).Contains("keydown(e)"), File.ReadAllText(file).Replace("\n", "⏎"));
        Check("右侧编辑区显示脚本", Named<TextBox>("MacroScriptBox")?.Text?.Contains("keydown(e)") == true);
        Shot("keymacro-new.png");

        // —— 脚本诊断 ——
        item.Script = "keypress(e)\nfoo(1)\nwait(0.1)";
        item.Reparse();
        await Settle();
        Check("未知指令报错并指出行号", item.HasErrors && item.DiagnosticLines.Any(l => l.Contains("第 2 行")), string.Join(" | ", item.DiagnosticLines));
        Check("状态栏显示错误数", item.StatusText.StartsWith("有 1 处错误"), item.StatusText);
        Shot("keymacro-diagnostics.png");
        item.Script = "keypress(e)\nwait(0.1)\nkeypress(q)";
        item.Reparse();
        await Settle(700);
        Check("修正后语法正确", !item.HasErrors && item.StatusText == "语法正确" && item.SummaryText.StartsWith("3 条指令"), item.SummaryText);
        Check("防抖后写入文件", File.ReadAllText(file).Contains("keypress(q)"));

        // —— 快捷键规则 ——
        item.ModeIndex = 0;
        item.HotkeyTypeIndex = 0;
        Check("键鼠监听不接受组合键", !item.SetHotkey("Ctrl + F8") && item.Hotkey == "" && page.Message.Contains("不支持组合键"), page.Message);
        item.ToggleRecordCommand.Execute(null);
        var recorded = item.OnRecordKey("F8", false, false, false, false);
        Check("录制快捷键 F8", recorded && item.Hotkey == "F8" && !item.IsRecording, item.Hotkey);
        Check("脚本里不能使用快捷键本身", ScriptRejectsHotkey(item));
        var conflict = page.Config.HotKeyConfig.GetType().GetProperties()
            .Where(p => p.PropertyType == typeof(string) && p.Name.EndsWith("Hotkey") && p.GetValue(page.Config.HotKeyConfig) is string { Length: > 0 } v && v != "< None >")
            .Select(p => (string)p.GetValue(page.Config.HotKeyConfig)!).FirstOrDefault(v => !v.Contains('+') && !v.StartsWith("XButton"));
        if (conflict is not null)
        {
            item.SetHotkey(conflict);
            host.Refresh();
            await Settle();
            Check("与软件已有快捷键冲突时不注册", !host.IsRegistered(item.Definition) && item.HotkeyStatus.Contains("冲突"), $"{conflict}：{item.HotkeyStatus}");
            item.SetHotkey("F8");
        }

        host.Refresh();
        await Settle();
        Check("键鼠监听 F8 已注册", host.IsRegistered(item.Definition) && item.HotkeyStatus.StartsWith("已生效"), item.HotkeyStatus);
        Shot("keymacro-registered.png");

        // —— 在假游戏上执行 ——
        var runtime = await WaitFor(() => vm.Host?.RuntimeService.Current);
        Check("截图器已启动（假游戏）", runtime is not null);
        if (runtime is null || dir is null) return;
        var gameLog = Path.Combine(dir, "fakegame.jsonl");
        List<System.Text.Json.JsonElement> Events(long since) => File.ReadAllLines(gameLog).Select(l => System.Text.Json.JsonDocument.Parse(l).RootElement)
            .Where(e => e.GetProperty("t").GetInt64() >= since).ToList();
        long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int Downs(List<System.Text.Json.JsonElement> ev, string key) => ev.Count(e => e.GetProperty("ev").GetString() == "key"
            && string.Equals(e.GetProperty("key").GetString(), key, StringComparison.OrdinalIgnoreCase)
            && e.GetProperty("action").GetString() == "down" && !(e.TryGetProperty("repeat", out var r) && r.GetBoolean()));
        static bool Xdo(string args)
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("xdotool", args) { UseShellExecute = false });
            proc?.WaitForExit(10000);
            return proc?.ExitCode == 0;
        }

        Xdo("search --name ^原神$ windowfocus");
        await Task.Delay(300);

        // 单次：按一下执行一遍
        var t0 = Now();
        Xdo("key F8");
        await Until(() => Downs(Events(t0), "Q") >= 1, 3000);
        await Task.Delay(300);
        var ev0 = Events(t0);
        Check("单次：按 F8 执行一遍（E、Q 各一次）", Downs(ev0, "E") == 1 && Downs(ev0, "Q") == 1, $"E={Downs(ev0, "E")} Q={Downs(ev0, "Q")}");
        Check("单次：执行完后停止", !host.IsRunning(item.Definition));

        // 按住循环：按住时重复执行，松开立即停止
        item.Script = "keypress(e)\nwait(0.05)";
        item.ModeIndex = 1;
        host.Refresh();
        await Settle();
        Check("按住循环：快捷键类型固定为键鼠监听", item.HotkeyTypeIndex == 0 && !item.CanSwitchHotKeyType && host.IsRegistered(item.Definition), item.HotkeyStatus);
        var t1 = Now();
        Xdo("keydown F8 sleep 1 keyup F8");
        await Task.Delay(150);
        var held = Downs(Events(t1), "E");
        await Task.Delay(500);
        var after = Downs(Events(t1), "E");
        Check("按住循环：按住 1 秒内重复执行", held >= 8, $"E={held}");
        Check("按住循环：松开后立即停止", after <= held + 1 && !host.IsRunning(item.Definition), $"松开时 {held}，0.5 秒后 {after}");
        Check("日志记录停止原因（松开快捷键）", UiLogLines().Any(l => l.Contains("按键宏「按住 E」停止：松开快捷键")));

        // 切换循环 + 全局热键（组合键）：按一下开始，再按一下停止
        item.ModeIndex = 2;
        item.HotkeyTypeIndex = 1;
        Check("切换快捷键类型后清空键值", item.Hotkey == "");
        Check("全局热键接受组合键", item.SetHotkey("Ctrl + Shift + F8") && item.Hotkey == "Ctrl + Shift + F8", item.Hotkey);
        host.Refresh();
        await Settle();
        Check("全局热键已注册（XGrabKey）", host.IsRegistered(item.Definition), item.HotkeyStatus);
        var t2 = Now();
        Xdo("key ctrl+shift+F8");
        var started = await Until(() => host.IsRunning(item.Definition), 2000);
        await Task.Delay(800);
        await Settle(100);
        Check("切换循环：开始后列表显示运行中", started && item.IsRunning);
        Shot("keymacro-running.png");
        Xdo("key ctrl+shift+F8");
        var stopped = await Until(() => !host.IsRunning(item.Definition), 2000);
        var n2 = Downs(Events(t2), "E");
        await Task.Delay(400);
        Check("切换循环：再按一下停止", stopped && Downs(Events(t2), "E") == n2 && n2 >= 5, $"E={n2}");
        Check("全局热键不传给游戏", Downs(Events(t2), "F8") == 0);

        // 带坐标点击：按 1920×1080 坐标换算到游戏画面
        item.ModeIndex = 0;
        item.Script = "click(left, 960, 540)";
        host.Refresh();
        await Settle();
        var t3 = Now();
        Xdo("key ctrl+shift+F8");
        await Until(() => Events(t3).Any(e => e.GetProperty("ev").GetString() == "click"), 3000);
        var click = Events(t3).FirstOrDefault(e => e.GetProperty("ev").GetString() == "click");
        var area = runtime.SystemInfo.CaptureAreaRect;
        var ex = (int)(960 * Math.Min(area.Width / 1920.0, area.Height / 1080.0));
        var ey = (int)(540 * Math.Min(area.Width / 1920.0, area.Height / 1080.0));
        var cx = click.ValueKind == System.Text.Json.JsonValueKind.Undefined ? -1 : click.GetProperty("x").GetInt32();
        var cy = click.ValueKind == System.Text.Json.JsonValueKind.Undefined ? -1 : click.GetProperty("y").GetInt32();
        Check("带坐标点击落在游戏画面对应位置", Math.Abs(cx - ex) <= 2 && Math.Abs(cy - ey) <= 2, $"点击 ({cx},{cy})，预期 ({ex},{ey})");

        // 总开关：关闭后全部注销
        page.Enabled = false;
        host.Refresh();
        await Settle();
        Check("总开关关闭后注销快捷键", !host.IsRegistered(item.Definition) && item.HotkeyStatus == "按键宏总开关已关闭", item.HotkeyStatus);
        var t4 = Now();
        Xdo("key ctrl+shift+F8");
        await Task.Delay(600);
        Check("总开关关闭时按快捷键不执行", Events(t4).All(e => e.GetProperty("ev").GetString() != "click"));
        page.Enabled = true;

        // —— 改名、复制、外部修改、删除 ——
        item.EditingName = "连点测试";
        item.RenameCommand.Execute(null);
        await Settle();
        var renamed = Path.Combine(store.DirectoryPath, "连点测试.txt");
        Check("重命名同时重命名文件", item.Name == "连点测试" && File.Exists(renamed) && !File.Exists(file), item.Name);
        item.EditingName = "bad/name";
        item.RenameCommand.Execute(null);
        Check("非法名称拒绝并恢复原名", item.Name == "连点测试" && item.EditingName == "连点测试" && page.Message.Contains("不能包含"), page.Message);
        page.DuplicateCommand.Execute(null);
        await Settle();
        var copy = page.SelectedMacro;
        Check("复制宏（快捷键不复制）", copy is not null && copy.Name == "连点测试 副本" && copy.Hotkey == "" && page.Macros.Count == 2, copy?.Name);
        var external = Path.Combine(store.DirectoryPath, "外部新增.txt");
        File.WriteAllText(external, "// 外部编辑器写入\nkeypress(f)\n");
        var loaded = await Until(() => page.Macros.Any(m => m.Name == "外部新增"), 4000);
        Check("目录监听：外部新增的文件自动载入", loaded && page.Macros.Count == 3);
        File.WriteAllText(renamed, File.ReadAllText(renamed).Replace("click(left, 960, 540)", "keypress(r)"));
        var reloaded = await Until(() => item.Script.Contains("keypress(r)"), 4000);
        Check("目录监听：外部修改后重新载入脚本", reloaded, item.Script.Replace("\n", "⏎"));
        Shot("keymacro-list.png");
        page.SelectedMacro = copy;
        page.DeleteCommand.Execute(null);
        await Settle();
        var trashed = Path.Combine(trash, "Trash", "files", "连点测试 副本.txt");
        var info = Path.Combine(trash, "Trash", "info", "连点测试 副本.txt.trashinfo");
        Check("删除：文件移到回收站并写 .trashinfo", File.Exists(trashed) && File.Exists(info) && !File.Exists(Path.Combine(store.DirectoryPath, "连点测试 副本.txt"))
            && File.ReadAllText(info).Contains("/BgiMacro/" + Uri.EscapeDataString("连点测试 副本.txt") + "\n"), File.Exists(info) ? File.ReadAllText(info).Replace("\n", "⏎") : "无 trashinfo");
        Check("删除后列表减少", page.Macros.Count == 2);

        // 收尾：删除自测创建的宏（回收站在临时目录）
        foreach (var leftover in store.Macros.ToList()) store.Delete(leftover);
        await Settle();
        Check("清理后宏目录为空", Directory.GetFiles(store.DirectoryPath, "*.txt").Length == 0 && page.Macros.Count == 0);
    }

    private static bool ScriptRejectsHotkey(KeyMacroItemViewModel item)
    {
        var original = item.Script;
        item.Script = "keypress(f8)";
        item.Reparse();
        var rejected = item.HasErrors;
        item.Script = original;
        item.Reparse();
        return rejected;
    }
}
