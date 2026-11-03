using System;
using System.Collections.Specialized;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Threading;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Host.Common.Storage;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>存储空间窗口（对照 WPF StorageManagementWindow）：打开时统计，关闭时取消进行中的统计</summary>
public partial class StorageManagementWindow : Window
{
    private readonly StorageManagementViewModel? _vm;

    public StorageManagementWindow()
    {
        InitializeComponent();
    }

    public StorageManagementWindow(StorageManagementViewModel vm) : this()
    {
        DataContext = _vm = vm;
        vm.CloseRequested += (_, _) => Close();
        vm.OverviewSegments.CollectionChanged += OnOverviewChanged;
        // 模态窗口打开时主窗口不可操作：确认框与提示显示在本窗口上
        vm.ConfirmAsync = async (message, title, kind) =>
            await Services.FluentMessageBox.ShowAsync(this, message, title, kind, DialogButtons.OkCancel) == DialogResult.Ok;
        vm.Notify = ShowNotice;
        _noticeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _noticeTimer.Tick += (_, _) => { _noticeTimer.Stop(); NoticeBar.IsVisible = false; };
    }

    private readonly DispatcherTimer? _noticeTimer;

    private void ShowNotice(DialogKind kind, string message)
    {
        NoticeText.Text = message;
        if (kind is DialogKind.Error or DialogKind.Warning) NoticeText.Foreground = kind == DialogKind.Error ? Brushes.IndianRed : Brushes.Orange;
        else NoticeText.ClearValue(TextBlock.ForegroundProperty);
        NoticeBar.IsVisible = true;
        _noticeTimer?.Stop();
        _noticeTimer?.Start();
    }

    /// <summary>当前打开的窗口（界面自测用）</summary>
    public static StorageManagementWindow? Current { get; private set; }

    /// <summary>打开时的统计任务（界面自测等待用）</summary>
    public System.Threading.Tasks.Task? InitialRefresh { get; private set; }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Current = this;
        InitialRefresh = _vm?.RefreshCommand.ExecuteAsync(null);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (Current == this) Current = null;
        _noticeTimer?.Stop();
        if (_vm is not null)
        {
            _vm.OverviewSegments.CollectionChanged -= OnOverviewChanged;
            _vm.Dispose();
        }

        base.OnClosed(e);
    }

    /// <summary>比例条：每个分组一列，宽度按占用字节数（同 WPF 的 ProportionalPanel）</summary>
    private void OnOverviewChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_vm is null) return;
        OverviewBar.Children.Clear();
        OverviewBar.ColumnDefinitions.Clear();
        foreach (var segment in _vm.OverviewSegments)
        {
            OverviewBar.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(segment.Weight, 0), GridUnitType.Star));
            var part = new Border { Background = StorageColorConverter.ToBrush(segment.Color) };
            ToolTip.SetTip(part, segment.Label);
            Grid.SetColumn(part, OverviewBar.ColumnDefinitions.Count - 1);
            OverviewBar.Children.Add(part);
        }
    }
}

/// <summary>分组颜色（#RRGGBB）转画刷</summary>
public sealed class StorageColorConverter : IValueConverter
{
    public static StorageColorConverter Instance { get; } = new();

    public static IBrush ToBrush(string? color) =>
        color is not null && Color.TryParse(color, out var c) ? new SolidColorBrush(c) : Brushes.Gray;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => ToBrush(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
