using BetterGenshinImpact.Model;
using System.Windows;
using System.Windows.Controls;

namespace BetterGenshinImpact.Core.Script.Project;

/// <summary>脚本设置界面生成（原 ScriptProject.LoadSettingUi，脚本项目已移入 Core）</summary>
public static class ScriptProjectSettingUi
{
    /// <summary>按 manifest 的设置项生成脚本设置界面，没有设置项时返回 null</summary>
    public static ScrollViewer? LoadSettingUi(this ScriptProject project, object? context)
    {
        var settingItems = project.Manifest.LoadSettingItems(project.ProjectPath);
        if (settingItems.Count == 0)
        {
            return null;
        }

        var stackPanel = new StackPanel
        {
            Margin = new Thickness(0, 0, 20, 0) // 给右侧滚动条留出位置
        };
        foreach (var item in settingItems)
        {
            var controls = item.ToControl(context);
            foreach (var control in controls)
            {
                stackPanel.Children.Add(control);
            }
        }

        var scrollViewer = new ScrollViewer
        {
            Content = stackPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 350 // 设置最大高度
        };

        return scrollViewer;
    }
}
