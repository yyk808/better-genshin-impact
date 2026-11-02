using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Model.MaskMap;
using BetterGenshinImpact.Service.Guide;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.ViewModel;

public partial class MaskMapPointInfoPopupViewModel : ObservableObject
{
    private readonly ILogger<MaskMapPointInfoPopupViewModel> _logger = CoreServices.GetLogger<MaskMapPointInfoPopupViewModel>();
    private CancellationTokenSource? _cts;
    private MemoryStream? _imageStream;

    public event EventHandler<MaskMapPoint?>? ToggleHiddenRequested;

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isTextLoading;
    [ObservableProperty] private string _textError = string.Empty;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private MaskMapPoint? _currentPoint;
    [ObservableProperty] private bool _isCurrentPointHidden;
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private IReadOnlyList<MaskMapLink> _urlList = Array.Empty<MaskMapLink>();
    [ObservableProperty] private bool _hasUrlList;
    [ObservableProperty] private bool _hasImage;
    [ObservableProperty] private bool _isImageLoading;
    [ObservableProperty] private string _imageError = string.Empty;
    [ObservableProperty] private bool _isGifImage;
    [ObservableProperty] private Uri? _gifSourceUri;
    [ObservableProperty] private Stream? _gifSourceStream;

    /// <summary>
    /// 悬浮小窗服务；子实例中不可用
    /// </summary>
    private static IGuideWindowService? GuideWindow => CoreServices.GetService<IGuideWindowService>();

    /// <summary>
    /// 查询点位详情的服务；未设置时取全局注册的点位服务
    /// </summary>
    public IMaskMapPointService? PointService { get; set; }

    /// <summary>
    /// 链接旁是否显示“换一种方式打开”的小按钮（悬浮小窗可用时）
    /// </summary>
    [ObservableProperty] private bool _isGuideWindowAvailable;

    /// <summary>
    /// 点击视频链接时是否默认在悬浮小窗打开（取自小窗设置“地图点位视频在小窗打开”）
    /// </summary>
    [ObservableProperty] private bool _isGuideWindowPreferred;

    partial void OnUrlListChanged(IReadOnlyList<MaskMapLink> value)
    {
        HasUrlList = value is { Count: > 0 };
    }

    /// <summary>打开点位信息弹窗；(anchorX, anchorY) 为点位在遮罩上的中心（遮罩坐标）</summary>
    public async Task ShowAsync(MaskMapPoint point, double anchorX, double anchorY, string title, bool isHidden, CancellationToken externalCt = default)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        var ct = _cts.Token;

        SetPlacement(anchorX, anchorY);

        Title = title ?? string.Empty;
        CurrentPoint = point;
        SetHiddenState(isHidden);
        Text = string.Empty;
        // 每次打开时读取小窗设置，设置页修改后立即生效
        var guide = GuideWindow;
        IsGuideWindowAvailable = guide is { IsAvailable: true };
        IsGuideWindowPreferred = IsGuideWindowAvailable && guide!.Config.OpenMapVideoInWindow;
        UrlList = point.VideoUrls;
        TextError = string.Empty;
        IsTextLoading = true;
        Image = null;
        HasImage = false;
        ImageError = string.Empty;
        IsImageLoading = false;
        IsGifImage = false;
        GifSourceUri = null;
        GifSourceStream = null;
        DisposeImageStream();
        IsLoading = true;
        IsOpen = true;

        try
        {
            var service = PointService ?? CoreServices.GetService<IMaskMapPointService>();
            if (service == null)
            {
                TextError = "地图服务未就绪";
                return;
            }

            var info = await service.GetPointInfoAsync(point, ct);
            ct.ThrowIfCancellationRequested();

            Text = string.IsNullOrEmpty(info.Text) ? "暂无描述" : info.Text;
            IsTextLoading = false;
            if (info.UrlList is { Count: > 0 })
            {
                UrlList = info.UrlList;
            }

            HasImage = !string.IsNullOrWhiteSpace(info.ImageUrl);
            if (HasImage)
            {
                IsImageLoading = true;
                try
                {
                    var (isGif, staticImage, gifUri, gifStream) = await LoadPopupImageNoCacheAsync(info.ImageUrl, ct);
                    ct.ThrowIfCancellationRequested();

                    IsGifImage = isGif;
                    GifSourceUri = gifUri;
                    GifSourceStream = gifStream;
                    Image = staticImage;

                    if (IsGifImage)
                    {
                        if (GifSourceUri == null && GifSourceStream == null)
                        {
                            ImageError = "图片加载失败";
                        }
                    }
                    else
                    {
                        if (Image == null)
                        {
                            ImageError = "图片加载失败";
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "加载点位图片失败");
                    ImageError = "图片加载失败";
                }
                finally
                {
                    IsImageLoading = false;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "查询地图点位详情失败");
            TextError = "查询失败";
        }
        finally
        {
            IsTextLoading = false;
            IsImageLoading = false;
            IsLoading = false;
        }
    }

    public void SetHiddenState(bool isHidden)
    {
        IsCurrentPointHidden = isHidden;
    }

    private void DisposeImageStream()
    {
        GifSourceStream = null;
        _imageStream?.Dispose();
        _imageStream = null;
    }

    [RelayCommand]
    private void ToggleHidden()
    {
        ToggleHiddenRequested?.Invoke(this, CurrentPoint);
    }

    [RelayCommand]
    public void Close()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        IsOpen = false;
        IsLoading = false;
        CurrentPoint = null;
        SetHiddenState(false);
        IsTextLoading = false;
        TextError = string.Empty;
        Text = string.Empty;
        UrlList = Array.Empty<MaskMapLink>();
        HasImage = false;
        IsImageLoading = false;
        ImageError = string.Empty;
        Image = null;
        IsGifImage = false;
        GifSourceUri = null;
        GifSourceStream = null;
        DisposeImageStream();
    }

    /// <summary>
    /// 点击链接：开启“地图点位视频在小窗打开”且是视频链接时在悬浮小窗打开，否则用系统浏览器打开
    /// </summary>
    [RelayCommand]
    private void OpenUrl(string? url)
    {
        url = NormalizeUrl(url);
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (IsGuideWindowPreferred && GuideLinkResolver.IsVideoUrl(url))
        {
            OpenInGuideWindow(url);
            return;
        }

        OpenInBrowser(url);
    }

    /// <summary>
    /// 链接旁的小按钮：默认在小窗打开时改用浏览器，默认用浏览器时改在小窗打开
    /// </summary>
    [RelayCommand]
    private void OpenUrlAlternate(string? url)
    {
        url = NormalizeUrl(url);
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (IsGuideWindowPreferred || !IsGuideWindowAvailable)
        {
            OpenInBrowser(url);
            return;
        }

        OpenInGuideWindow(url);
    }

    /// <summary>
    /// 在悬浮小窗中打开链接
    /// </summary>
    private void OpenInGuideWindow(string url)
    {
        if (GuideWindow is not { IsAvailable: true } guide)
        {
            OpenInBrowser(url);
            return;
        }

        guide.Open(new GuideOpenRequest(url, GuideOpenSource.MapPoint));
    }

    /// <summary>
    /// 用系统默认浏览器打开链接
    /// </summary>
    private void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "打开链接失败: {Url}", url);
        }
    }

    private static string NormalizeUrl(string? url)
    {
        url = (url ?? string.Empty).Trim();
        url = url.Trim('`').Trim('"').Trim('\'').Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var abs))
        {
            return abs.AbsoluteUri;
        }

        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            return "https:" + url;
        }

        if (url.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            return "https://" + url;
        }

        return url;
    }
}
