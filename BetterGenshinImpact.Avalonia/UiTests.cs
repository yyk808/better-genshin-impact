using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using BetterGenshinImpact.Avalonia.ViewModels;
using BetterGenshinImpact.Avalonia.Views;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using Newtonsoft.Json.Linq;
using Serilog;

namespace BetterGenshinImpact.Avalonia;

/// <summary>
/// 宿主内界面自测（--ui-test script-settings | taskplan）：在真实窗口中按名称/文字找到控件，
/// 像用户一样点击按钮、选择下拉、勾选、输入，然后直接读取磁盘上的配置文件核对保存结果。
/// 每项检查在日志中输出“UI 自测 PASS/FAIL”，结束时汇总
/// </summary>
internal sealed class UiTests(MainWindow window, MainWindowViewModel vm, ILogger log, string? dir)
{
    private int _pass;
    private int _fail;

    public async Task RunAsync(string name)
    {
        try
        {
            switch (name)
            {
                case "script-settings": await ScriptSettingsAsync(); break;
                case "taskplan": await TaskPlanAsync(); break;
                default: log.Warning("未知的界面自测 {Name}", name); break;
            }
        }
        catch (Exception e)
        {
            _fail++;
            log.Error(e, "UI 自测异常");
        }

        log.Information("UI 自测 {Name} 结束：PASS {Pass} FAIL {Fail}", name, _pass, _fail);
    }

    // —— 工具 ——

    private void Check(string what, bool ok, object? detail = null)
    {
        if (ok) _pass++;
        else _fail++;
        log.Information("UI 自测 {Result} {What} {Detail}", ok ? "PASS" : "FAIL", what, detail);
    }

    private IEnumerable<T> All<T>() where T : Control => window.GetVisualDescendants().OfType<T>();

    private T? Named<T>(string name) where T : Control => All<T>().FirstOrDefault(c => c.Name == name && c.IsEffectivelyVisible);

    private Button? ButtonByText(string text) => All<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.Content as string == text);

    private static readonly MethodInfo OnClickMethod = typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>与鼠标点击相同：引发 Click 事件并执行按钮命令（禁用的按钮不响应）</summary>
    private async Task Click(Button? button, string what)
    {
        if (button is null || !button.IsEffectivelyEnabled)
        {
            Check($"点击 {what}", false, button is null ? "找不到按钮" : "按钮不可用");
            return;
        }

        OnClickMethod.Invoke(button, null);
        await Settle();
    }

    /// <summary>等待控件出现（界面切换后布局可能稍晚完成），最多 5 秒</summary>
    private static async Task<T?> WaitFor<T>(Func<T?> find) where T : class
    {
        for (var i = 0; i < 50; i++)
        {
            if (find() is { } found) return found;
            await Task.Delay(100);
        }

        return null;
    }

    private static async Task Settle(int ms = 400) => await Task.Delay(ms);

    private void Shot(string file)
    {
        if (dir is null) return;
        Directory.CreateDirectory(dir);
        AvaloniaHostProgram.SaveScreenshotPublic(window, Path.Combine(dir, file), log);
    }

    // —— 脚本设置 ——

    private async Task ScriptSettingsAsync()
    {
        const string groupName = "M35设置自测";
        vm.Navigate("全自动");
        vm.Auto.SelectedTab = 0;
        var page = vm.ScriptControl;
        page.Reload();
        await Settle(1000);
        if (page.ScriptGroups.FirstOrDefault(g => g.Name == groupName) is { } old)
        {
            page.DeleteScriptGroupCommand.Execute(old);
        }

        await Settle();
        page.NewGroupName = groupName;
        await Click(await WaitFor(() => ButtonByText("新增")), "新增配置组");
        var path = ScriptGroupStore.FilePath(groupName);
        Check("新增配置组写入文件", File.Exists(path), path);

        page.SelectedJsScript = "m35demo";
        await Settle();
        var addButtons = All<Button>().Where(b => b.IsEffectivelyVisible && b.Content as string == "添加").ToList();
        await Click(addButtons.FirstOrDefault(), "添加 JS 脚本");
        page.NewShellCommand = "echo m35";
        await Click(addButtons.Skip(2).FirstOrDefault(), "添加 Shell");
        var group = page.SelectedScriptGroup!;
        Check("配置组内项目", group.Projects.Count == 2, string.Join(",", group.Projects.Select(p => p.Type)));

        // 1. JS 自定义设置：默认值写入，修改后保存
        var js = group.Projects.First(p => p.Type == "Javascript");
        await Click(All<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.Content as string == "JS设置"), "JS设置");
        Check("JS 设置面板打开", page.Editor?.Kind == ScriptSettingsKind.JsSettings);
        Shot("avalonia-js-settings-default.png");
        Check("默认值：输入框", Named<TextBox>("js:teamName")?.Text == "默认队伍", Named<TextBox>("js:teamName")?.Text);
        Check("默认值：下拉", Named<ComboBox>("js:mode")?.SelectedItem as string == "标准");
        Check("默认值：勾选", Named<CheckBox>("js:skipBoss")?.IsChecked == true);
        Check("默认值：多选", Named<CheckBox>("js:regions:蒙德")?.IsChecked == true && Named<CheckBox>("js:regions:璃月")?.IsChecked == false);
        Check("默认值：级联", Named<ComboBox>("js:material:1")?.SelectedItem as string == "矿石" && Named<ComboBox>("js:material:2")?.SelectedItem as string == "白铁块");
        Named<TextBox>("js:teamName")!.Text = "M35 队伍";
        Named<ComboBox>("js:mode")!.SelectedItem = "完整";
        Named<CheckBox>("js:skipBoss")!.IsChecked = false;
        Named<CheckBox>("js:regions:稻妻")!.IsChecked = true;
        Named<CheckBox>("js:regions:蒙德")!.IsChecked = false;
        Named<ComboBox>("js:material:1")!.SelectedItem = "特产";
        await Settle();
        Named<ComboBox>("js:material:2")!.SelectedItem = "琉璃袋";
        await Settle();
        Shot("avalonia-js-settings.png");
        await Click(Named<Button>("CloseEditorButton"), "保存并关闭");
        var saved = JObject.Parse(File.ReadAllText(path));
        var settings = saved["projects"]?.FirstOrDefault(p => p.Value<string>("type") == "Javascript")?["jsScriptSettingsObject"] as JObject;
        Check("存盘：JS 输入框", settings?.Value<string>("teamName") == "M35 队伍", settings?.ToString(Newtonsoft.Json.Formatting.None));
        Check("存盘：JS 下拉", settings?.Value<string>("mode") == "完整");
        Check("存盘：JS 勾选", settings?["skipBoss"]?.Type == JTokenType.Boolean && settings.Value<bool>("skipBoss") == false);
        Check("存盘：JS 多选", settings?["regions"] is JArray r && r.Values<string>().SequenceEqual(["稻妻"]));
        Check("存盘：JS 级联", settings?.Value<string>("material") == "琉璃袋");

        // 重新打开：读回已保存的值
        page.Reload();
        await Settle();
        page.SelectedScriptGroup = page.ScriptGroups.First(g => g.Name == groupName);
        await Settle();
        page.OpenJsSettings(page.SelectedScriptGroup.Projects.First(p => p.Type == "Javascript"));
        await Settle();
        Check("重新打开：值保持", Named<TextBox>("js:teamName")?.Text == "M35 队伍" && Named<CheckBox>("js:regions:稻妻")?.IsChecked == true
                              && Named<ComboBox>("js:material:2")?.SelectedItem as string == "琉璃袋");
        await Click(Named<Button>("CloseEditorButton"), "保存并关闭");

        // 2. 项目通用设置
        var jsRow = page.SelectedScriptGroup.Projects.First(p => p.Type == "Javascript");
        await Click(All<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.Content as string == "设置" && b.CommandParameter == jsRow), "通用设置");
        Check("通用设置面板打开", page.Editor?.Kind == ScriptSettingsKind.Project);
        var schedule = Named<ComboBox>("p:Schedule")!;
        schedule.SelectedIndex = ScriptGroupProjectExtensions.ScheduleDescriptions.Keys.ToList().IndexOf("Monday");
        Named<NumericUpDown>("p:RunNum")!.Value = 3;
        Named<ComboBox>("p:AllowJsHTTP")!.SelectedIndex = 0; // 启用
        await Settle();
        Shot("avalonia-project-settings.png");
        await Click(Named<Button>("CloseEditorButton"), "保存并关闭");
        saved = JObject.Parse(File.ReadAllText(path));
        var proj = saved["projects"]!.First(p => p.Value<string>("type") == "Javascript");
        Check("存盘：执行周期", proj.Value<string>("schedule") == "Monday", proj.Value<string>("schedule"));
        Check("存盘：执行次数", proj.Value<int>("runNum") == 3);
        Check("存盘：HTTP 权限 Hash", !string.IsNullOrEmpty(proj.Value<string>("allowJsHTTPHash")), proj.Value<string>("allowJsHTTPHash"));

        // 3. 配置组设置
        await Click(ButtonByText("配置组设置"), "配置组设置");
        Check("配置组设置面板打开", page.Editor?.Kind == ScriptSettingsKind.GroupConfig);
        Shot("avalonia-group-config.png");
        Named<ToggleSwitch>("f:PathingConfig.Enabled")!.IsChecked = true;
        Named<TextBox>("f:PathingConfig.PartyName")!.Text = "M35 队伍";
        Named<ComboBox>("f:PathingConfig.RecoverTiming")!.SelectedIndex = 1; // 只在传送点
        Named<ComboBox>("f:PathingConfig.MainAvatarIndex")!.SelectedIndex = 2; // "2"
        var timeout = Named<TextBox>("f:PathingConfig.AutoFightConfig.Timeout")!;
        timeout.Text = "abc";
        await Settle(150);
        Check("无效数字不写入", page.SelectedScriptGroup.Config.PathingConfig.AutoFightConfig.Timeout != 0, page.SelectedScriptGroup.Config.PathingConfig.AutoFightConfig.Timeout);
        timeout.Text = "150";
        Named<ComboBox>("f:PathingConfig.AutoFightConfig.OnlyPickEliteDropsMode")!.SelectedIndex = 2;
        Named<ToggleSwitch>("f:EnableShellConfig")!.IsChecked = true;
        Named<TextBox>("f:ShellConfig.Timeout")!.Text = "120";
        await Settle();
        // 显示条件：赶路角色选玛薇卡后出现跳飞设置
        Check("跳飞设置默认隐藏", Named<ToggleSwitch>("f:PathingConfig.MwkJumpFlyEnabled") is null);
        Check("仅任务计划节点的项隐藏", Named<ToggleSwitch>("f:PathingConfig.AutoSkipEnabled") is null);
        Named<ComboBox>("f:PathingConfig.HurryOnAvatar")!.SelectedItem = "玛薇卡";
        await Settle();
        Check("选玛薇卡后显示跳飞设置", Named<ToggleSwitch>("f:PathingConfig.MwkJumpFlyEnabled") is not null);
        Named<ComboBox>("f:PathingConfig.HurryOnAvatar")!.BringIntoView();
        await Settle();
        Shot("avalonia-group-config-hurry.png");
        Named<ToggleSwitch>("f:PathingConfig.AutoFightConfig.SwimmingEnabled")?.BringIntoView();
        await Settle();
        Shot("avalonia-group-config-fight.png");
        await Click(Named<Button>("CloseEditorButton"), "保存并关闭");
        var reloaded = ScriptGroup.FromJson(File.ReadAllText(path)).Config;
        Check("存盘：地图追踪配置启用", reloaded.PathingConfig.Enabled);
        Check("存盘：队伍名称", reloaded.PathingConfig.PartyName == "M35 队伍", reloaded.PathingConfig.PartyName);
        Check("存盘：回复时机", reloaded.PathingConfig.RecoverTiming == RecoverTiming.OnlyTeleport, reloaded.PathingConfig.RecoverTiming);
        Check("存盘：行走位", reloaded.PathingConfig.MainAvatarIndex == "2", reloaded.PathingConfig.MainAvatarIndex);
        Check("存盘：战斗超时", reloaded.PathingConfig.AutoFightConfig.Timeout == 150, reloaded.PathingConfig.AutoFightConfig.Timeout);
        Check("存盘：精英掉落模式", reloaded.PathingConfig.AutoFightConfig.OnlyPickEliteDropsMode == "DisableAutoPickupForNonElite");
        Check("存盘：赶路角色", reloaded.PathingConfig.HurryOnAvatar == "玛薇卡");
        Check("存盘：Shell 配置", reloaded.EnableShellConfig && reloaded.ShellConfig.Timeout == 120, reloaded.ShellConfig.Timeout);
        Check("字段目录可解析", ScriptGroupConfigFields.All.All(f => ScriptGroupConfigFields.Resolve(new ScriptGroupConfig(), f.Path, out _, out _)),
            string.Join(",", ScriptGroupConfigFields.All.Where(f => !ScriptGroupConfigFields.Resolve(new ScriptGroupConfig(), f.Path, out _, out _)).Select(f => f.Path)));
        page.Reload();
    }

    // —— 任务计划 ——

    private static JObject ReadPlan(string id) => JObject.Parse(File.ReadAllText(Global.Absolute($@"User\Pulonia\plans\{id}.json")));

    private static JArray Children(JObject plan) => (JArray)plan["root_task"]!["children"]!;

    private async Task TaskPlanAsync()
    {
        const string planName = "M35 界面自测";
        vm.Navigate("任务计划");
        var page = vm.TaskPlan;
        await page.ReloadAsync();
        await Settle(1000);
        foreach (var old in page.Plans.Where(p => p.Name == planName).ToList())
        {
            page.SelectedPlan = old;
            await Settle(300);
            if (page.SelectedPlan == old)
            {
                await Click(await WaitFor(() => ButtonByText("删除计划")), "删除旧计划");
            }
        }

        page.NewPlanName = planName;
        await Click(await WaitFor(() => All<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && ToolTip.GetTip(b) as string == "新建计划")), "新建计划");
        var id = page.SelectedPlan?.Plan.Id ?? "";
        var created = page.SelectedPlan?.Name == planName && File.Exists(Global.Absolute($@"User\Pulonia\plans\{id}.json"));
        Check("新建计划写入文件", created, id);
        if (!created)
        {
            // 没有新建成功时停止，避免改动已有计划
            return;
        }

        // 添加：Shell、录制回放（第一个资源）、分组
        page.SelectedAddOption = page.AddOptions.First(o => o.Definition.TaskType == "shell");
        await Settle();
        await Click(ButtonByText("＋ 添加"), "添加 Shell");
        page.SelectedNode = null;
        page.SelectedAddOption = page.AddOptions.First(o => o.Definition.TaskType == "keymouse");
        await Settle(800);
        await Click(ButtonByText("＋ 添加"), "添加录制回放");
        page.SelectedNode = null;
        await Click(ButtonByText("分组"), "添加分组");
        var kids = Children(ReadPlan(id));
        Check("存盘：添加 3 个任务", kids.Select(k => k.Value<string>("task_type")).SequenceEqual(["shell", "keymouse", "group"]),
            string.Join(",", kids.Select(k => k.Value<string>("task_type"))));
        Shot("avalonia-taskplan-edit.png");

        // 排序：选中第一项点“↓”
        page.SelectedNode = page.Nodes[0];
        await Settle();
        await Click(All<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.Content as string == "↓" && ToolTip.GetTip(b) as string == "下移"), "下移");
        kids = Children(ReadPlan(id));
        Check("存盘：下移", kids.Select(k => k.Value<string>("task_type")).SequenceEqual(["keymouse", "shell", "group"]),
            string.Join(",", kids.Select(k => k.Value<string>("task_type"))));
        Check("移动后保持选中", page.SelectedNode?.Task.TaskType == "shell", page.SelectedNode?.Name);
        await Click(All<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.Content as string == "↑" && ToolTip.GetTip(b) as string == "上移"), "上移");
        kids = Children(ReadPlan(id));
        Check("存盘：上移", kids.Select(k => k.Value<string>("task_type")).SequenceEqual(["shell", "keymouse", "group"]),
            string.Join(",", kids.Select(k => k.Value<string>("task_type"))));

        // 启用：取消勾选第二行
        var list = All<ListBox>().First(l => ReferenceEquals(l.ItemsSource, page.Nodes));
        var rowCheck = list.GetVisualDescendants().OfType<CheckBox>().Skip(1).FirstOrDefault();
        if (rowCheck is not null) rowCheck.IsChecked = false;
        await Settle();
        kids = Children(ReadPlan(id));
        Check("存盘：停用第二项", kids[1].Value<bool>("is_enabled") == false && kids[0].Value<bool>("is_enabled"));

        // 任务参数：Shell 改名、改 file_name 与 arguments
        page.SelectedNode = page.Nodes.First(n => n.Task.TaskType == "shell");
        await Settle();
        Check("任务设置面板", page.NodeEditor is { HasFields: true }, string.Join(",", page.NodeEditor?.Fields.Select(f => f.Name) ?? []));
        Named<TextBox>("NodeNameBox")!.Text = "M35 Shell";
        var fileBox = FieldBox("file_name");
        if (fileBox is not null) fileBox.Text = "/bin/echo";
        var argsBox = FieldBox("arguments");
        if (argsBox is not null) argsBox.Text = "hello\nm35";
        await Settle();
        Shot("avalonia-taskplan-params.png");
        await Click(Named<Button>("ApplyNodeButton"), "应用");
        var shell = Children(ReadPlan(id)).First(k => k.Value<string>("task_type") == "shell");
        Check("存盘：任务名称", shell.Value<string>("name") == "M35 Shell", shell.Value<string>("name"));
        Check("存盘：参数 file_name", shell["parameters"]?.Value<string>("file_name") == "/bin/echo", shell["parameters"]?.ToString(Newtonsoft.Json.Formatting.None));
        Check("存盘：参数 arguments", shell["parameters"]?["arguments"] is JArray a && a.Values<string>().SequenceEqual(["hello", "m35"]));
        // 无效参数：整数字段填文字，不写入
        var intField = page.NodeEditor?.Fields.FirstOrDefault(f => f.Field.Kind == Pulonia.Services.PuloniaParameterFieldKind.Integer);
        if (intField is not null)
        {
            var before = ReadPlan(id).ToString();
            FieldBox(intField.Name)!.Text = "abc";
            await Settle();
            await Click(Named<Button>("ApplyNodeButton"), "应用（无效）");
            Check("无效参数被拒绝且不存盘", page.StatusMessage.StartsWith("设置无效") && ReadPlan(id).ToString() == before, page.StatusMessage);
        }

        // 分组执行次数
        page.SelectedNode = page.Nodes.First(n => n.Task.TaskType == "group");
        await Settle();
        var beforeRepeat = ReadPlan(id).ToString();
        Named<TextBox>("NodeRepeatBox")!.Text = "0";
        await Click(Named<Button>("ApplyNodeButton"), "应用（无效次数）");
        Check("无效执行次数被拒绝且不存盘", page.StatusMessage.StartsWith("设置无效") && ReadPlan(id).ToString() == beforeRepeat, page.StatusMessage);
        Named<TextBox>("NodeRepeatBox")!.Text = "3";
        await Click(Named<Button>("ApplyNodeButton"), "应用（分组）");
        Check("存盘：分组执行次数", Children(ReadPlan(id)).First(k => k.Value<string>("task_type") == "group").Value<int?>("repeat_count") == 3);

        // 删除：删除分组
        page.SelectedNode = page.Nodes.First(n => n.Task.TaskType == "group");
        await Settle();
        await Click(All<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.Content as string == "删除" && b.Command == page.RemoveTaskCommand), "删除任务");
        kids = Children(ReadPlan(id));
        Check("存盘：删除分组", kids.Count == 2 && kids.All(k => k.Value<string>("task_type") != "group"));

        // 触发方式：新增定时、无效表达式、启停、删除
        page.SelectedTab = 2;
        await Settle();
        await Click(Named<Button>("AddScheduleTriggerButton"), "＋ 定时触发");
        Named<TextBox>("TriggerCronBox")!.Text = "99 5 * * *";
        await Settle();
        Check("无效 Cron 不能保存", page.TriggerEditor is { IsValid: false } && Named<Button>("SaveTriggerButton")?.IsEffectivelyEnabled == false, page.TriggerEditor?.PreviewText);
        Named<TextBox>("TriggerCronBox")!.Text = "30 5 * * 1-5";
        Named<CheckBox>("TriggerEnabledBox")!.IsChecked = true;
        await Settle();
        Shot("avalonia-taskplan-trigger.png");
        await Click(Named<Button>("SaveTriggerButton"), "保存触发器");
        var triggers = (JArray)ReadPlan(id)["triggers"]!;
        Check("存盘：定时触发", triggers.Count == 1 && triggers[0].Value<string>("cron") == "30 5 * * 1-5" && triggers[0].Value<bool>("enabled"),
            triggers.ToString(Newtonsoft.Json.Formatting.None));
        var triggerList = All<ListBox>().First(l => ReferenceEquals(l.ItemsSource, page.Triggers));
        var toggle = triggerList.GetVisualDescendants().OfType<ToggleSwitch>().FirstOrDefault();
        if (toggle is not null) toggle.IsChecked = false;
        await Settle();
        triggers = (JArray)ReadPlan(id)["triggers"]!;
        Check("存盘：停用触发", triggers.Count == 1 && !triggers[0].Value<bool>("enabled"));
        // 编辑已有触发：改时间
        page.SelectedTrigger = page.Triggers.FirstOrDefault();
        await Settle();
        Named<TextBox>("TriggerCronBox")!.Text = "0 6 * * *";
        await Settle();
        await Click(Named<Button>("SaveTriggerButton"), "保存触发器（编辑）");
        triggers = (JArray)ReadPlan(id)["triggers"]!;
        Check("存盘：编辑触发", triggers.Count == 1 && triggers[0].Value<string>("cron") == "0 6 * * *");
        Shot("avalonia-taskplan-trigger-list.png");
        page.SelectedTrigger = page.Triggers.FirstOrDefault();
        await Click(All<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.Content as string == "删除" && b.Command == page.DeleteTriggerCommand), "删除触发");
        Check("存盘：删除触发", ((JArray)ReadPlan(id)["triggers"]!).Count == 0);

        // 删除计划
        page.SelectedTab = 0;
        await Settle();
        if (page.SelectedPlan?.Plan.Id == id)
        {
            await Click(ButtonByText("删除计划"), "删除计划");
        }

        Check("删除计划文件", !File.Exists(Global.Absolute($@"User\Pulonia\plans\{id}.json")));
    }

    private TextBox? FieldBox(string name)
        => All<StackPanel>().FirstOrDefault(p => p.Tag as string == name && p.IsEffectivelyVisible)?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
}
