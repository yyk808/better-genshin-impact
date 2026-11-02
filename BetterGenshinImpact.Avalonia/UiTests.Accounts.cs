using Avalonia.Controls;
using BetterGenshinImpact.Avalonia.ViewModels;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Microsoft.Extensions.DependencyInjection;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;

namespace BetterGenshinImpact.Avalonia;

/// <summary>账户页（M64）：账户目录与密钥环凭据（pulonia-accounts），在假游戏登录界面上切换账户（account-switch）</summary>
internal sealed partial class UiTests
{
    private const string UidA = "100000001", UidB = "100000002", UserB = "bob@example.com", PassB = "pw-b-123", ListB = "bob****0002";

    private static string AccountsJson => Path.Combine(Global.Absolute(@"User\Pulonia"), "accounts.json");

    /// <summary>回答下一个消息框（Primary = 是 / 确定）</summary>
    private async Task AnswerDialogAsync(FluentAvalonia.UI.Controls.FAContentDialogResult result, string what)
    {
        var dialog = await WaitFor(() => Services.FluentMessageBox.Current);
        Check("弹出确认框：" + what, dialog is not null, dialog?.Title);
        dialog?.Hide(result);
        await Settle(200);
    }

    private async Task<bool> WaitIdle(AccountsPageViewModel page, int timeoutMs = 10000) => await Until(() => !page.IsBusy, timeoutMs);

    /// <summary>新建两个账户（A 当前、B 账号密码），保存；返回 B</summary>
    private async Task<(PuloniaAccountItemViewModelRef A, PuloniaAccountItemViewModelRef B)?> SetupAccountsAsync(AccountsPageViewModel page, bool expectKeyring)
    {
        vm.Navigate("账户");
        await Settle(1000);
        await WaitIdle(page);
        Check("账户页显示", Named<Button>("AddAccountButton") is not null && Named<ComboBox>("CurrentAccountBox") is not null);
        if (page.Accounts.Count == 0) { Check("默认账户", false); return null; }
        var a = page.Accounts[0];
        a.Name = "账号A";
        a.Uid = UidA;
        a.ListText = "138****0001";
        a.LoginMode = PuloniaGenshinAccountLoginMode.ListText;
        await Click(Named<Button>("AddAccountButton"), "新增账户");
        var b = page.SelectedAccount;
        Check("新增账户草稿并选中", b is not null && b != a && page.Accounts.Count == 2, page.Accounts.Count);
        if (b is null) return null;
        b.Name = "账号B";
        b.Uid = UidB;
        page.SelectedDetailTabIndex = 1;
        await Settle();
        page.SelectedLoginMode = page.LoginModes.First(m => m.Key == PuloniaGenshinAccountLoginMode.Password);
        b.ListText = ListB;
        b.LoginUsername = UserB;
        b.LoginPassword = PassB;
        await Settle();
        Check("多个账户时显示账户列表", Named<ListBox>("AccountList") is not null);
        Check("登录资料页：密码框掩码", Named<TextBox>("LoginPasswordBox") is { PasswordChar: '●' });
        Shot("accounts-login.png");
        await Click(Named<Button>("SaveAccountsButton"), "保存");
        await WaitIdle(page, 30000);
        await Settle(600);
        var json = File.Exists(AccountsJson) ? File.ReadAllText(AccountsJson) : "";
        Check("accounts.json 有两个账户、不含密码", json.Contains(UidA) && json.Contains(UidB) && !json.Contains(PassB) && !json.Contains(UserB), json.Length);
        var store = vm.Host!.Services.GetRequiredService<PuloniaTaskStore>();
        var saved = (await store.LoadAccountSettingsAsync()).Accounts.FirstOrDefault(x => x.Uid == UidB);
        if (expectKeyring)
        {
            Check("保存成功（密钥环可用）", page.Message == "账户目录已保存。" && !page.HasUnsavedChanges, page.Message);
            var credential = saved?.CredentialReference is { } reference ? store.CredentialVault.Read(reference) : null;
            Check("凭据写入系统密钥环并可读回", credential is { UserName: UserB, Password: PassB }, credential?.ToString());
            Check("凭据 ToString 不含密码", credential?.ToString().Contains(PassB) == false);
        }
        else
        {
            Check("没有密钥环：目录保存但提示凭据未保存", page.Message.Contains("没有可用的系统密钥环") && page.Message.Contains("不会把账户密码以明文保存"), page.Message);
            Check("没有密钥环：保留密码草稿以便重试", b.LoginPassword == PassB && b.IsDirty);
            // 与 Windows 相同：目录先保存（记下凭据引用），凭据写入失败只提示；凭据不会落到任何明文文件里
            string? readBack;
            try { readBack = saved?.CredentialReference is { } r ? store.CredentialVault.Read(r)?.Password : null; }
            catch (Exception e) { readBack = null; log.Information("UI 自测 没有密钥环时读取凭据：{Message}", e.Message); }
            Check("没有密钥环：凭据没有保存到任何地方", readBack is null
                && !Directory.EnumerateFiles(Global.Absolute(@"User\Pulonia"), "*", SearchOption.AllDirectories).Any(f => File.ReadAllText(f).Contains(PassB)));
            Check("页面说明密钥环不可用", !page.CredentialStoreAvailable && page.CredentialStoreText.Contains("没有可用的系统密钥环"), page.CredentialStoreText);
        }
        return (new(a), new(b));
    }

    /// <summary>账户目录与凭据（--ui-test pulonia-accounts；BGI_TEST_EXPECT_KEYRING=0 时验证没有密钥环的提示）</summary>
    private async Task PuloniaAccountsAsync()
    {
        var page = vm.Accounts;
        var expectKeyring = Environment.GetEnvironmentVariable("BGI_TEST_EXPECT_KEYRING") != "0";
        await page.ProbeCredentialStoreAsync();
        Check("凭据存储探测", page.CredentialStoreAvailable == expectKeyring, page.CredentialStoreText);
        var setup = await SetupAccountsAsync(page, expectKeyring);
        if (setup is not { } s) return;
        var a = s.A.Item;
        var b = s.B.Item;

        // 本实例当前账户：选择即保存
        var service = vm.Host!.Services.GetRequiredService<IPuloniaAccountService>();
        page.CurrentAccountChoice = page.CurrentAccountOptions.First(o => o.Id == b.Id);
        await Until(() => page.Message.StartsWith("本实例当前账户已改为"), 3000);
        Check("当前账户改为账号B并保存", await service.GetCurrentAccountIdAsync(service.CurrentInstanceKey) == b.Id, page.Message);

        // 任务计划：运行账户
        const string planName = "M64 账号计划";
        vm.Navigate("任务计划");
        var plans = vm.TaskPlan;
        await plans.ReloadAsync();
        await Settle(600);
        foreach (var old in plans.Plans.Where(p => p.Name == planName).ToList()) { plans.SelectedPlan = old; await plans.DeletePlanCommand.ExecuteAsync(null); }
        plans.NewPlanName = planName;
        await plans.NewPlanCommand.ExecuteAsync(null);
        await Settle();
        var id = plans.SelectedPlan?.Plan.Id;
        try
        {
            Check("运行账户入口显示（多个账户）", plans.ShowRunAccountEntry && Named<ComboBox>("RunAccountBox") is not null);
            Check("运行账户选项：跟随当前 + 两个账户", plans.RunAccountOptions.Count == 3 && plans.RunAccountOptions[0].Value == "跟随当前账户（不切换）",
                string.Join("|", plans.RunAccountOptions.Select(o => o.Value)));
            plans.SelectedRunAccount = plans.RunAccountOptions.First(o => o.Key == a.Id);
            await Settle(600);
            var planJson = id is null ? "" : File.ReadAllText(Path.Combine(Global.Absolute(@"User\Pulonia"), "plans", id + ".json"));
            Check("运行账户写入计划文件", planJson.Contains(a.Id), planJson.Length);
            Shot("taskplan-run-account.png");
            // 账户页概况：指定了该账户的计划
            vm.Navigate("账户");
            page.SelectedAccount = page.Accounts.First(x => x.Id == a.Id);
            await Until(() => page.PlanUsages.Any(u => u.PlanName == planName), 3000);
            Check("概况列出指定了该账户的计划", page.PlanUsages.Any(u => u.PlanName == planName));
            Check("删除被计划指定的账户被拒绝", await vm.Host!.Services.GetRequiredService<IPuloniaAccountService>().CheckAccountRemovalAsync(a.Id) is not null);
            page.OpenPlanCommand.Execute(page.PlanUsages.First(u => u.PlanName == planName));
            await Settle(600);
            Check("打开计划：转到任务计划并选中", vm.CurrentPage == vm.TaskPlan && plans.SelectedPlan?.Plan.Id == id);
        }
        finally
        {
            plans.SelectedPlan = plans.Plans.FirstOrDefault(p => p.Plan.Id == id);
            if (plans.SelectedPlan is not null) await plans.DeletePlanCommand.ExecuteAsync(null);
        }

        // 新一条龙：切换账号步骤
        vm.Navigate("新一条龙");
        await Settle(800);
        Check("新一条龙“切换账号”步骤显示运行账户", Named<Border>("AccountStepCard") is not null && Named<ComboBox>("OneDragonRunAccountBox") is not null);
        Shot("newonedragon-account-step.png");

        // 删除账号B（确认），凭据一并删除
        vm.Navigate("账户");
        await Settle(600);
        page.CurrentAccountChoice = page.CurrentAccountOptions.First(o => o.Id == a.Id);
        await Settle(600);
        var store = vm.Host!.Services.GetRequiredService<PuloniaTaskStore>();
        var reference = (await store.LoadAccountSettingsAsync()).Accounts.FirstOrDefault(x => x.Id == b.Id)?.CredentialReference;
        page.SelectedAccount = page.Accounts.First(x => x.Id == b.Id);
        var delete = page.DeleteAccountCommand.ExecuteAsync(null);
        await AnswerDialogAsync(FluentAvalonia.UI.Controls.FAContentDialogResult.Primary, "删除账户");
        await delete;
        await Settle(600);
        Check("删除账号B", page.Accounts.Count == 1 && !File.ReadAllText(AccountsJson).Contains(UidB), page.Message);
        if (expectKeyring) Check("删除账户时一并删除密钥环凭据", reference is not null && store.CredentialVault.Read(reference) is null);
        Shot("accounts-after-delete.png");
    }

    /// <summary>
    /// 在 Wine 运行的假 YuanShen.exe 登录界面上切换账户（--ui-test account-switch，需 --auto-start；
    /// 假游戏世界 m64/world-login 以账号A 登录）：环境检查、读取 UID、密码登录、采集图片、图片登录、列表文字登录、按计划运行账户、UID 核验失败、密码错误、密钥环锁定时超时
    /// </summary>
    private async Task AccountSwitchAsync()
    {
        var page = vm.Accounts;
        var host = vm.Host!;
        var setup = await SetupAccountsAsync(page, expectKeyring: true);
        if (setup is not { } s) return;
        var a = s.A.Item;
        var b = s.B.Item;
        page.CurrentAccountChoice = page.CurrentAccountOptions.First(o => o.Id == a.Id);
        await Settle(600);

        // 1. 运行环境检查：本机 XTest 输入 + Wine 下的 YuanShen.exe（_NET_WM_PID → /proc）
        var coordinator = host.Services.GetRequiredService<PuloniaGameTaskCoordinator>();
        await Until(() => host.RuntimeService.Current is not null, 20000);
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
        using (var lease = await coordinator.AcquireAsync(cts.Token))
        {
            var reason = BetterGenshinImpact.Pulonia.Services.PuloniaGenshinAccountSwitcher.DefaultEnvironmentFactory().GetUnsupportedReason(lease.Runtime);
            Check("运行环境检查通过（XTest + Wine YuanShen.exe）", reason is null, $"{reason} pid={lease.Runtime.Window.ProcessId} input={lease.Runtime.Input.GetType().Name}");
        }

        // 2. 读取 UID
        page.SelectedAccount = page.Accounts.First(x => x.Id == a.Id);
        page.SelectedDetailTabIndex = 0;
        a.Uid = "";
        await Click(Named<Button>("ReadUidButton"), "从当前游戏读取");
        await WaitIdle(page, 30000);
        Check("从游戏读取 UID", a.Uid == UidA && page.Message.Contains("已读取 UID"), page.Message);
        // 读回的 UID 与原值相同时没有未保存的修改，“保存”不可用属正常
        if (Named<Button>("SaveAccountsButton") is { IsEffectivelyEnabled: true } save) await Click(save, "保存");
        await WaitIdle(page);

        async Task<bool> SwitchTo(PuloniaAccountItemViewModel target, string expectInMessage, string what)
        {
            page.SelectedAccount = target;
            page.SelectedDetailTabIndex = 1;
            await Settle();
            var task = page.TestSwitchCommand.ExecuteAsync(null);
            await AnswerDialogAsync(FluentAvalonia.UI.Controls.FAContentDialogResult.Primary, "测试切换 " + what);
            await task;
            var ok = page.Message.Contains(expectInMessage);
            Check(what, ok, page.Message);
            return ok;
        }

        // 3. 密码登录切到账号B（凭据来自密钥环，Ctrl+V 粘贴到登录表单）
        await SwitchTo(b, $"UID {UidB} 核验通过", "密码登录切到账号B 并核验 UID");
        var service = host.Services.GetRequiredService<IPuloniaAccountService>();
        Check("切换后本实例当前账户为账号B", await service.GetCurrentAccountIdAsync(service.CurrentInstanceKey) == b.Id);
        Shot("switch-1-password.png");

        // 4. 采集账号B 的账户图片，改用图片登录
        page.SelectedAccount = b;
        var capture = page.CaptureImageCommand.ExecuteAsync(null);
        await AnswerDialogAsync(FluentAvalonia.UI.Controls.FAContentDialogResult.Primary, "采集账户图片");
        await capture;
        Check("采集账号B 图片（回到主界面并核验 UID）", b.ImagePath is { Length: > 0 } && page.Message.StartsWith("采集完成"), page.Message);
        page.SelectedLoginMode = page.LoginModes.First(m => m.Key == PuloniaGenshinAccountLoginMode.Image);
        await Click(Named<Button>("SaveAccountsButton"), "保存");
        await WaitIdle(page);

        // 5. 列表文字切回账号A
        await SwitchTo(a, $"UID {UidA} 核验通过", "列表文字切到账号A 并核验 UID");

        // 6. 图片登录切到账号B
        await SwitchTo(b, $"UID {UidB} 核验通过", "账户图片切到账号B 并核验 UID");
        Shot("switch-2-image.png");

        // 7. 按计划的运行账户运行：计划指定账号A，运行前切换并核验
        const string planName = "M64 切换计划";
        vm.Navigate("任务计划");
        var plans = vm.TaskPlan;
        await plans.ReloadAsync();
        await Settle(600);
        foreach (var old in plans.Plans.Where(p => p.Name == planName).ToList()) { plans.SelectedPlan = old; await plans.DeletePlanCommand.ExecuteAsync(null); }
        plans.NewPlanName = planName;
        await plans.NewPlanCommand.ExecuteAsync(null);
        await Settle();
        var id = plans.SelectedPlan?.Plan.Id;
        // 需要游戏会话的节点才做账户准备：用一个空的录制回放（文件名排在最前，添加时默认选中）
        var noop = Path.Combine(Global.Absolute(@"User\KeyMouseScript"), "000-m64-noop.json");
        Directory.CreateDirectory(Path.GetDirectoryName(noop)!);
        await File.WriteAllTextAsync(noop, "{\"macroEvents\":[],\"info\":{\"x\":0,\"y\":0,\"width\":1920,\"height\":1080,\"recordDpi\":1.0,\"startTime\":\"2026-11-03 00:00:00\",\"startTimeUnixTimestamp\":0}}");
        try
        {
            plans.SelectedAddOption = plans.AddOptions.First(o => o.Definition.TaskType == "keymouse");
            await Settle(800);
            await Click(ButtonByText("＋ 添加"), "添加录制回放（空）");
            plans.SelectedRunAccount = plans.RunAccountOptions.First(o => o.Key == a.Id);
            await Settle(600);
            var logStart = UiLogLines().Count;
            await plans.RunPlanAsync(plans.SelectedPlan);
            var verified = await Until(() => UiLogLines().Skip(logStart).Any(l => l.Contains("账户准备：账户已核验")), 150000);
            await Until(() => !plans.IsSelectedPlanRunning, 60000);
            Check("计划指定账号A：运行前切换并核验 UID", verified && await service.GetCurrentAccountIdAsync(service.CurrentInstanceKey) == a.Id,
                string.Join(" / ", UiLogLines().Skip(logStart).Where(l => l.Contains("账户准备")).Take(6)));
        }
        finally
        {
            plans.SelectedPlan = plans.Plans.FirstOrDefault(p => p.Plan.Id == id);
            if (plans.SelectedPlan is not null) await plans.DeletePlanCommand.ExecuteAsync(null);
            File.Delete(noop);
        }
        vm.Navigate("账户");
        await Settle(600);

        // 8. UID 核验失败：账号C 的列表文字指向账号B 的登录项，切换后读到的 UID 不是 C
        await Click(Named<Button>("AddAccountButton"), "新增账户C");
        var c = page.SelectedAccount!;
        c.Name = "账号C";
        c.Uid = "100000003";
        c.ListText = ListB;
        page.SelectedLoginMode = page.LoginModes.First(m => m.Key == PuloniaGenshinAccountLoginMode.ListText);
        await Click(Named<Button>("SaveAccountsButton"), "保存");
        await WaitIdle(page);
        await SwitchTo(c, "无法完整核验目标 UID", "UID 核验失败：读到的 UID 与目标不符时停止");
        Check("核验失败后当前账户改为“未确定”", await service.GetCurrentAccountIdAsync(service.CurrentInstanceKey) is null);

        // 9. 密码错误：识别“账号或密码错误”后停止，不重复提交
        page.SelectedAccount = b;
        page.SelectedLoginMode = page.LoginModes.First(m => m.Key == PuloniaGenshinAccountLoginMode.Password);
        b.LoginUsername = UserB;
        b.LoginPassword = "wrong-pw";
        await Click(Named<Button>("SaveAccountsButton"), "保存");
        await WaitIdle(page);
        // 游戏先回到账号A，再用错误密码切到账号B
        await SwitchTo(a, $"UID {UidA} 核验通过", "列表文字切回账号A");
        await SwitchTo(b, "账号密码错误", "密码错误时识别错误提示并停止");
        Shot("switch-3-wrong-password.png");

        // 10. 密钥环锁定：读取凭据时密钥环弹出解锁对话框；无人操作时等到超时（2 分钟）后停止切换并说明原因，界面不卡住
        // 密码错误后切换器停下等人工处理：模拟用户关闭密码表单（右上角关闭），再切回账号A
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
        using (var lease = await coordinator.AcquireAsync(cts.Token))
        using (var frame = BetterGenshinImpact.GameTask.Common.GameRuntimeExtensions.CaptureToRectArea(lease.Runtime))
            frame.ClickTo(1260 * frame.Width / 1920d, 259 * frame.Width / 1920d);
        await Settle(1500);
        await SwitchTo(a, $"UID {UidA} 核验通过", "关闭密码表单后列表文字切回账号A");
        var collection = Gdbus("/org/freedesktop/secrets", "org.freedesktop.Secret.Service.ReadAlias", "default").Trim();
        collection = System.Text.RegularExpressions.Regex.Match(collection, @"'(/[^']+)'").Groups[1].Value;
        Gdbus("/org/freedesktop/secrets", "org.freedesktop.Secret.Service.Lock", $"['{collection}']");
        Check("密钥环已锁定", Gdbus(collection, "org.freedesktop.DBus.Properties.Get", "org.freedesktop.Secret.Collection", "Locked").Contains("true"), collection);
        page.SelectedAccount = b;
        page.SelectedDetailTabIndex = 1;
        await Settle();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var locked = page.TestSwitchCommand.ExecuteAsync(null);
        await AnswerDialogAsync(FluentAvalonia.UI.Controls.FAContentDialogResult.Primary, "测试切换 密钥环锁定");
        // 等待期间界面仍响应：每次短暂 Settle 都按时返回
        var maxTick = 0L;
        while (!locked.IsCompleted && watch.Elapsed < TimeSpan.FromMinutes(4))
        {
            var tick = System.Diagnostics.Stopwatch.StartNew();
            await Settle(200);
            maxTick = Math.Max(maxTick, tick.ElapsedMilliseconds);
            if (watch.Elapsed is { TotalSeconds: > 20 and < 21 }) Shot("switch-4-keyring-locked-waiting.png");
        }
        await locked;
        var elapsed = watch.Elapsed;
        Check("密钥环锁定：等待解锁超时后停止切换并说明原因", page.Message.Contains("等待密钥环对话框超时"), page.Message);
        Check("密钥环锁定：按 PromptTimeout（2 分钟）超时", elapsed > TimeSpan.FromSeconds(115) && elapsed < TimeSpan.FromMinutes(3), elapsed);
        Check("密钥环锁定：等待期间界面不卡住", maxTick < 1500, maxTick);
        Shot("switch-4-keyring-locked.png");
    }

    /// <summary>调用会话总线上的 Secret Service（gdbus），返回输出</summary>
    private static string Gdbus(string objectPath, string method, params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("gdbus") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "call", "--session", "--dest", "org.freedesktop.secrets", "--object-path", objectPath, "--method", method }.Concat(args)) psi.ArgumentList.Add(a);
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
        proc.WaitForExit(10000);
        return output;
    }

    private static List<string> UiLogLines() => Services.UiLogSink.Instance.Lines.Select(l => l.Message).ToList();

    /// <summary>引用包装（元组中保留草稿对象）</summary>
    private sealed record PuloniaAccountItemViewModelRef(BetterGenshinImpact.ViewModel.Pages.Pulonia.PuloniaAccountItemViewModel Item);
}
