using BetterGenshinImpact.Model.MaskMap;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel;

/// <summary>
/// 点位信息弹窗的 Linux 部分：弹出位置（点位在遮罩上的范围）与图片。
/// 图片由界面宿主提供加载方法（返回界面框架的位图），与 WPF 一样不缓存、GIF 只显示第一帧以外的由界面决定
/// </summary>
public partial class MaskMapPointInfoPopupViewModel
{
    /// <summary>按地址加载弹窗图片（界面宿主设置）；返回值直接用作图片源，失败返回 null</summary>
    public static Func<string, CancellationToken, Task<object?>>? ImageLoader { get; set; }

    /// <summary>点位在遮罩上的范围（遮罩坐标，同 WPF PlacementRect）</summary>
    [ObservableProperty] private System.Drawing.RectangleF _placementRect;

    [ObservableProperty] private object? _image;

    private void SetPlacement(double anchorX, double anchorY)
    {
        PlacementRect = new System.Drawing.RectangleF(
            (float)(anchorX - MaskMapPointStatic.Width / 2.0),
            (float)(anchorY - MaskMapPointStatic.Height / 2.0),
            (float)MaskMapPointStatic.Width,
            (float)MaskMapPointStatic.Height);
    }

    private static async Task<(bool IsGif, object? StaticImage, Uri? GifSourceUri, Stream? GifSourceStream)> LoadPopupImageNoCacheAsync(string url, CancellationToken ct)
    {
        if (ImageLoader is not { } loader)
        {
            return default;
        }

        try
        {
            return (false, await loader(url, ct), null, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return default;
        }
    }
}
