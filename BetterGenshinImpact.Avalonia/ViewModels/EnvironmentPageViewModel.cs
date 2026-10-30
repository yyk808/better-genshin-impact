using System.Collections.ObjectModel;
using Avalonia.Media;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Host.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using Serilog;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>环境探测结果的一项</summary>
public sealed record EnvironmentItem(string Group, string Name, string Value);

/// <summary>“环境”页：平台探测（显示服务器、扩展等）、推理、OpenCV、字体、素材目录</summary>
public sealed partial class EnvironmentPageViewModel : PageViewModel
{
    private readonly GameHost? _host;
    private readonly string? _hostError;
    private readonly ILogger _log;

    [ObservableProperty] private string _openCvCheckText = "未运行";
    [ObservableProperty] private bool _isChecking;

    public EnvironmentPageViewModel(GameHost? host, string? hostError, ILogger log)
    {
        _host = host;
        _hostError = hostError;
        _log = log;
        Refresh();
    }

    public override string Title => "环境";

    public ObservableCollection<EnvironmentItem> Items { get; } = [];

    public override void OnNavigatedTo() => Refresh();

    [RelayCommand]
    public void Refresh()
    {
        Items.Clear();
        if (_host is null)
        {
            Items.Add(new("平台", "状态", "初始化失败：" + _hostError));
        }
        else
        {
            Items.Add(new("平台", "平台服务", _host.Platform.Name));
            Items.Add(new("平台", "类型", _host.Probe.Kind.ToString()));
            foreach (var (name, value) in _host.Probe.Items)
            {
                Items.Add(new("平台", name, value));
            }

            Items.Add(new("游戏", "进程名", string.Join(", ", _host.Options.ProcessNames)));
            Items.Add(new("游戏", "截图器", _host.IsRunning ? "运行中" : "未启动"));
            try
            {
                var inference = _host.Inference;
                Items.Add(new("推理", "引擎 / 设备", $"{inference.Current.Engine} / {inference.Current.Device.Name}"));
                Items.Add(new("推理", "运行库目录", inference.Current.RuntimeDirectory ?? "—"));
            }
            catch (Exception e)
            {
                Items.Add(new("推理", "状态", "未初始化：" + e.Message));
            }
        }

        Items.Add(new("OpenCV", "版本", SafeOpenCvVersion()));
        Items.Add(new("OpenCV", "原生库", FindNative("libOpenCvSharpExtern.so")));
        Items.Add(new("OpenCV", "模块自检", OpenCvCheckText));
        Items.Add(new("界面", "中文字体", FontInfo.CjkFamily ?? "未找到（请安装 Noto Sans CJK SC 或文泉驿字体）"));
        Items.Add(new("界面", "默认字体", FontManager.Current.DefaultFontFamily.Name));
        Items.Add(new("界面", "运行时", $".NET {Environment.Version}，{System.Runtime.InteropServices.RuntimeInformation.OSDescription}"));
        Items.Add(new("素材", "目录", Global.StartUpPath));
        Items.Add(new("素材", "Assets 存在", Directory.Exists(Global.Absolute("Assets")) ? "是" : "否（请用 --assets 指向主程序输出目录）"));
    }

    [RelayCommand]
    private async Task RunOpenCvCheckAsync()
    {
        IsChecking = true;
        OpenCvCheckText = "检查中…";
        Refresh();
        try
        {
            var ok = await Task.Run(() => OpenCvSelfCheck.Run(_log));
            OpenCvCheckText = ok ? "通过（SIFT / SURF / dnn / 连通域 / 相位相关，详见日志）" : "失败（详见日志）";
        }
        catch (Exception e)
        {
            OpenCvCheckText = "异常：" + e.Message;
        }
        finally
        {
            IsChecking = false;
            Refresh();
        }
    }

    private static string SafeOpenCvVersion()
    {
        try
        {
            return Cv2.GetVersionString() ?? "未知";
        }
        catch (Exception e)
        {
            return "无法加载：" + e.Message;
        }
    }

    private static string FindNative(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "runtimes", "linux-x64", "native", file);
        return File.Exists(path) ? $"{path}（{new FileInfo(path).Length / 1024 / 1024} MB）" : "未找到 " + file;
    }
}
