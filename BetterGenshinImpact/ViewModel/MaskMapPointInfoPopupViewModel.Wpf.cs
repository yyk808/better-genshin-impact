using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterGenshinImpact.Infrastructure.Cache;
using BetterGenshinImpact.Model.MaskMap;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.ViewModel;

/// <summary>点位信息弹窗的 WPF 部分：弹出位置（Popup.PlacementRectangle）与图片（BitmapImage / GIF）</summary>
public partial class MaskMapPointInfoPopupViewModel
{
    /// <summary>
    /// 远程图片服务：点位图片经文件缓存下载，跟随网络与代理设置
    /// </summary>
    private static IRemoteImageService? Remote => CoreServices.GetService<IRemoteImageService>();
    [ObservableProperty] private Rect _placementRect = Rect.Empty;
    [ObservableProperty] private ImageSource? _image;

    private void SetPlacement(double anchorX, double anchorY)
    {
        PlacementRect = new Rect(
            anchorX - MaskMapPointStatic.Width / 2.0,
            anchorY - MaskMapPointStatic.Height / 2.0,
            MaskMapPointStatic.Width,
            MaskMapPointStatic.Height);
    }

    private async Task<(bool IsGif, ImageSource? StaticImage, Uri? GifSourceUri, Stream? GifSourceStream)> LoadPopupImageNoCacheAsync(
        string url,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return default;
        }

        try
        {
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // 经文件缓存获取，7 天内再次查看同一点位不重复下载
                var bytes = Remote is null ? null : await Remote.GetBytesAsync(CachePartitions.MapPointImages, url, ct);
                if (bytes is not { Length: > 0 })
                {
                    return default;
                }

                if (IsGifBytes(bytes))
                {
                    DisposeImageStream();
                    _imageStream = new MemoryStream(bytes, writable: false);
                    _imageStream.Position = 0;
                    return (IsGif: true, StaticImage: null, GifSourceUri: null, GifSourceStream: _imageStream);
                }

                var img = await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    using var ms = new MemoryStream(bytes, writable: false);
                    ms.Position = 0;
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    return (ImageSource)bmp;
                });

                return (IsGif: false, StaticImage: img, GifSourceUri: null, GifSourceStream: null);
            }

            if (LooksLikeGifUri(url))
            {
                if (Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var gifUri))
                {
                    DisposeImageStream();
                    return (IsGif: true, StaticImage: null, GifSourceUri: gifUri, GifSourceStream: null);
                }
            }

            var staticImg = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                DisposeImageStream();
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(url, UriKind.RelativeOrAbsolute);
                bmp.EndInit();
                bmp.Freeze();
                return (ImageSource)bmp;
            });
            return (IsGif: false, StaticImage: staticImg, GifSourceUri: null, GifSourceStream: null);
        }
        catch
        {
            return default;
        }
    }

    private static bool LooksLikeGifUri(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri))
        {
            var path = uri.IsAbsoluteUri ? uri.AbsolutePath : uri.OriginalString;
            var q = path.IndexOfAny(['?', '#']);
            if (q >= 0)
            {
                path = path[..q];
            }

            return path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
        }

        return url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGifBytes(byte[] bytes)
    {
        return bytes is
        [
            (byte)'G', (byte)'I', (byte)'F', (byte)'8',
            (byte)'7' or (byte)'9',
            (byte)'a',
            ..
        ];
    }
}
