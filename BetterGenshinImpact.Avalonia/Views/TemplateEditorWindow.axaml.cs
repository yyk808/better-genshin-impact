using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Avalonia.ViewModels;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Host.Common;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.GameTask.Common;
using Mat = OpenCvSharp.Mat;
using Serilog;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>模板素材制作窗口：快捷键截取当前游戏画面后打开（同 WPF RecognitionTemplateEditorService.OpenAsync）</summary>
public partial class TemplateEditorWindow : Window
{
    private Point? _dragStart;

    /// <summary>当前打开的窗口（再次按快捷键时激活，不重复打开）</summary>
    public static TemplateEditorWindow? Current { get; private set; }

    private static int _isOpening;

    public TemplateEditorWindow() => InitializeComponent();

    private TemplateEditorWindow(TemplateEditorViewModel vm) : this()
    {
        DataContext = vm;
        vm.ConfirmOverwrite = async text =>
        {
            var box = new MessageBoxWindow(text, "确认覆盖模板素材", DialogKind.Warning, DialogButtons.YesNo);
            await box.ShowDialog(this);
            return box.Result == DialogResult.Yes;
        };
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(vm.SelectionX) or nameof(vm.SelectionY) or nameof(vm.SelectionWidth) or nameof(vm.SelectionHeight)) UpdateRect();
        };
        var canvas = this.FindControl<Canvas>("SelectionCanvas")!;
        canvas.PointerPressed += (_, e) =>
        {
            _dragStart = e.GetPosition(canvas);
            e.Pointer.Capture(canvas);
        };
        canvas.PointerMoved += (_, e) =>
        {
            if (_dragStart is { } start) Select(start, e.GetPosition(canvas));
        };
        canvas.PointerReleased += (_, e) =>
        {
            if (_dragStart is { } start) Select(start, e.GetPosition(canvas));
            _dragStart = null;
            e.Pointer.Capture(null);
        };
        UpdateRect();
        Closed += (_, _) =>
        {
            if (ReferenceEquals(Current, this)) Current = null;
            vm.Dispose();
        };
    }

    private TemplateEditorViewModel? Vm => DataContext as TemplateEditorViewModel;

    private void Select(Point a, Point b) => Vm?.SetSelection(a.X, a.Y, b.X, b.Y);

    private void UpdateRect()
    {
        if (Vm is not { } vm || this.FindControl<Border>("SelectionRect") is not { } rect) return;
        Canvas.SetLeft(rect, vm.SelectionX);
        Canvas.SetTop(rect, vm.SelectionY);
        rect.Width = vm.SelectionWidth;
        rect.Height = vm.SelectionHeight;
        rect.IsVisible = vm.SelectionWidth > 0 && vm.SelectionHeight > 0;
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && await vm.SaveAsync()) Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// 截取当前运行环境画面并打开窗口；已打开时激活；截图器未启动时提示（与 WPF 相同的提示文字）
    /// </summary>
    public static async Task OpenAsync(GameHost? host, DevConfig devConfig)
    {
        if (Current is { } existing)
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }

        if (Interlocked.Exchange(ref _isOpening, 1) != 0) return;
        try
        {
            Mat image;
            try
            {
                image = await Task.Run(() =>
                {
                    var runtime = host?.RuntimeService.Current ?? throw new InvalidOperationException("截图器未启动");
                    using var region = runtime.CaptureToRectArea();
                    if (region.SrcMat.Empty()) throw new InvalidOperationException("未获取到有效的游戏截图。");
                    return region.SrcMat.Clone();
                });
            }
            catch (Exception ex)
            {
                Log.Warning("打开模板素材制作窗口失败：{Message}", ex.Message);
                var owner = AvaloniaPickers.MainWindow;
                var box = new MessageBoxWindow($"无法截取当前游戏画面并打开模板制作工具。{Environment.NewLine}{Environment.NewLine}{ex.Message}"
                    + $"{Environment.NewLine}{Environment.NewLine}请先在启动页启动截图器，并确认游戏窗口未最小化。", "模板素材制作", DialogKind.Warning, DialogButtons.Ok);
                if (owner is not null) await box.ShowDialog(owner);
                return;
            }

            var window = new TemplateEditorWindow(new TemplateEditorViewModel(image, new RecognitionTemplateAssetService(), devConfig));
            Current = window;
            if (AvaloniaPickers.MainWindow is { IsVisible: true } main) window.Show(main);
            else window.Show();
            window.Activate();
        }
        finally
        {
            Interlocked.Exchange(ref _isOpening, 0);
        }
    }
}
