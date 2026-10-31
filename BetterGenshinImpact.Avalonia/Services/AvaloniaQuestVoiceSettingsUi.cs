using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.ViewModel.Pages;

namespace BetterGenshinImpact.Avalonia.Services;

/// <summary>
/// 自动配音设置模型在 Avalonia 下的界面能力（对应 WPF 的 WpfQuestVoiceSettingsUi）：
/// Background 优先级投递、DispatcherTimer、主题消息框确认、StorageProvider 文件选择。
/// 对话框的所有者为当前打开的设置窗口，没有时为主窗口
/// </summary>
public sealed class AvaloniaQuestVoiceSettingsUi : IQuestVoiceSettingsUi
{
    /// <summary>当前打开的设置窗口（由窗口打开 / 关闭时设置）</summary>
    public Window? Owner { get; set; }

    /// <summary>UI 自测用：替代确认框与文件选择（返回 null 表示不替代）</summary>
    public static Func<string, bool>? TestConfirm { get; set; }
    public static Func<string, string?>? TestPickFile { get; set; }

    private Window? EffectiveOwner => Owner ?? AvaloniaPickers.MainWindow;

    public bool TryPost(Action action)
    {
        Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
        return true;
    }

    public IQuestVoiceUiTimer CreateTimer(TimeSpan interval, Action tick) => new Timer(interval, tick);

    public async Task<bool> ConfirmAsync(string message, string title)
    {
        if (TestConfirm is { } test) return test(message);
        if (EffectiveOwner is not { } owner) return true;
        return await FluentMessageBox.ShowAsync(owner, message, title, DialogKind.Question, DialogButtons.YesNo) == DialogResult.Yes;
    }

    public async Task<string?> PickOpenFileAsync(string title, string filter, string? initialDirectory)
    {
        if (TestPickFile is { } test) return test(title);
        if (EffectiveOwner?.StorageProvider is not { CanOpen: true } storage) return null;
        var options = new FilePickerOpenOptions { Title = title, AllowMultiple = false, FileTypeFilter = ParseFilter(filter) };
        if (!string.IsNullOrEmpty(initialDirectory))
            options.SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(initialDirectory);
        var files = await storage.OpenFilePickerAsync(options);
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(string title, string filter, string fileName)
    {
        if (TestPickFile is { } test) return test(title);
        if (EffectiveOwner?.StorageProvider is not { CanSave: true } storage) return null;
        var types = ParseFilter(filter);
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = fileName,
            FileTypeChoices = types,
            DefaultExtension = Path.GetExtension(fileName).TrimStart('.'),
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    /// <summary>把 WPF 风格的“说明|*.a;*.b|说明|*.*”转换为 Avalonia 的文件类型列表</summary>
    public static IReadOnlyList<FilePickerFileType> ParseFilter(string filter)
    {
        var parts = filter.Split('|');
        var result = new List<FilePickerFileType>();
        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            var patterns = parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            result.Add(new FilePickerFileType(parts[i]) { Patterns = patterns });
        }
        return result;
    }

    private sealed class Timer : IQuestVoiceUiTimer
    {
        private readonly DispatcherTimer _timer;

        public Timer(TimeSpan interval, Action tick)
        {
            _timer = new DispatcherTimer(interval, DispatcherPriority.Normal, (_, _) => tick());
            _timer.Stop();
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();
    }
}
