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
