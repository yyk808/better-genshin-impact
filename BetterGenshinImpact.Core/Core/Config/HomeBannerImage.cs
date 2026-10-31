using System.IO;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
/// 首页横幅图片：用户自定义图片（User/Images/custom_banner.jpg）存在时使用它，否则使用内置默认图（WPF 与 Avalonia 共用）
/// </summary>
public static class HomeBannerImage
{
    /// <summary>自定义横幅图片路径</summary>
    public static string CustomPath => Global.Absolute("User/Images/custom_banner.jpg");

    /// <summary>存在自定义图片时返回其完整路径，否则返回 null（使用内置默认图）</summary>
    public static string? ResolveCustomFile() => File.Exists(CustomPath) ? Path.GetFullPath(CustomPath) : null;

    /// <summary>选择本地图片的标题与过滤（与 WPF 相同）</summary>
    public const string PickTitle = "选择背景图片";

    /// <summary>可选的图片扩展名</summary>
    public static readonly string[] ImagePatterns = ["*.jpg", "*.jpeg", "*.png", "*.bmp", "*.gif"];

    public const string ChangedText = "背景图片更换成功！";
    public const string ResetText = "已恢复为默认背景图片！";
    public const string ResetDeleteFailedText = "已恢复为默认背景图片！但清除自定义图片失败，请手动删除文件。";

    /// <summary>把选中的图片复制为自定义横幅（覆盖），返回自定义图片完整路径</summary>
    public static string CopyToCustom(string sourceFile)
    {
        var directory = Path.GetDirectoryName(CustomPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Copy(sourceFile, CustomPath, true);
        return Path.GetFullPath(CustomPath);
    }

    /// <summary>删除自定义横幅（不存在时忽略）</summary>
    public static void DeleteCustom()
    {
        var full = Path.GetFullPath(CustomPath);
        if (File.Exists(full))
        {
            File.Delete(full);
        }
    }
}
