using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.View.Pages.View;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages;
using BetterGenshinImpact.ViewModel.Pages.View;
using BetterGenshinImpact.ViewModel.Resin;
using BetterGenshinImpact.ViewModel.SoloTaskSettings;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.Service;

/// <summary>
/// 界面冒烟检查：仅当环境变量 BGI_WPF_SMOKE=1 时生效（默认关闭，正常启动不执行任何代码）。
/// 主窗口加载后依次打开主要页面并展开其中的卡片，打开树脂设置、体力计划子任务设置、自动配音设置、配置组设置等窗口（数秒后自动关闭），
/// 记录期间的未处理异常；结果写入 BGI_WPF_SMOKE_OUT 指定的文件后退出，退出码 0 表示全部通过。
/// 不保存任何设置：弹窗一律以关闭（取消）结束，配置组设置使用临时配置。
/// </summary>
internal static class WpfSmokeTest
{
    private static readonly List<string> Errors = [];
    private static readonly StringBuilder Report = new();
    private static string _step = "启动";

    public static bool IsRequested => Environment.GetEnvironmentVariable("BGI_WPF_SMOKE") == "1";

    /// <summary>在 OnStartup 末尾调用；未设置环境变量时直接返回。</summary>
    public static void StartIfRequested(IServiceProvider services)
    {
        if (!IsRequested) return;
        var app = Application.Current;
        app.DispatcherUnhandledException += (_, e) => Errors.Add($"[{_step}] {e.Exception.GetType().Name}: {e.Exception.Message}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Errors.Add($"[{_step}] 线程异常: {e.ExceptionObject}");
        app.Dispatcher.InvokeAsync(() => RunAsync(services), DispatcherPriority.ApplicationIdle);
    }

    private static async Task RunAsync(IServiceProvider services)
    {
        var exitCode = 1;
        try
        {
            for (var i = 0; i < 120 && Application.Current.MainWindow is not { IsLoaded: true }; i++) await Task.Delay(500);
            await Task.Delay(3000);
            var navigation = services.GetRequiredService<INavigationService>();
            foreach (var page in new[]
                     {
                         typeof(HomePage), typeof(TriggerSettingsPage), typeof(TaskSettingsPage), typeof(PuloniaTaskPlanPage),
                         typeof(PuloniaOneDragonPage), typeof(OneDragonFlowPage), typeof(ScriptControlPage), typeof(CommonSettingsPage),
                         typeof(HotKeyPage), typeof(NotificationSettingsPage)
                     })
            {
                await Step($"页面 {page.Name}", async () =>
                {
                    // 已在该页时 Navigate 返回 false，不算失败
                    var navigated = navigation.Navigate(page);
                    await Task.Delay(1500);
                    // 展开页面内的卡片，使折叠区域的模板与绑定也被加载
                    var expanded = ExpandAll(Application.Current.MainWindow!);
                    await Task.Delay(1500);
                    return $"{(navigated ? "" : "（已在该页）")}展开 {expanded} 个卡片";
                });
            }

            var resinDialogs = services.GetRequiredService<IResinSettingsDialogs>();
            await Modal("树脂设置对话框（自动秘境）", () => resinDialogs.EditResinUsage("自动秘境", AutoDomainTask.ResinCapability, new ResinUsage()));
            await Modal("树脂设置对话框（合成浓缩树脂）", () => resinDialogs.EditCraftResin(0));
            var planDialogs = services.GetRequiredService<IResinPlanSettingsDialogs>();
            foreach (var spec in new[] { ResinPlanSubTaskSpec.Domain, ResinPlanSubTaskSpec.Boss, ResinPlanSubTaskSpec.Stygian, ResinPlanSubTaskSpec.LeyLine })
                await Modal($"体力计划子任务设置（{spec.DisplayName}）", () => planDialogs.EditSubTask(spec, new JObject()));

            var questVoice = services.GetRequiredService<QuestVoiceSettingsViewModel>();
            await questVoice.Management.EnsureLoadedAsync();
            await Modal("自动配音设置窗口", () => new QuestVoiceSettingsWindow(questVoice) { Owner = Application.Current.MainWindow }.ShowDialog());

            var config = services.GetRequiredService<AllConfig>();
            await Modal("调度器配置组设置", () => new FluentWindow
            {
                Title = "配置组设置",
                Content = new ScriptGroupConfigView(new ScriptGroupConfigViewModel(config, new ScriptGroupConfig())),
                Width = 800,
                Height = 600,
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            }.ShowDialog());
            // 任务计划“编辑任务”窗口：临时计划中逐类插入任务后打开（冒烟脚本运行前后备份恢复 User/Pulonia）
            var planPage = services.GetRequiredService<PuloniaTaskPlanViewModel>();
            var session = planPage.Session;
            var document = session.CreatePlanDocument("WPF 冒烟临时计划");
            var registry = services.GetService<BetterGenshinImpact.GameTask.SoloTask.SoloTaskRegistry>();
            var taskDialogs = services.GetService<BetterGenshinImpact.ViewModel.Pages.Pulonia.IPuloniaTaskSettingsDialogs>();
            var definitions = session.TaskService.Definitions;
            var picked = definitions.Where(d => d.TaskType is "pathing" or "keymouse" or "javascript" or "shell")
                .Concat(definitions.Where(d => d.TaskType is "solo.auto_domain" or "solo.resin_plan" or "solo.one_dragon"))
                .Concat(definitions.Where(d => d.TaskType.StartsWith("solo.", StringComparison.Ordinal)
                    && BetterGenshinImpact.Pulonia.Executors.PuloniaSoloTaskExecutor.HasEditableParameters(d)).Take(4))
                .DistinctBy(d => d.TaskType).ToList();
            foreach (var definition in picked)
            {
                var node = document.InsertNode(new BetterGenshinImpact.Pulonia.Models.PuloniaTask { Name = definition.DisplayName, TaskType = definition.TaskType },
                    document.RootNode, document.RootNode.Children.Count);
                await Modal($"编辑任务窗口（{definition.DisplayName}）", () => new BetterGenshinImpact.View.Windows.Pulonia.PuloniaTaskNodeSettingsDialog(
                    new BetterGenshinImpact.ViewModel.Windows.Pulonia.PuloniaTaskNodeSettingsDialogViewModel(session, node, definition, registry, taskDialogs))
                {
                    Owner = Application.Current.MainWindow
                }.ShowDialog());
            }

            _step = "结束";
            await Task.Delay(1000);
            exitCode = Errors.Count == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Errors.Add($"[{_step}] 冒烟检查中断: {ex}");
        }

        Report.AppendLine(exitCode == 0 ? "RESULT PASS" : $"RESULT FAIL {Errors.Count}");
        foreach (var error in Errors) Report.AppendLine("ERROR " + error);
        try
        {
            var output = Environment.GetEnvironmentVariable("BGI_WPF_SMOKE_OUT");
            if (!string.IsNullOrWhiteSpace(output)) File.WriteAllText(output, Report.ToString(), new UTF8Encoding(false));
        }
        catch
        {
            // 结果文件写入失败时仍以退出码报告
        }

        Application.Current.Shutdown(exitCode);
    }

    /// <summary>执行一步并记录该步期间新增的异常。</summary>
    private static async Task Step(string name, Func<Task<string>> action)
    {
        _step = name;
        var before = Errors.Count;
        string detail;
        try
        {
            detail = await action();
        }
        catch (Exception ex)
        {
            Errors.Add($"[{name}] {ex.GetType().Name}: {ex.Message}");
            detail = "";
        }

        Report.AppendLine($"{(Errors.Count == before ? "PASS" : "FAIL")} {name} {detail}".TrimEnd());
    }

    /// <summary>打开模态窗口；数秒后关闭本步打开的所有窗口（相当于取消）。</summary>
    private static Task Modal(string name, Action open) => Step(name, async () =>
    {
        var existing = Application.Current.Windows.OfType<Window>().ToHashSet();
        var opened = new List<string>();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            foreach (var window in Application.Current.Windows.OfType<Window>().Where(w => !existing.Contains(w)).Reverse().ToList())
            {
                ExpandAll(window);
                window.UpdateLayout();
                opened.Add(window.Title);
                window.Close();
            }
        };
        timer.Start();
        try
        {
            open();
        }
        finally
        {
            timer.Stop();
        }

        await Task.Delay(500);
        if (opened.Count == 0) throw new InvalidOperationException("窗口未打开");
        return "窗口：" + string.Join(" / ", opened.Distinct());
    });

    private static int ExpandAll(DependencyObject root)
    {
        var count = 0;
        foreach (var expander in Descendants(root).OfType<Expander>().ToList())
        {
            if (expander.IsExpanded) continue;
            expander.IsExpanded = true;
            count++;
        }

        if (root is UIElement element) element.UpdateLayout();
        return count;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;
            var children = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(current) : 0;
            for (var i = 0; i < children; i++) stack.Push(VisualTreeHelper.GetChild(current, i));
        }
    }
}
