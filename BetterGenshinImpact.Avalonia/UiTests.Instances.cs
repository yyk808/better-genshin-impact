using Avalonia.Controls;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service.Instance;
using Microsoft.Extensions.DependencyInjection;

namespace BetterGenshinImpact.Avalonia;

/// <summary>
/// 多实例（--ui-test instances-root / instances-child）：根实例与另一 Unix 会话里的子实例经实例 IPC 连接，
/// 同一会话里重复启动转发给根实例，根实例把任务计划整次下发到子实例执行并收到状态回报
/// </summary>
internal sealed partial class UiTests
{
    private const string InstancesPlanName = "M64 多实例下发";

    private static string InstancesOutput => Path.Combine(Path.GetTempPath(), "bgi-m64-instances.txt");

    private async Task InstancesRootAsync()
    {
        var host = vm.Host!;
        var instances = host.Instances;
        Check("多实例已启用，本进程是根实例", instances is { Context.IsRoot: true }, instances?.Context.InstanceType);
        if (instances is null) return;
        var keys = host.Services.GetRequiredService<IPuloniaInstanceKeyProvider>();
        Check("根实例的实例键为主实例", keys.CurrentInstanceKey == PuloniaInstanceKey.Primary, keys.CurrentInstanceKey);
        var presence = host.Services.GetRequiredService<PuloniaTaskStore>();
        Check("在线标记已持有（online.lock 独占 flock）", presence.IsInstanceOnline(PuloniaInstanceKey.Primary));

        // 同一 Unix 会话里的重复启动：转发参数后退出，根实例激活主窗口
        var forwarded = await Until(() => UiLogLines().Any(l => l.Contains("收到另一次启动转发")), 60000);
        Check("同一会话重复启动转发给根实例并激活主窗口", forwarded, UiLogLines().LastOrDefault(l => l.Contains("启动转发")));

        // 子实例（另一 Unix 会话）连接
        vm.Navigate("任务计划");
        var plans = vm.TaskPlan;
        await plans.ReloadAsync();
        var connected = await Until(() => { plans.RefreshRunTargets(); return plans.ShowRunTargets; }, 90000);
        var child = plans.RunTargets.Skip(1).FirstOrDefault();
        Check("子实例连接后显示执行实例选择", connected && child is not null && Named<ComboBox>("RunTargetBox") is { IsVisible: true },
            string.Join(" / ", plans.RunTargets.Select(t => t.Label)));
        Check("在线子实例的在线标记可见", await Until(() => presence.IsInstanceOnline(PuloniaInstanceKey.ChildSession), 10000));
        if (child?.Endpoint is null) return;

        File.Delete(InstancesOutput);
        foreach (var old in plans.Plans.Where(p => p.Name == InstancesPlanName).ToList()) { plans.SelectedPlan = old; await plans.DeletePlanCommand.ExecuteAsync(null); }
        plans.NewPlanName = InstancesPlanName;
        await plans.NewPlanCommand.ExecuteAsync(null);
        await Settle();
        var id = plans.SelectedPlan?.Plan.Id;
        try
        {
            plans.SelectedAddOption = plans.AddOptions.First(o => o.Definition.TaskType == "shell");
            await Settle(600);
            await Click(ButtonByText("＋ 添加"), "添加 Shell");
            plans.SelectedNode = plans.Nodes.Last(n => n.Task.TaskType == "shell");
            await Settle();
            if (FieldBox("arguments") is { } a) a.Text = $"-c\necho \"pid=$PPID\" > '{InstancesOutput}'";
            await Settle();
            await Click(Named<Button>("ApplyNodeButton"), "应用");
            plans.SelectedRunTarget = child;
            await Settle(400);
            Shot("instances-run-target.png");
            var runId = await plans.RunPlanAsync(plans.SelectedPlan);
            Check("下发到子实例并得到运行 ID", runId is not null && plans.StatusMessage.Contains("已把"), plans.StatusMessage);
            var done = await Until(() => plans.LatestDispatchedRun is { IsActive: false, Status: not PuloniaTaskRunStatus.Queued }, 60000);
            var remote = plans.LatestDispatchedRun;
            Check("子实例执行完成并回报状态", done && remote?.Status == PuloniaTaskRunStatus.Succeeded, $"{remote?.Status} {remote?.Message}");
            var text = File.Exists(InstancesOutput) ? File.ReadAllText(InstancesOutput).Trim() : "";
            Check("命令由子实例进程执行", text == $"pid={child.Endpoint.ProcessId}", $"{text}（子实例进程 {child.Endpoint.ProcessId}）");
            await Settle(600);
            Check("执行状态显示子实例", plans.ExecutionStatusText.StartsWith("[桌面分身]"), plans.ExecutionStatusText);
            Shot("instances-done.png");
        }
        finally
        {
            plans.SelectedPlan = plans.Plans.FirstOrDefault(p => p.Plan.Id == id);
            if (plans.SelectedPlan is not null) await plans.DeletePlanCommand.ExecuteAsync(null);
            File.Delete(InstancesOutput);
        }
    }

    private async Task InstancesChildAsync()
    {
        var host = vm.Host!;
        var instances = host.Instances;
        Check("本进程是子实例（另一 Unix 会话）", instances is { Context.InstanceType: BetterGiInstanceType.ChildSession }, instances?.Context.InstanceType);
        var keys = host.Services.GetRequiredService<IPuloniaInstanceKeyProvider>();
        Check("子实例的实例键", keys.CurrentInstanceKey == PuloniaInstanceKey.ChildSession, keys.CurrentInstanceKey);
        var tasks = host.Services.GetRequiredService<IPuloniaTaskService>();
        var ran = await Until(() => tasks.ListRunsAsync().GetAwaiter().GetResult()
            .Any(r => r.Status == PuloniaTaskRunStatus.Succeeded && r.Source != "ui"), 120000);
        Check("子实例执行了根实例下发的运行", ran, string.Join(" / ", tasks.ListRunsAsync().GetAwaiter().GetResult().Select(r => $"{r.Source}:{r.Status}")));
        await Settle(3000);
    }
}
