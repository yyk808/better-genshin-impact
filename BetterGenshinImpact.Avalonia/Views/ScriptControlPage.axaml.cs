using System.ComponentModel;
using Avalonia.Controls;
using BetterGenshinImpact.Avalonia.ViewModels;

namespace BetterGenshinImpact.Avalonia.Views;

public partial class ScriptControlPage : UserControl
{
    private ScriptControlPageViewModel? _vm;

    public ScriptControlPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
            {
                _vm.PropertyChanged -= OnVmPropertyChanged;
            }

            _vm = DataContext as ScriptControlPageViewModel;
            if (_vm != null)
            {
                _vm.PropertyChanged += OnVmPropertyChanged;
                BuildEditor();
            }
        };
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScriptControlPageViewModel.Editor))
        {
            BuildEditor();
        }
    }

    // 项目行右键菜单（弹出层不在页面可视树内，命令在这里转给页面 ViewModel）
    private void OnRowOpenFolder(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is BetterGenshinImpact.Core.Script.Group.ScriptGroupProject p) _vm?.OpenProjectFolderCommand.Execute(p);
    }

    private void OnRowDeleteByFolder(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is BetterGenshinImpact.Core.Script.Group.ScriptGroupProject p) _vm?.DeleteProjectsByFolderCommand.Execute(p);
    }

    /// <summary>按当前设置面板生成表单</summary>
    private void BuildEditor()
    {
        var host = this.FindControl<ScrollViewer>("EditorHost")!;
        if (_vm?.Editor is not { } editor)
        {
            host.Content = null;
            return;
        }

        static void Changed()
        {
        }

        host.Content = editor.Kind switch
        {
            ScriptSettingsKind.GroupConfig => ScriptSettingsForms.BuildGroupConfig(editor.Group.Config, Changed),
            ScriptSettingsKind.Project => ScriptSettingsForms.BuildProjectSettings(editor.Project!, _vm.GlobalJsNotificationEnabled, Changed),
            _ => ScriptSettingsForms.BuildJsSettings(editor.Items!, editor.Project!.JsScriptSettingsObject!, Changed),
        };
    }
}
