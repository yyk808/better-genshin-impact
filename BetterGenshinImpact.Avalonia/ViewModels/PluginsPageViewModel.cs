using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using Avalonia.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Plugins;
using BetterGenshinImpact.Core.Plugins.Pluggables;
using BetterGenshinImpact.Host.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// “插件”页（对应 WPF PluginsPage）：功能选择（Core 的 PluggableRegistry，Linux 宿主只登记 Core 内置实现，改选立即生效并写入 PluginConfig）；
/// 已安装插件为只读列表（读取插件目录下各文件夹的 plugin.json）。Linux 宿主暂不加载外部插件（插件是 Windows 程序集，PluginManager 在 WPF 主程序中），
/// 因此不提供安装、启用 / 停用与插件设置
/// </summary>
public sealed partial class PluginsPageViewModel : PageViewModel
{
    private readonly ILogger _log;

    [ObservableProperty] private bool _hasNoPlugins = true;

    public PluginsPageViewModel(GameHost? host, ILogger log)
    {
        _log = log;
        Registry = host?.Services.GetService<PluggableRegistry>();
        if (Registry is not null)
        {
            foreach (var descriptor in Registry.All)
            {
                Pluggables.Add(new PluggableItem(Registry, descriptor));
            }
        }

        Refresh();
    }

    public override string Title => "插件";

    public PluggableRegistry? Registry { get; }

    public string PluginsDirectory => Global.Absolute("Plugins");

    public ObservableCollection<PluggableItem> Pluggables { get; } = [];

    public ObservableCollection<InstalledPluginInfo> Plugins { get; } = [];

    public override void OnNavigatedTo() => Refresh();

    /// <summary>重新读取插件目录（与 WPF 相同跳过 . 开头的暂存目录）</summary>
    [RelayCommand]
    public void Refresh()
    {
        Plugins.Clear();
        if (Directory.Exists(PluginsDirectory))
        {
            foreach (var dir in Directory.GetDirectories(PluginsDirectory).Where(d => !Path.GetFileName(d).StartsWith('.')).Order(StringComparer.OrdinalIgnoreCase))
            {
                var manifestPath = Path.Combine(dir, "plugin.json");
                if (!File.Exists(manifestPath)) continue;
                try
                {
                    using var stream = File.OpenRead(manifestPath);
                    var manifest = JsonSerializer.Deserialize<PluginManifest>(stream, Global.ManifestJsonOptions);
                    Plugins.Add(manifest is null
                        ? new InstalledPluginInfo(Path.GetFileName(dir), "", "", dir, "plugin.json 为空")
                        : new InstalledPluginInfo(string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Id : manifest.Name, manifest.Version, manifest.Id, dir,
                            "Linux 版暂不加载外部插件", manifest.Description));
                }
                catch (Exception e)
                {
                    Plugins.Add(new InstalledPluginInfo(Path.GetFileName(dir), "", "", dir, $"plugin.json 无法读取：{e.Message}"));
                }
            }
        }

        HasNoPlugins = Plugins.Count == 0;
    }

    [RelayCommand]
    private void OpenPluginsDirectory()
    {
        try
        {
            Directory.CreateDirectory(PluginsDirectory);
            Process.Start(new ProcessStartInfo("xdg-open", $"\"{PluginsDirectory}\"") { UseShellExecute = false });
        }
        catch (Exception e)
        {
            _log.Warning("无法打开插件目录：{Message}", e.Message);
        }
    }
}

/// <summary>“功能选择”的一行：显示内容由 Core 的 PluggableSelectionPresentation 计算（与 WPF 相同）</summary>
public sealed partial class PluggableItem : ObservableObject
{
    private readonly PluggableRegistry _registry;
    private bool _syncing;

    [ObservableProperty] private PluggableImplOption? _selectedOption;
    [ObservableProperty] private string? _fallbackText;
    [ObservableProperty] private bool _hasFallback;

    public PluggableItem(PluggableRegistry registry, PluggableDescriptor descriptor)
    {
        _registry = registry;
        Descriptor = descriptor;
        _registry.Changed += (_, e) =>
        {
            if (e.PluggableId == Descriptor.Id) Dispatcher.UIThread.Post(Refresh);
        };
        Refresh();
    }

    public PluggableDescriptor Descriptor { get; }

    public string DisplayName => Descriptor.DisplayName;

    public string Description => Descriptor.Description ?? string.Empty;

    public ObservableCollection<PluggableImplOption> Options { get; } = [];

    public void Refresh()
    {
        var view = PluggableSelectionPresentation.Build(_registry, Descriptor.Id);
        _syncing = true;
        try
        {
            Options.Clear();
            foreach (var option in view.Options) Options.Add(option);
            SelectedOption = view.Selected;
            HasFallback = view.HasFallback;
            FallbackText = view.FallbackText;
        }
        finally
        {
            _syncing = false;
        }
    }

    partial void OnSelectedOptionChanged(PluggableImplOption? value)
    {
        if (_syncing || value is null || value.IsUnavailable) return;
        _registry.Select(Descriptor.Id, value.Id);
    }
}

/// <summary>已安装插件（只读）</summary>
public sealed record InstalledPluginInfo(string Name, string Version, string Id, string Directory, string StatusText, string Description = "")
{
    public string VersionText => string.IsNullOrWhiteSpace(Version) ? "" : $"v{Version}";
}
