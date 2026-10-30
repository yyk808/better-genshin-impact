using BetterGenshinImpact.Platform;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers.Security;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.GameTask.UseRedeemCode;

public class RedeemCodeManager
{
    /// <summary>
    /// 配置根。静态方法没有创建方可以传参，暂时读配置根（见 task-context-removal.md 第 5 节）
    /// </summary>
    private static AllConfig Config => ConfigHost.Config!;

    public static HashSet<string> CancelClipboardHash { get; } = [];

    /// <summary>
    /// 询问用户是否使用从剪贴板中识别到的兑换码，返回 true 表示确认。
    /// Windows 主程序启动时设为原来的 PromptDialog 多行文本框对话框；
    /// 未设置时用 <see cref="UiHost.Dialogs"/> 的确认框（兑换码逐行列在消息里）。
    /// </summary>
    public static System.Func<IReadOnlyList<string>, bool> ConfirmCodes { get; set; } = static codes =>
        UiHost.Dialogs.Show("从剪切版中获取到下面的兑换码，是否自动使用？\n" + string.Join("\n", codes),
            "自动使用兑换码", DialogKind.Information, DialogButtons.OkCancel) == DialogResult.Ok;

    private static readonly Regex UrlStripRegex =
        new(@"https?://\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    public static void AddNotDetectClipboardText(string clipboardText)
    {
        var md5Hash = MD5Helper.ComputeMD5(clipboardText);
        CancelClipboardHash.Add(md5Hash);
    }
    
    public static async Task ImportFromClipboard(string clipboardText)
    {
        if (!Config.AutoRedeemCodeConfig.ClipboardListenerEnabled)
        {
            return;
        }
        
        var md5Hash = MD5Helper.ComputeMD5(clipboardText);
        if (CancelClipboardHash.Contains(md5Hash))
        {
            return;
        }

        var cleanedText = UrlStripRegex.Replace(clipboardText, "");  
        var codes = ExtractAllCodes(cleanedText);
        if (codes.Count == 0)
        {
            return;
        }

        if (!ConfirmCodes(codes))
        {
            if (CancelClipboardHash.Count > 10)
            {
                CancelClipboardHash.Clear();
            }
            CancelClipboardHash.Add(md5Hash);
            return;
        }

        UiHost.Clipboard.Clear();
        // 任务在拿到本轮运行环境之后才构造
        await new TaskRunner().RunSoloTaskAsync(rt => new UseRedemptionCodeTask(rt), new UseRedemptionCodeParam { Codes = codes });
    }

    public static List<string> ExtractAllCodes(string clipboardText)
    {
        if (string.IsNullOrEmpty(clipboardText))
        {
            return [];
        }

        var regex = new Regex(@"(?<![A-Z0-9])(?=[A-Z0-9]*[A-Z])[A-Z0-9]{12}(?![A-Z0-9])");
        return regex.Matches(clipboardText)
            .Select(m => m.Value)
            .ToList();
    }
}
