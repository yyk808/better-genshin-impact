using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Ui;
using System.ComponentModel;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Controls.Overlay;
using BetterGenshinImpact.GameTask.MapMask;
using BetterGenshinImpact.GameTask.Runtime;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using PresentMonFps;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Model.MaskMap;
using BetterGenshinImpact.Service.Model;
using BetterGenshinImpact.Service.Model.OverlayMetric;
using Vanara.PInvoke;
using MaskMapPoint = BetterGenshinImpact.Model.MaskMap.MaskMapPoint;
using MaskMapPointLabel = BetterGenshinImpact.Model.MaskMap.MaskMapPointLabel;
using Rect = System.Windows.Rect;

namespace BetterGenshinImpact.ViewModel
{
    public partial class MaskWindowViewModel : ObservableRecipient
    {
        /// <summary>
        /// 绘制层重绘上限约 30 fps
        /// </summary>
        private static readonly TimeSpan DrawingRefreshInterval = TimeSpan.FromMilliseconds(33);

        private readonly ILogger<MaskWindowViewModel> _logger;
        private readonly OverlayMetricsService _overlayMetricsService;
        private readonly IMaskMapPointService _maskMapPointService;
        private readonly IMaskWindowSnapshotSource<MaskWindowDrawingSnapshot> _drawingSource;
        private readonly IMaskWindowSnapshotSource<MaskWindowMapSnapshot> _mapSource;
        private readonly UiCoalescer _drawingCoalescer;
        private readonly UiCoalescer _mapCoalescer;
        private readonly UiCoalescer _metricsCoalescer;
        private OverlayMetricsSnapshot? _pendingMetricsSnapshot;
        private string? _pendingFps;

        [ObservableProperty] private Rect _windowRect;

        [ObservableProperty] private ObservableCollection<StatusItem> _statusList = [];

        public AllConfig Config { get; }

        /// <summary>
        /// 绘制层的快照，由 <see cref="IMaskWindowDrawingBoard"/> 写入后合并刷新
        /// </summary>
        [ObservableProperty] private MaskWindowDrawingSnapshot _drawing = MaskWindowDrawingSnapshot.Empty;

        /// <summary>
        /// 绘制层的全局样式，只在 Recognition* 配置或分辨率缩放变化时更新
        /// </summary>
        [ObservableProperty] private MaskWindowDrawingStyle _drawingStyle = MaskWindowDrawingStyle.Default;

        /// <summary>
        /// 大地图点位视口（2048 级地图坐标）
        /// </summary>
        [ObservableProperty] private Rect _bigMapViewport;

        /// <summary>
        /// 小地图点位视口
        /// </summary>
        [ObservableProperty] private Rect _miniMapViewport;

        /// <summary>
        /// 布局编辑模式或大地图界面需要交互，其余时候鼠标穿透到游戏
        /// </summary>
        public bool IsClickThrough => !(Config.MaskWindowConfig.OverlayLayoutEditEnabled || IsInBigMapUi);

        /// <summary>
        /// 关闭点击穿透时分层窗口按像素命中测试，准星像素会拦截点击，所以此时隐藏准星
        /// </summary>
        public bool IsCrosshairVisible => Config.MaskWindowConfig.CrosshairEnabled && IsClickThrough;

        [ObservableProperty] private string _fps = "0";

        [ObservableProperty] private string _overlayMetricsText = string.Empty;

        [ObservableProperty] private IReadOnlyList<OverlayMetricDisplayItem> _overlayMetricDisplayItems = [];

        public bool HasOverlayMetricsText => OverlayMetricDisplayItems.Count > 0;

        public bool IsOverlayMetricsVisible => Config?.MaskWindowConfig.ShowOverlayMetrics == true && HasOverlayMetricsText;
        
        [ObservableProperty] private double _maskWindowWidth;

        [ObservableProperty] private double _maskWindowHeight;

        /// <summary>
        /// 游戏分辨率相对于 1080P 的运行时缩放比例。
        /// </summary>
        [ObservableProperty] private double _scaleTo1080PRatio = 1.0;

        /// <summary>
        /// 遮罩窗口所在显示器的运行时 DPI 缩放比例。
        /// </summary>
        [ObservableProperty] private double _displayDpiScale = 1.0;

        private bool _metricsSubscribed;

        /// <summary>
        /// 用户是否已开启游戏帧率采样（勾选后保持开启，随截图器启停切换进程）
        /// </summary>
        private bool _fpsStarted;

        /// <summary>
        /// 当前 FPS 采样会话的取消源；截图器停止或重新绑定时取消。只在 UI 线程上读写
        /// </summary>
        private CancellationTokenSource? _fpsCts;

        /// <summary>
        /// 当前 FPS 采样会话的任务。重新开始前等待它结束，避免同名 ETW 会话冲突
        /// </summary>
        private Task _fpsTask = Task.CompletedTask;

        /// <summary>
        /// 游戏运行环境服务。读取当前游戏窗口时取 <see cref="GameRuntimeService.Current"/>，不缓存
        /// </summary>
        private readonly GameRuntimeService _runtimeService;

        /// <summary>
        /// 遮罩绘制层，遮罩配置变化时清空旧的识别结果
        /// </summary>
        private readonly IMaskWindowDrawingBoard _drawingBoard;

        public MaskWindowViewModel(
            IConfigService configService,
            OverlayMetricsService overlayMetricsService,
            IMaskMapPointService maskMapPointService,
            IMaskWindowSnapshotSource<MaskWindowDrawingSnapshot> drawingSource,
            IMaskWindowSnapshotSource<MaskWindowMapSnapshot> mapSource,
            ILogger<MaskWindowViewModel> logger,
            GameRuntimeService runtimeService,
            IMaskWindowDrawingBoard drawingBoard)
        {
            Config = configService.Get();
            _overlayMetricsService = overlayMetricsService;
            _maskMapPointService = maskMapPointService;
            _drawingSource = drawingSource;
            _mapSource = mapSource;
            _logger = logger;
            _runtimeService = runtimeService;
            _drawingBoard = drawingBoard;
            // 遮罩缩放、DPI 与 FPS 进程号跟随绑定变化（Started / Stopped 在 UI 线程上触发）
            _runtimeService.Started += OnRuntimeStarted;
            _runtimeService.Stopped += OnRuntimeStopped;

            _drawingCoalescer = new UiCoalescer(() => Drawing = _drawingSource.Current, DrawingRefreshInterval);
            _mapCoalescer = new UiCoalescer(ApplyMapSnapshot);
            _metricsCoalescer = new UiCoalescer(ApplyPendingMetrics, priority: DispatcherPriority.Background);
            _drawingSource.Changed += _drawingCoalescer.Request;
            _mapSource.Changed += _mapCoalescer.Request;
            Config.MaskWindowConfig.PropertyChanged += OnMaskWindowConfigPropertyChanged;
            DrawingStyle = CreateDrawingStyle();

            PointInfoPopup.ToggleHiddenRequested += (_, point) => ToggleMapPointHidden(point);

            WeakReferenceMessenger.Default.Register<PropertyChangedMessage<object>>(this, (sender, msg) =>
            {
                if (msg.PropertyName == "RefreshSettings")
                {
                    UIDispatcherHelper.Invoke(RefreshSettings);
                }
            });

            // VM 创建前可能已经有写入，先同步一次
            _drawingCoalescer.Request();
            _mapCoalescer.Request();
        }

        private void ApplyMapSnapshot()
        {
            var snapshot = _mapSource.Current;
            IsInBigMapUi = snapshot.IsInBigMap;
            BigMapViewport = snapshot.BigMapViewport.ToWpfRect();
            MiniMapViewport = snapshot.MiniMapViewport.ToWpfRect();
        }

        private void OnMaskWindowConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 遮罩配置任一属性变化时清掉旧的识别结果（原逻辑在 AllConfig.OnConfigChanged，见 task-context-removal.md 第 6.2 节）
            _drawingBoard.ClearAll();

            switch (e.PropertyName)
            {
                case nameof(MaskWindowConfig.OverlayLayoutEditEnabled):
                    OnPropertyChanged(nameof(IsClickThrough));
                    OnPropertyChanged(nameof(IsCrosshairVisible));
                    break;
                case nameof(MaskWindowConfig.CrosshairEnabled):
                    OnPropertyChanged(nameof(IsCrosshairVisible));
                    break;
                case nameof(MaskWindowConfig.RecognitionUseDrawableStyle):
                case nameof(MaskWindowConfig.RecognitionRectStrokeColor):
                case nameof(MaskWindowConfig.RecognitionRectStrokeThickness):
                case nameof(MaskWindowConfig.RecognitionLineStrokeColor):
                case nameof(MaskWindowConfig.RecognitionLineStrokeThickness):
                case nameof(MaskWindowConfig.RecognitionTextColor):
                case nameof(MaskWindowConfig.RecognitionTextFontSize):
                    DrawingStyle = CreateDrawingStyle();
                    break;
            }
        }

        private MaskWindowDrawingStyle CreateDrawingStyle()
        {
            var config = Config.MaskWindowConfig;
            return new MaskWindowDrawingStyle(
                config.RecognitionUseDrawableStyle,
                new MaskWindowDrawingStroke(
                    OverlayStyleHelper.ParseColorOrDefault(config.RecognitionRectStrokeColor, Colors.Red).ToMaskColor(),
                    Math.Max(0, config.RecognitionRectStrokeThickness)),
                new MaskWindowDrawingStroke(
                    OverlayStyleHelper.ParseColorOrDefault(config.RecognitionLineStrokeColor, Colors.Red).ToMaskColor(),
                    Math.Max(0, config.RecognitionLineStrokeThickness)),
                OverlayStyleHelper.ParseColorOrDefault(config.RecognitionTextColor, Colors.Black).ToMaskColor(),
                Math.Max(1, config.RecognitionTextFontSize) * ScaleTo1080PRatio);
        }

        partial void OnScaleTo1080PRatioChanged(double value)
        {
            DrawingStyle = CreateDrawingStyle();
        }

        private void InitializeStatusList()
        {
            // VM 是单例，窗口重建后 Loaded 会再次触发，避免重复添加
            if (StatusList.Count == 0)
            {
                StatusList.Add(new StatusItem("\uf256 拾取", Config.AutoPickConfig));
                StatusList.Add(new StatusItem("\uf075 剧情", Config.AutoSkipConfig));
                StatusList.Add(new StatusItem("\ue5c8 邀约", Config.AutoSkipConfig, "AutoHangoutEventEnabled"));
                StatusList.Add(new StatusItem("\uf578 钓鱼", Config.AutoFishingConfig));
                StatusList.Add(new StatusItem("\uf3c5 传送", Config.QuickTeleportConfig));
            }
        }

        [RelayCommand]
        private void OnLoaded()
        {
            RefreshSettings();
            InitializeStatusList();
            InitMetrics();
        }

        private void RefreshSettings()
        {
            Config.MaskWindowConfig.EnsureOverlayMetricItems();
            Config.MaskWindowConfig.MigrateLegacyOverlayMetricsLayout();
            RecalculateScaleTo1080PRatio();
            RefreshDisplayDpiScale();
            OnPropertyChanged(nameof(Config));
            OnPropertyChanged(nameof(IsOverlayMetricsVisible));
            DrawingStyle = CreateDrawingStyle();

            SyncSelectedMapPointApiProviderFromConfig();
            SyncSelectedHoYoLabLanguageFromConfig();
            LoadHiddenMapPointKeysFromStorage();
            InitMetrics();
        }

        /// <summary>
        /// 当显示器 DPI 发生变化时调用，刷新遮罩 UI 的运行时缩放比例。
        /// </summary>
        [RelayCommand]
        private void OnDpiChanged(double dpiScale)
        {
            DisplayDpiScale = double.IsFinite(dpiScale) && dpiScale > 0 ? dpiScale : 1.0;
        }

        private void RefreshDisplayDpiScale()
        {
            try
            {
                OnDpiChanged(DpiHelper.GetScaleY(_runtimeService.Current?.Window.Handle ?? 0));
            }
            catch
            {
                OnDpiChanged(1.0);
            }
        }

        /// <summary>
        /// 实时获取游戏窗口分辨率，计算相对于 1080P 的缩放比例。
        /// 当游戏窗口句柄短暂无效时，保留上次有效值，避免遮罩文字缩到极小。
        /// </summary>
        private void RecalculateScaleTo1080PRatio()
        {
            // 截图器未启动时句柄为 0，取不到客户区，保留上次的值
            var gameScreenRect = SystemControl.GetGameScreenRect(_runtimeService.Current?.Window.Handle ?? 0);
            if (gameScreenRect.Width > 0 && gameScreenRect.Height > 0)
            {
                // 取宽高缩放比中的较小值，与 SystemInfo.ScaleTo1080PRatio / GameCaptureRegion.DeriveTo1080P 保持一致
                ScaleTo1080PRatio = Math.Min(gameScreenRect.Width / 1920d, gameScreenRect.Height / 1080d);
            }
        }

        private void InitMetrics()
        {
            if (!_metricsSubscribed)
            {
                _overlayMetricsService.MetricsUpdated += OverlayMetricsServiceOnMetricsUpdated;
                _metricsSubscribed = true;
                OverlayMetricDisplayItems = _overlayMetricsService.CurrentSnapshot.Items;
                OverlayMetricsText = _overlayMetricsService.CurrentSnapshot.CombinedText;
            }

            if (Config?.MaskWindowConfig.ShowOverlayMetrics == true
                && Config.MaskWindowConfig.IsOverlayMetricEnabled(OverlayMetricItem.GameFps) == true
                && !_fpsStarted)
            {
                // FPS 由 PresentMon 长任务持续采样，只有用户勾选游戏帧率时才启动，避免无意义后台采样。
                // 之后随截图器的启动 / 停止切换采样的游戏进程
                _fpsStarted = true;
                RestartFpsSampling();
            }

            _overlayMetricsService.Refresh();
        }

        /// <summary>
        /// 截图器启动后：按新绑定的游戏窗口刷新缩放与 DPI，并切换 FPS 采样的进程。
        /// 遮罩窗口可能在绑定完成前就已加载，这里补一次
        /// </summary>
        private void OnRuntimeStarted(object? sender, EventArgs e)
        {
            RecalculateScaleTo1080PRatio();
            RefreshDisplayDpiScale();
            RestartFpsSampling();
        }

        /// <summary>
        /// 截图器停止后：结束 FPS 采样。未绑定就等于没有游戏，不再沿用旧进程
        /// </summary>
        private void OnRuntimeStopped(object? sender, EventArgs e)
        {
            StopFpsSampling();
        }

        /// <summary>
        /// 按当前运行环境的游戏进程重新开始 FPS 采样。用户未开启游戏帧率或截图器未启动时只停止旧会话。
        /// 只在 UI 线程上调用
        /// </summary>
        private void RestartFpsSampling()
        {
            StopFpsSampling();
            var pid = _runtimeService.Current?.Window.ProcessId ?? 0;
            if (!_fpsStarted || pid <= 0)
            {
                return;
            }

            var cts = new CancellationTokenSource();
            _fpsCts = cts;
            var token = cts.Token;
            var previous = _fpsTask;
            _fpsTask = Task.Run(async () =>
            {
                try
                {
                    // PresentMon 的 ETW 会话名固定，等上一轮会话完全退出后再开始
                    await previous;
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    await FpsInspector.StartForeverAsync(new FpsRequest((uint)pid), result =>
                    {
                        // PresentMon 回调在后台线程，只记录最新值，合并后在 UI 线程更新
                        Volatile.Write(ref _pendingFps, $"{result.Fps:0}");
                        _metricsCoalescer.Request();
                        _overlayMetricsService.UpdateGameFps(result.Fps);
                    }, token);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "游戏帧率采样结束");
                }
            });
        }

        /// <summary>
        /// 停止当前 FPS 采样会话。会话任务只读取取消令牌，取消源在这里直接释放
        /// </summary>
        private void StopFpsSampling()
        {
            var cts = _fpsCts;
            _fpsCts = null;
            if (cts == null)
            {
                return;
            }

            cts.Cancel();
            cts.Dispose();
        }

        private void OverlayMetricsServiceOnMetricsUpdated(object? sender, OverlayMetricsSnapshot snapshot)
        {
            // OverlayMetricsService 可能在计时器线程发布事件：只记录最新快照，不阻塞发布方
            Volatile.Write(ref _pendingMetricsSnapshot, snapshot);
            _metricsCoalescer.Request();
        }

        private void ApplyPendingMetrics()
        {
            var snapshot = Interlocked.Exchange(ref _pendingMetricsSnapshot, null);
            if (snapshot != null)
            {
                OverlayMetricDisplayItems = snapshot.Items;
                OverlayMetricsText = snapshot.CombinedText;
            }

            var fps = Interlocked.Exchange(ref _pendingFps, null);
            if (fps != null)
            {
                Fps = fps;
            }
        }

        [RelayCommand]
        private void OnOverlayLayoutCommitted(OverlayLayoutCommittedEventArgs args)
        {
            if (Config == null)
            {
                return;
            }

            if (args.Width <= 0 || args.Height <= 0)
            {
                return;
            }

            if (MaskWindowWidth <= 0 || MaskWindowHeight <= 0)
            {
                return;
            }

            var leftRatio = ToRatio(args.Left, MaskWindowWidth);
            var topRatio = ToRatio(args.Top, MaskWindowHeight);
            var widthRatio = ToRatio(args.Width, MaskWindowWidth);
            var heightRatio = ToRatio(args.Height, MaskWindowHeight);

            switch (args.LayoutKey)
            {
                case "LogTextBox":
                    Config.MaskWindowConfig.LogTextBoxLeftRatio = leftRatio;
                    Config.MaskWindowConfig.LogTextBoxTopRatio = topRatio;
                    Config.MaskWindowConfig.LogTextBoxWidthRatio = widthRatio;
                    Config.MaskWindowConfig.LogTextBoxHeightRatio = heightRatio;
                    break;
                case "StatusList":
                    Config.MaskWindowConfig.StatusListLeftRatio = leftRatio;
                    Config.MaskWindowConfig.StatusListTopRatio = topRatio;
                    Config.MaskWindowConfig.StatusListWidthRatio = widthRatio;
                    Config.MaskWindowConfig.StatusListHeightRatio = heightRatio;
                    break;
                case "Metrics":
                    Config.MaskWindowConfig.MetricsLeftRatio = leftRatio;
                    Config.MaskWindowConfig.MetricsTopRatio = topRatio;
                    Config.MaskWindowConfig.MetricsWidthRatio = widthRatio;
                    Config.MaskWindowConfig.MetricsHeightRatio = heightRatio;
                    break;
            }
        }

        [RelayCommand]
        private void OnWindowSizeChanged(Size size)
        {
            MaskWindowWidth = size.Width;
            MaskWindowHeight = size.Height;

            // 游戏分辨率变化时，重新计算 ScaleTo1080PRatio
            // 使日志字号实时适配新的游戏分辨率
            RecalculateScaleTo1080PRatio();
        }

        [RelayCommand]
        private void OnExitOverlayLayoutEditMode()
        {
            if (Config == null)
            {
                return;
            }

            Config.MaskWindowConfig.OverlayLayoutEditEnabled = false;
            _runtimeService.Current?.Window.Activate();
        }

        private static double ToRatio(double value, double baseSize)
        {
            if (double.IsNaN(value) || double.IsNaN(baseSize) || baseSize <= 0)
            {
                return 0;
            }

            var ratio = value / baseSize;
            return ratio switch
            {
                < 0 => 0,
                > 1 => 1,
                _ => ratio
            };
        }

        partial void OnOverlayMetricsTextChanged(string value)
        {
            OnPropertyChanged(nameof(IsOverlayMetricsVisible));
        }

        partial void OnOverlayMetricDisplayItemsChanged(IReadOnlyList<OverlayMetricDisplayItem> value)
        {
            OnPropertyChanged(nameof(HasOverlayMetricsText));
            OnPropertyChanged(nameof(IsOverlayMetricsVisible));
        }

        /// <summary>地图点位部分（MaskWindowViewModel.MapPoints.cs）切回界面线程：WPF 用 Dispatcher 的 Background 优先级</summary>
        private static async Task InvokeOnUiAsync(Action action) =>
            await Application.Current.Dispatcher.InvokeAsync(action, DispatcherPriority.Background);

        private static async Task<T> InvokeOnUiAsync<T>(Func<T> func) =>
            await Application.Current.Dispatcher.InvokeAsync(func, DispatcherPriority.Background);
    }

    /// <summary>点位标签项的图标（WPF ImageSource，来自 MapIconImageCache）</summary>
    public partial class MapLabelItemVm
    {
        [ObservableProperty] private ImageSource? _iconImage;

        public async Task LoadIconAsync(CancellationToken ct)
        {
            if (IconImage != null || string.IsNullOrEmpty(IconUrl))
            {
                return;
            }

            var img = await MapIconImageCache.GetAsync(IconUrl, ct);
            if (img == null)
            {
                return;
            }

            await Application.Current.Dispatcher.InvokeAsync(() => { IconImage = img; }, DispatcherPriority.Background);
        }
    }
}
