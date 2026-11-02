using Avalonia.Controls;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Avalonia;

/// <summary>Shell 任务在 Linux 上运行（--ui-test pulonia-shell）：默认 /bin/sh；Windows 上创建的 cmd.exe /c 命令改用 /bin/sh -c</summary>
internal sealed partial class UiTests
{
    private async Task PuloniaShellAsync()
    {
        const string planName = "M64 Shell 计划";
        var dir = Path.Combine(Path.GetTempPath(), "bgi-m64-shell");
        Directory.CreateDirectory(dir);
        var outSh = Path.Combine(dir, "sh.txt");
        var outCmd = Path.Combine(dir, "cmd.txt");
        File.Delete(outSh);
        File.Delete(outCmd);
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
            async Task AddShell(string name, string? file, string args)
            {
                plans.SelectedAddOption = plans.AddOptions.First(o => o.Definition.TaskType == "shell");
                await Settle(600);
                await Click(ButtonByText("＋ 添加"), "添加 Shell");
                plans.SelectedNode = plans.Nodes.Last(n => n.Task.TaskType == "shell");
                await Settle();
                if (file is null) Check("Linux 上 Shell 默认 /bin/sh", FieldBox("file_name")?.Text == "/bin/sh", FieldBox("file_name")?.Text);
                Named<TextBox>("NodeNameBox")!.Text = name;
                if (file is not null && FieldBox("file_name") is { } f) f.Text = file;
                if (FieldBox("arguments") is { } a) a.Text = args;
                await Settle();
                await Click(Named<Button>("ApplyNodeButton"), "应用");
            }
            await AddShell("M64 sh", null, $"-c\necho m64-sh > '{outSh}'");
            await AddShell("M64 cmd", "cmd.exe", $"/d\n/c\necho m64-cmd > '{outCmd}'");
            Shot("shell-plan.png");
            var logStart = UiLogLines().Count;
            await plans.RunPlanAsync(plans.SelectedPlan);
            await Until(() => File.Exists(outCmd) || UiLogLines().Skip(logStart).Any(l => l.Contains("失败")), 30000);
            await Until(() => !plans.IsSelectedPlanRunning, 30000);
            Check("/bin/sh -c 命令运行", File.Exists(outSh) && File.ReadAllText(outSh).Trim() == "m64-sh", File.Exists(outSh) ? File.ReadAllText(outSh) : "无输出文件");
            Check("cmd.exe /d /c 命令在 Linux 上改用 /bin/sh -c 运行", File.Exists(outCmd) && File.ReadAllText(outCmd).Trim() == "m64-cmd", File.Exists(outCmd) ? File.ReadAllText(outCmd) : "无输出文件");
            Check("日志说明改用 /bin/sh", UiLogLines().Skip(logStart).Any(l => l.Contains("改用 /bin/sh -c 运行")));
        }
        finally
        {
            plans.SelectedPlan = plans.Plans.FirstOrDefault(p => p.Plan.Id == id);
            if (plans.SelectedPlan is not null) await plans.DeletePlanCommand.ExecuteAsync(null);
            Directory.Delete(dir, true);
        }
    }
}
