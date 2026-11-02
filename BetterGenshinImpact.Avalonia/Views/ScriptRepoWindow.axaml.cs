using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Avalonia.ViewModels;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Pulonia.Services;
using FluentAvalonia.UI.Controls;
using Serilog;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>脚本仓库窗口（对应 WPF ScriptRepoWindow），逻辑在 <see cref="ScriptRepoWindowViewModel"/></summary>
public partial class ScriptRepoWindow : Window
{
    private static ScriptRepoWindow? _current;

    public ScriptRepoWindow()
    {
        InitializeComponent();
    }

    public ScriptRepoWindowViewModel? ViewModel => DataContext as ScriptRepoWindowViewModel;

    /// <summary>当前打开的仓库窗口（自测用）</summary>
    public static ScriptRepoWindow? Current => _current;

    /// <summary>打开脚本仓库窗口（已打开时激活）</summary>
    public static ScriptRepoWindow Open(AllConfig config, IPuloniaCredentialVault? vault, Window? owner)
    {
        if (_current is { IsVisible: true })
        {
            _current.Activate();
            return _current;
        }

        var vm = new ScriptRepoWindowViewModel(config.ScriptConfig, vault);
        var window = new ScriptRepoWindow { DataContext = vm };
        vm.PickZip = () => AvaloniaPickers.PickFileAsync("选择脚本仓库压缩包", "压缩包文件", "*.zip");
        vm.PromptRepoUpdate = window.PromptRepoUpdateAsync;
        vm.OpenRepoWebView = () =>
        {
            if (ScriptRepoWebWindow.ShowOrActivate(config) is { } reason)
            {
                UiHost.Dialogs.Toast(DialogKind.Error, "无法打开脚本仓库：" + reason);
                return false;
            }

            window.Close();
            return true;
        };
        window.Closing += (_, _) => vm.SaveCredentials();
        window.Closed += (_, _) =>
        {
            vm.Dispose();
            if (ReferenceEquals(_current, window)) _current = null;
        };
        _current = window;
        if (owner is { IsVisible: true })
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }

        return window;
    }

    /// <summary>
    /// 仓库超过 30 天未更新的提示（与 WPF RepoUpdateDialog 相同：“直接打开”倒计时结束后才能点击，30 天为 5 秒，超过 30 天每多 2 天增加 1 秒）
    /// </summary>
    private async Task<ScriptRepoWindowViewModel.RepoUpdatePromptResult> PromptRepoUpdateAsync(int days)
    {
        var remaining = 5 + (days - 30) / 2;
        var dialog = new FAContentDialog
        {
            Title = "仓库更新提示",
            Content = new TextBlock
            {
                TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
                Text = $"脚本仓库已经 {days} 天未更新\n\n温馨提示：\n脚本内容跟随仓库版本，旧版仓库会订阅到旧版脚本。\n更新仓库后需要重新订阅脚本，以更新脚本内容。\n\n是否立即更新？",
            },
            PrimaryButtonText = "立即更新",
            SecondaryButtonText = $"直接打开 ({remaining}s)",
            IsSecondaryButtonEnabled = false,
            CloseButtonText = "关闭",
            DefaultButton = FAContentDialogButton.Primary,
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            remaining--;
            if (remaining <= 0)
            {
                timer.Stop();
                dialog.SecondaryButtonText = "直接打开";
                dialog.IsSecondaryButtonEnabled = true;
            }
            else
            {
                dialog.SecondaryButtonText = $"直接打开 ({remaining}s)";
            }
        };
        timer.Start();
        PromptDialog = dialog;
        try
        {
            return await dialog.ShowAsync(this) switch
            {
                FAContentDialogResult.Primary => ScriptRepoWindowViewModel.RepoUpdatePromptResult.UpdateNow,
                FAContentDialogResult.Secondary => ScriptRepoWindowViewModel.RepoUpdatePromptResult.OpenDirectly,
                _ => ScriptRepoWindowViewModel.RepoUpdatePromptResult.None,
            };
        }
        finally
        {
            timer.Stop();
            PromptDialog = null;
        }
    }

    /// <summary>正在显示的更新提示（自测用）</summary>
    public FAContentDialog? PromptDialog { get; private set; }

    private void OnRepoDocClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { "https://www.bettergi.com/feats/autos/srepo.html" }, UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "无法打开链接");
            UiHost.Dialogs.Toast(DialogKind.Warning, $"无法打开链接: {ex.Message}");
        }
    }
}
