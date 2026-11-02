using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.ViewModel;

/// <summary>
/// Linux 遮罩的地图点位模型：地图点位逻辑（标签树、选择持久化、隐藏点位、数据源切换、加载点位、点位交互）
/// 由 WPF 的 MaskWindowViewModel.MapPoints.cs 直接编译，这里只有 Linux 宿主需要的平台部分——
/// 构造、地图状态快照（MapMaskTrigger 写入的大地图 / 小地图视口）、切回界面线程。
/// WPF MaskWindowViewModel 的其余部分（识别绘制、状态栏、日志、FPS 等）在 Avalonia 遮罩里另有实现，不在这里
/// </summary>
public partial class MaskWindowViewModel : ObservableObject
{
    private readonly ILogger<MaskWindowViewModel> _logger;
    private readonly IMaskMapPointService _maskMapPointService;

    public AllConfig Config { get; }

    /// <summary>大地图视口（游戏大地图在地图图片上的范围，MapMaskTrigger 计算）</summary>
    [ObservableProperty] private MaskRect _bigMapViewport;

    /// <summary>小地图视口</summary>
    [ObservableProperty] private MaskRect _miniMapViewport;

    public MaskWindowViewModel(AllConfig config, IMaskMapPointService maskMapPointService, ILogger<MaskWindowViewModel> logger)
    {
        Config = config;
        _maskMapPointService = maskMapPointService;
        _logger = logger;
        PointInfoPopup.PointService = maskMapPointService;
        PointInfoPopup.ToggleHiddenRequested += (_, point) => ToggleMapPointHidden(point);
        // 同 WPF RefreshSettings 中与地图点位有关的部分
        SyncSelectedMapPointApiProviderFromConfig();
        SyncSelectedHoYoLabLanguageFromConfig();
        LoadHiddenMapPointKeysFromStorage();
    }

    /// <summary>大地图界面或布局编辑模式时遮罩接收鼠标（同 WPF）</summary>
    public bool IsClickThrough => !(Config.MaskWindowConfig.OverlayLayoutEditEnabled || IsInBigMapUi);

    public bool IsCrosshairVisible => Config.MaskWindowConfig.CrosshairEnabled && IsClickThrough;

    /// <summary>应用地图状态快照（同 WPF ApplyMapSnapshot），在界面线程调用</summary>
    public void ApplyMapSnapshot(MaskWindowMapSnapshot snapshot)
    {
        IsInBigMapUi = snapshot.IsInBigMap;
        BigMapViewport = snapshot.BigMapViewport;
        MiniMapViewport = snapshot.MiniMapViewport;
    }

    private static Task InvokeOnUiAsync(Action action) =>
        UiHost.Dispatcher.InvokeAsync(() =>
        {
            action();
            return true;
        });

    private static Task<T> InvokeOnUiAsync<T>(Func<T> func) => UiHost.Dispatcher.InvokeAsync(func);
}

/// <summary>点位标签项的图标：Linux 由界面宿主提供加载方法（返回界面框架的位图），未提供时不显示图标</summary>
public partial class MapLabelItemVm
{
    /// <summary>按图标地址加载图标（界面宿主设置，返回值直接用作图片源）</summary>
    public static Func<string, CancellationToken, Task<object?>>? IconLoader { get; set; }

    [ObservableProperty] private object? _iconImage;

    public async Task LoadIconAsync(CancellationToken ct)
    {
        if (IconImage != null || string.IsNullOrEmpty(IconUrl) || IconLoader is not { } loader)
        {
            return;
        }

        var img = await loader(IconUrl, ct);
        if (img == null)
        {
            return;
        }

        UiHost.Dispatcher.Post(() => IconImage = img);
    }
}
