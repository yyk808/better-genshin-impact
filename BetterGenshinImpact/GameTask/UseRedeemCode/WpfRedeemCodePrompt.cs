using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BetterGenshinImpact.View.Windows;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace BetterGenshinImpact.GameTask.UseRedeemCode;

/// <summary>兑换码确认框的 WPF 实现：与迁移前 RedeemCodeManager 中的对话框完全相同</summary>
public static class WpfRedeemCodePrompt
{
    public static bool Confirm(IReadOnlyList<string> codes)
    {
        var multilineTextBox = new TextBox
        {
            TextWrapping = TextWrapping.Wrap,
            Height = 340,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = codes.Aggregate("", (current, code) => current + $"{code}\n"),
        };
        var p = new PromptDialog(
            "从剪切版中获取到下面的兑换码，是否自动使用？",
            "自动使用兑换码",
            multilineTextBox,
            null);
        p.Height = 500;
        p.ShowDialog();

        return p.DialogResult == true;
    }
}
