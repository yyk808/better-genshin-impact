using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;

namespace BetterGenshinImpact.Avalonia.Services;

/// <summary>
/// JSON 文件编辑对话框（对应 WPF JsonMonoDialog）：等宽多行文本框，保存前校验 JSON 格式，格式错误时提示且不写入
/// </summary>
public static class JsonEditDialog
{
    /// <summary>当前打开的编辑框（自检用）</summary>
    public static TextBox? CurrentEditor { get; private set; }

    /// <summary>编辑并保存；返回是否已保存</summary>
    public static async Task<bool> ShowAsync(string path, string title)
    {
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null) return false;
        var editor = new TextBox
        {
            Name = "JsonEditorBox",
            Text = File.Exists(path) ? await File.ReadAllTextAsync(path) : "",
            AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("monospace"), Width = 640, Height = 420,
        };
        var error = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#E5484D")), TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(new TextBlock { Text = path, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(editor);
        content.Children.Add(error);
        var dialog = new FluentAvalonia.UI.Controls.FAContentDialog
        {
            Title = title, Content = content, PrimaryButtonText = "保存", CloseButtonText = "取消",
            DefaultButton = FluentAvalonia.UI.Controls.FAContentDialogButton.Primary,
        };
        var saved = false;
        dialog.PrimaryButtonClick += (_, e) =>
        {
            try
            {
                Newtonsoft.Json.Linq.JToken.Parse(editor.Text ?? "");
                File.WriteAllText(path, editor.Text ?? "");
                saved = true;
            }
            catch (Exception ex) when (ex is Newtonsoft.Json.JsonException or IOException or UnauthorizedAccessException)
            {
                error.Text = "未保存：" + ex.Message;
                e.Cancel = true;
            }
        };
        CurrentEditor = editor;
        try
        {
            await FluentMessageBox.ShowCustomAsync(dialog, owner);
        }
        finally
        {
            CurrentEditor = null;
        }

        return saved;
    }
}
