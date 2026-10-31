using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Win32;

namespace BetterGenshinImpact.Service.Ui;

/// <summary>
/// 自动配音设置模型在 WPF 下的界面能力：Background 优先级投递、DispatcherTimer、ThemedMessageBox 确认和系统文件对话框，
/// 与模型迁入 Core 之前的写法保持一致。
/// </summary>
public sealed class WpfQuestVoiceSettingsUi : IQuestVoiceSettingsUi
{
    /// <summary>在构造线程（UI 线程）捕获调度器，与原先设置模型的做法相同。</summary>
    private readonly Dispatcher _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    public bool TryPost(Action action)
    {
        if (_dispatcher.HasShutdownStarted) return false;
        _dispatcher.BeginInvoke(DispatcherPriority.Background, action);
        return true;
    }

    public IQuestVoiceUiTimer CreateTimer(TimeSpan interval, Action tick) => new Timer(interval, tick);

    /// <summary>没有主窗口（组件测试）时不弹框，直接视为确认。</summary>
    public Task<bool> ConfirmAsync(string message, string title) => Task.FromResult(Application.Current?.MainWindow is null
        || ThemedMessageBox.Question(message, title, MessageBoxButton.YesNo, MessageBoxResult.No) == MessageBoxResult.Yes);

    public Task<string?> PickOpenFileAsync(string title, string filter, string? initialDirectory)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, Multiselect = false };
        if (!string.IsNullOrEmpty(initialDirectory)) dialog.InitialDirectory = initialDirectory;
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }

    public Task<string?> PickSaveFileAsync(string title, string filter, string fileName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = fileName, AddExtension = true };
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }

    private sealed class Timer : IQuestVoiceUiTimer
    {
        private readonly DispatcherTimer _timer;

        public Timer(TimeSpan interval, Action tick)
        {
            _timer = new DispatcherTimer { Interval = interval };
            _timer.Tick += (_, _) => tick();
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();
    }
}
