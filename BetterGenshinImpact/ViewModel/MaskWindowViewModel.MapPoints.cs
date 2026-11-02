using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.MapMask;
using BetterGenshinImpact.Model.MaskMap;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaskMapPoint = BetterGenshinImpact.Model.MaskMap.MaskMapPoint;
using MaskMapPointLabel = BetterGenshinImpact.Model.MaskMap.MaskMapPointLabel;

namespace BetterGenshinImpact.ViewModel
{
    /// <summary>
    /// 遮罩的地图点位部分：标签树、选择与持久化、隐藏点位、数据源切换、加载点位、点位交互。
    /// 不依赖界面框架，Windows（WPF 遮罩）与 Linux（Avalonia 遮罩，Host.Common 直接编译本文件）共用；
    /// 平台部分（切回界面线程、图标图片）在 MaskWindowViewModel.cs / MapLabelItemVm 的平台文件
    /// </summary>
    public partial class MaskWindowViewModel
    {
        [ObservableProperty] private bool _isInBigMapUi;

        [ObservableProperty] private bool _isMapPointPickerOpen;

        [ObservableProperty] private bool _isMapLabelTreeLoading;

        [ObservableProperty] private bool _isMapLabelItemsLoading;

        [ObservableProperty] private string _mapLabelSearchText = string.Empty;

        [ObservableProperty] private ObservableCollection<MapLabelCategoryVm> _mapLabelCategories = [];

        [ObservableProperty] private ObservableCollection<MapLabelItemVm> _mapLabelItems = [];

        [ObservableProperty] private MapLabelCategoryVm? _selectedMapLabelCategory;

        [ObservableProperty] private ObservableCollection<MapLabelItemVm> _selectedMapLabelItems = [];

        [ObservableProperty] private ObservableCollection<MaskMapPoint> _mapPoints = [];

        [ObservableProperty] private ObservableCollection<MaskMapPointLabel> _mapPointLabels = [];

        [ObservableProperty] private bool _isMapPointsLoading;

        [ObservableProperty] private string _mapPointsLoadingText = "正在加载点位...";

        public int MapPointTotalCount => MapPoints.Count;

        public int VisibleMapPointCount => MapPoints.Count(x => !x.IsHidden);

        public int HiddenMapPointCount => MapPoints.Count(IsMapPointHidden);

        public bool HasMapPoints => MapPointTotalCount > 0;

        public bool HasHiddenMapPoints => HiddenMapPointCount > 0;

        public string MapPointVisibilitySummary => HasMapPoints
            ? $"{VisibleMapPointCount}/{MapPointTotalCount}"
            : "0/0";

        public double MiniMapOverlayLeftRatio => MapAssets.MimiMapRect1080P.X / 1920d;

        public double MiniMapOverlayTopRatio => MapAssets.MimiMapRect1080P.Y / 1080d;

        public double MiniMapOverlaySizeRatio => MapAssets.MimiMapRect1080P.Width / 1080d;

        public sealed record MapPointApiProviderOption(MapPointApiProvider Provider, string DisplayName);

        public sealed record MapLanguageOption(string Code, string DisplayName);

        public IReadOnlyList<MapPointApiProviderOption> MapPointApiProviderOptions { get; } =
        [
            new(MapPointApiProvider.MihoyoMap, "米游社大地图"),
            new(MapPointApiProvider.KongyingTavern, "空荧酒馆"),
            new(MapPointApiProvider.HoYoLab, "HoYoLab")
        ];

        public IReadOnlyList<MapLanguageOption> HoYoLabLanguageOptions { get; } =
        [
            new(MapMaskConfig.HoYoLabLanguageEnUs, "English (en-us)"),
            new(MapMaskConfig.HoYoLabLanguagePtPt, "Português (pt-pt)"),
            new(MapMaskConfig.HoYoLabLanguageEsEs, "Español (es-es)")
        ];

        [ObservableProperty] private MapPointApiProviderOption? _selectedMapPointApiProviderOption;

        [ObservableProperty] private MapLanguageOption? _selectedHoYoLabLanguageOption;

        public bool IsHoYoLabProviderSelected => SelectedMapPointApiProviderOption?.Provider == MapPointApiProvider.HoYoLab;

        public MaskMapPointInfoPopupViewModel PointInfoPopup { get; } = new();

        private bool _isMapLabelTreeLoaded;
        private int _mapLabelTreeLoadVersion;
        private CancellationTokenSource? _mapLabelItemsCts;
        private CancellationTokenSource? _mapPointListCts;
        private int _mapLabelItemsLoadVersion;
        private int _mapPointsLoadVersion;
        private readonly HashSet<string> _hiddenMapPointIds = new(StringComparer.Ordinal);
        private readonly SemaphoreSlim _iconLoadSemaphore = new(10, 10);
        private bool _isRestoringMapMaskState;

        /// <summary>
        /// 遮罩隐藏、最小化或地图视口变化时，关闭点位信息弹窗和点位选择器
        /// </summary>
        [RelayCommand]
        private void CloseTransientPopups()
        {
            PointInfoPopup.CloseCommand.Execute(null);
            IsMapPointPickerOpen = false;
        }

        [RelayCommand]
        private async Task ToggleMapPointPickerAsync()
        {
            if (!IsInBigMapUi)
            {
                IsMapPointPickerOpen = false;
                return;
            }

            IsMapPointPickerOpen = !IsMapPointPickerOpen;
            if (IsMapPointPickerOpen)
            {
                await EnsureLabelTreeLoadedAsync();
            }
        }

        [RelayCommand(AllowConcurrentExecutions = true)]
        private async Task SelectMapLabelCategory(MapLabelCategoryVm? category)
        {
            SelectedMapLabelCategory = category;
            await StartPopulateRightListAsync(category, MapLabelSearchText);
        }

        [RelayCommand]
        private async Task SelectMapLabelItem(MapLabelItemVm? item)
        {
            if (item == null)
            {
                return;
            }

            var existing = SelectedMapLabelItems.FirstOrDefault(x => x.Id == item.Id);
            if (existing != null)
            {
                SelectedMapLabelItems.Remove(existing);
            }
            else
            {
                SelectedMapLabelItems.Add(item);
                await EnsureIconLoadedAsync(item, CancellationToken.None);
            }

            SaveSelectedMapLabelItemsToStorage();
            StartRefreshSelectedMapPoints();
        }

        [RelayCommand]
        private void ResetSelectedMapLabelSelection()
        {
            if (SelectedMapLabelItems.Count == 0)
            {
                return;
            }

            SelectedMapLabelItems.Clear();
            SaveSelectedMapLabelItemsToStorage();
            StartRefreshSelectedMapPoints();
        }

        private void SyncSelectedMapPointApiProviderFromConfig()
        {
            var provider = Config.MapMaskConfig.MapPointApiProvider;
            SelectedMapPointApiProviderOption = MapPointApiProviderOptions.FirstOrDefault(x => x.Provider == provider)
                                                ?? MapPointApiProviderOptions.FirstOrDefault();
        }

        partial void OnSelectedMapPointApiProviderOptionChanged(MapPointApiProviderOption? value)
        {
            OnPropertyChanged(nameof(IsHoYoLabProviderSelected));
            if (value == null)
            {
                return;
            }

            _ = SwitchMapPointApiProviderAsync(value.Provider);
        }

        partial void OnSelectedHoYoLabLanguageOptionChanged(MapLanguageOption? value)
        {
            if (value == null)
            {
                return;
            }

            _ = SwitchHoYoLabLanguageAsync(value.Code);
        }

        private void SyncSelectedHoYoLabLanguageFromConfig()
        {
            var lang = HoYoLabMapApiService.NormalizeLanguage(Config.MapMaskConfig.HoYoLabLanguage);
            SelectedHoYoLabLanguageOption = HoYoLabLanguageOptions.FirstOrDefault(x => x.Code == lang)
                                         ?? HoYoLabLanguageOptions.FirstOrDefault();
        }

        private async Task SwitchHoYoLabLanguageAsync(string language)
        {
            try
            {
                var normalized = HoYoLabMapApiService.NormalizeLanguage(language);
                var mapMaskConfig = Config.MapMaskConfig;
                if (mapMaskConfig.HoYoLabLanguage == normalized)
                {
                    return;
                }

                mapMaskConfig.HoYoLabLanguage = normalized;
                if (Config != null)
                {
                    Config.MapMaskConfig.HoYoLabLanguage = normalized;
                }

                if (mapMaskConfig.MapPointApiProvider == MapPointApiProvider.HoYoLab)
                {
                    await ResetAndReloadMapPointPickerAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "切换HoYoLab语言时发生异常");
            }
        }

        private async Task SwitchMapPointApiProviderAsync(MapPointApiProvider provider)
        {
            try
            {
                var mapMaskConfig = Config.MapMaskConfig;
                if (mapMaskConfig.MapPointApiProvider == provider)
                {
                    return;
                }

                mapMaskConfig.MapPointApiProvider = provider;
                if (Config != null)
                {
                    Config.MapMaskConfig.MapPointApiProvider = provider;
                }

                await ResetAndReloadMapPointPickerAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "切换地图点位来源时发生异常");
            }
        }

        private async Task ResetAndReloadMapPointPickerAsync()
        {
            _mapLabelItemsCts?.Cancel();
            _mapPointListCts?.Cancel();
            PointInfoPopup.Close();

            Interlocked.Increment(ref _mapLabelTreeLoadVersion);
            _isMapLabelTreeLoaded = false;
            MapLabelSearchText = string.Empty;

            await InvokeOnUiAsync(() =>
            {
                SelectedMapLabelItems.Clear();
                SelectedMapLabelCategory = null;
                MapLabelCategories = [];
                MapLabelItems = [];
                MapPointLabels = [];
                ReplaceMapPoints(Array.Empty<MaskMapPoint>());
            });

            if (IsMapPointPickerOpen)
            {
                await EnsureLabelTreeLoadedAsync();
            }
        }

        partial void OnIsInBigMapUiChanged(bool value)
        {
            OnPropertyChanged(nameof(IsClickThrough));
            OnPropertyChanged(nameof(IsCrosshairVisible));

            if (!value)
            {
                IsMapPointPickerOpen = false;
                PointInfoPopup.Close();
                return;
            }

            if (!_isMapLabelTreeLoaded && HasSavedMapLabelSelection())
            {
                _ = EnsureLabelTreeLoadedAsync();
            }
        }

        partial void OnMapLabelSearchTextChanged(string value)
        {
            _ = StartPopulateRightListAsync(SelectedMapLabelCategory, value);
        }

        private async Task EnsureLabelTreeLoadedAsync()
        {
            if (_isMapLabelTreeLoaded || IsMapLabelTreeLoading)
            {
                return;
            }

            var loadVersion = _mapLabelTreeLoadVersion;
            IsMapLabelTreeLoading = true;
            try
            {
                var service = _maskMapPointService;
                if (service == null)
                {
                    MapLabelCategories = [];
                    MapLabelItems = [];
                    SelectedMapLabelCategory = null;
                    return;
                }

                var categories = await service.GetLabelCategoriesAsync();
                if (loadVersion != _mapLabelTreeLoadVersion)
                {
                    return;
                }
                if (categories.Count == 0)
                {
                    MapLabelCategories = [];
                    MapLabelItems = [];
                    SelectedMapLabelCategory = null;
                    return;
                }

                var vms = categories.Select(x => new MapLabelCategoryVm(x)).ToList();
                MapLabelCategories = new ObservableCollection<MapLabelCategoryVm>(vms);
                await SelectMapLabelCategory(MapLabelCategories.FirstOrDefault());
                await RestoreSelectedMapLabelItemsFromStorageAsync();
                _isMapLabelTreeLoaded = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "加载地图点位树时发生异常");
            }
            finally
            {
                IsMapLabelTreeLoading = false;
                // if (!_isMapLabelTreeLoaded && IsMapPointPickerOpen && loadVersion != _mapLabelTreeLoadVersion)
                // {
                //     _ = EnsureLabelTreeLoadedAsync();
                // }
            }
        }

        private bool HasSavedMapLabelSelection()
        {
            return MapMaskStateStorage.Read(GetMapMaskDataSourceKey()).SelectedLabelItems.Count > 0;
        }

        private async Task RestoreSelectedMapLabelItemsFromStorageAsync()
        {
            var savedItems = MapMaskStateStorage.Read(GetMapMaskDataSourceKey()).SelectedLabelItems;
            if (savedItems.Count == 0)
            {
                return;
            }

            _isRestoringMapMaskState = true;
            try
            {
                SelectedMapLabelItems.Clear();
                foreach (var savedItem in savedItems)
                {
                    var item = FindMapLabelItem(savedItem.Id)
                               ?? new MapLabelItemVm(new MaskMapPointLabel
                               {
                                   LabelId = savedItem.Id,
                                   LabelIds = savedItem.LabelIds ?? [],
                                   ParentId = savedItem.ParentId,
                                   Name = savedItem.Name,
                                   IconUrl = savedItem.IconUrl,
                                   PointCount = savedItem.PointCount
                               });

                    if (SelectedMapLabelItems.Any(x => x.Id == item.Id))
                    {
                        continue;
                    }

                    SelectedMapLabelItems.Add(item);
                    await EnsureIconLoadedAsync(item, CancellationToken.None);
                }
            }
            finally
            {
                _isRestoringMapMaskState = false;
            }

            if (SelectedMapLabelItems.Count > 0)
            {
                StartRefreshSelectedMapPoints();
            }
        }

        private MapLabelItemVm? FindMapLabelItem(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            return MapLabelCategories
                .SelectMany(x => x.Items)
                .FirstOrDefault(x => x.Id == id);
        }

        private void SaveSelectedMapLabelItemsToStorage()
        {
            if (_isRestoringMapMaskState)
            {
                return;
            }

            var selectedItems = SelectedMapLabelItems
                .Select(x => new MapMaskSelectedLabelState
                {
                    Id = x.Id,
                    LabelIds = x.LabelIds.ToList(),
                    ParentId = x.ParentId,
                    Name = x.Name,
                    IconUrl = x.IconUrl,
                    PointCount = x.PointCount
                })
                .ToList();
            MapMaskStateStorage.Update(GetMapMaskDataSourceKey(), state => state.SelectedLabelItems = selectedItems);
        }

        private void StartRefreshSelectedMapPoints()
        {
            var loadVersion = Interlocked.Increment(ref _mapPointsLoadVersion);
            _mapPointListCts?.Cancel();
            _mapPointListCts = new CancellationTokenSource();
            var ct = _mapPointListCts.Token;
            _ = RefreshSelectedMapPointsAsync(loadVersion, ct);
        }

        private async Task RefreshSelectedMapPointsAsync(int loadVersion, CancellationToken ct)
        {
            try
            {
                if (loadVersion == _mapPointsLoadVersion)
                {
                    IsMapPointsLoading = true;
                }

                var selectedItems = await InvokeOnUiAsync(
                    () => SelectedMapLabelItems.ToList());
                ct.ThrowIfCancellationRequested();
                if (selectedItems.Count == 0)
                {
                    await InvokeOnUiAsync(() =>
                    {
                        MapPointLabels = [];
                        ReplaceMapPoints(Array.Empty<MaskMapPoint>());
                    });
                    return;
                }

                var service = _maskMapPointService;
                if (service == null)
                {
                    return;
                }

                var selectedModels = selectedItems.Select(x => x.ToModel()).ToList();
                var result = await service.GetPointsAsync(selectedModels, ct);
                ct.ThrowIfCancellationRequested();

                await InvokeOnUiAsync(() =>
                {
                    MapPointLabels = new ObservableCollection<MaskMapPointLabel>(result.Labels);
                    ReplaceMapPoints(result.Points);
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "刷新地图点位列表时发生异常");
            }
            finally
            {
                if (loadVersion == _mapPointsLoadVersion)
                {
                    IsMapPointsLoading = false;
                }
            }
        }

        private Task StartPopulateRightListAsync(MapLabelCategoryVm? category, string? searchText)
        {
            var loadVersion = Interlocked.Increment(ref _mapLabelItemsLoadVersion);
            _mapLabelItemsCts?.Cancel();
            _mapLabelItemsCts?.Dispose();
            _mapLabelItemsCts = new CancellationTokenSource();
            var ct = _mapLabelItemsCts.Token;
            return PopulateRightListAsync(loadVersion, category, searchText, ct);
        }

        private async Task PopulateRightListAsync(int loadVersion, MapLabelCategoryVm? category, string? searchText, CancellationToken ct)
        {
            try
            {
                if (loadVersion == _mapLabelItemsLoadVersion)
                {
                    IsMapLabelItemsLoading = true;
                }
                await InvokeOnUiAsync(() =>
                {
                    if (loadVersion == _mapLabelItemsLoadVersion)
                    {
                        MapLabelItems = [];
                    }
                });

                if (category?.Items == null)
                {
                    return;
                }

                var src = category.Items.AsEnumerable();
                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    var q = searchText.Trim();
                    src = src.Where(x => x.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
                }

                const int batchSize = 24;
                var batch = new List<MapLabelItemVm>(batchSize);

                foreach (var item in src)
                {
                    ct.ThrowIfCancellationRequested();
                    if (loadVersion != _mapLabelItemsLoadVersion)
                    {
                        return;
                    }
                    batch.Add(item);
                    if (batch.Count < batchSize)
                    {
                        continue;
                    }

                    var snapshot = batch.ToArray();
                    batch.Clear();
                    await InvokeOnUiAsync(() =>
                    {
                        if (loadVersion != _mapLabelItemsLoadVersion)
                        {
                            return;
                        }
                        foreach (var it in snapshot)
                        {
                            MapLabelItems.Add(it);
                        }
                    });

                    foreach (var it in snapshot)
                    {
                        _ = EnsureIconLoadedAsync(it, ct);
                    }

                    await Task.Delay(1, ct);
                }

                if (batch.Count > 0)
                {
                    var snapshot = batch.ToArray();
                    await InvokeOnUiAsync(() =>
                    {
                        if (loadVersion != _mapLabelItemsLoadVersion)
                        {
                            return;
                        }
                        foreach (var it in snapshot)
                        {
                            MapLabelItems.Add(it);
                        }
                    });

                    foreach (var it in snapshot)
                    {
                        _ = EnsureIconLoadedAsync(it, ct);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (loadVersion == _mapLabelItemsLoadVersion)
                {
                    IsMapLabelItemsLoading = false;
                }
            }
        }

        private async Task EnsureIconLoadedAsync(MapLabelItemVm item, CancellationToken ct)
        {
            if (item.IconImage != null || string.IsNullOrEmpty(item.IconUrl))
            {
                return;
            }


            try
            {
                await _iconLoadSemaphore.WaitAsync(ct);
                try
                {
                    await item.LoadIconAsync(ct);
                }
                finally
                {
                    _iconLoadSemaphore.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "加载地图点位图标失败");
            }
        }

        [RelayCommand]
        private async Task OnPointClick(MaskMapPointClickArgs? args)
        {
            var point = args?.Point;
            if (point == null || !IsInBigMapUi)
            {
                return;
            }

            await PointInfoPopup.ShowAsync(point, args!.AnchorPosition.X, args.AnchorPosition.Y, ResolvePointTitle(point), IsMapPointHidden(point));
        }

        private string ResolvePointTitle(MaskMapPoint point)
        {
            var labelName = MapPointLabels.FirstOrDefault(x => x.LabelId == point.LabelId)?.Name;
            if (!string.IsNullOrWhiteSpace(labelName))
            {
                return labelName;
            }

            return $"点位 {point.Id}";
        }

        [RelayCommand]
        private Task OnPointRightClick(MaskMapPoint? point)
        {
            ToggleMapPointHidden(point);
            return Task.CompletedTask;
        }

        [RelayCommand]
        private Task OnPointHover(MaskMapPoint? point)
        {
            if (point != null)
            {
                // 悬停逻辑
            }

            return Task.CompletedTask;
        }

        [RelayCommand]
        private void ToggleMapPointHidden(MaskMapPoint? point)
        {
            if (point == null)
            {
                return;
            }

            var key = GetMapPointKey(point);
            if (!_hiddenMapPointIds.Add(key))
            {
                _hiddenMapPointIds.Remove(key);
            }

            ApplyMapPointHiddenStates();
            SaveHiddenMapPointKeysToStorage();
            SyncPointInfoPopupHiddenState();
        }

        [RelayCommand]
        private void HideAllMapPoints()
        {
            if (MapPoints.Count == 0)
            {
                return;
            }

            _hiddenMapPointIds.Clear();
            foreach (var point in MapPoints)
            {
                _hiddenMapPointIds.Add(GetMapPointKey(point));
            }

            ApplyMapPointHiddenStates();
            SaveHiddenMapPointKeysToStorage();
            SyncPointInfoPopupHiddenState();
        }

        [RelayCommand]
        private void ShowAllMapPoints()
        {
            if (_hiddenMapPointIds.Count == 0)
            {
                return;
            }

            _hiddenMapPointIds.Clear();
            ApplyMapPointHiddenStates();
            SaveHiddenMapPointKeysToStorage();
            SyncPointInfoPopupHiddenState();
        }

        private void ReplaceMapPoints(IEnumerable<MaskMapPoint> points)
        {
            LoadHiddenMapPointKeysFromStorage();
            var pointList = points.ToList();
            foreach (var point in pointList)
            {
                point.IsHidden = IsMapPointHidden(point);
            }

            MapPoints = new ObservableCollection<MaskMapPoint>(pointList);
            NotifyMapPointVisibilityPropertiesChanged();
            SyncPointInfoPopupHiddenState();
        }

        private void ApplyMapPointHiddenStates()
        {
            var pointList = MapPoints.ToList();
            foreach (var point in pointList)
            {
                point.IsHidden = IsMapPointHidden(point);
            }

            MapPoints = new ObservableCollection<MaskMapPoint>(pointList);
            NotifyMapPointVisibilityPropertiesChanged();
        }

        private bool IsMapPointHidden(MaskMapPoint point)
        {
            return _hiddenMapPointIds.Contains(GetMapPointKey(point));
        }

        private string GetMapPointKey(MaskMapPoint point)
        {
            var mapMaskConfig = Config.MapMaskConfig;
            var provider = mapMaskConfig.MapPointApiProvider.ToString();
            if (mapMaskConfig.MapPointApiProvider != MapPointApiProvider.HoYoLab)
            {
                return $"{provider}:{point.Id}";
            }

            var language = HoYoLabMapApiService.NormalizeLanguage(mapMaskConfig.HoYoLabLanguage);
            return $"{provider}:{language}:{point.Id}";
        }

        private string GetMapMaskDataSourceKey()
        {
            var mapMaskConfig = Config.MapMaskConfig;
            var provider = mapMaskConfig.MapPointApiProvider.ToString();
            if (mapMaskConfig.MapPointApiProvider != MapPointApiProvider.HoYoLab)
            {
                return provider;
            }

            var language = HoYoLabMapApiService.NormalizeLanguage(mapMaskConfig.HoYoLabLanguage);
            return $"{provider}_{language}";
        }

        private void LoadHiddenMapPointKeysFromStorage()
        {
            _hiddenMapPointIds.Clear();

            var keys = MapMaskStateStorage.Read(GetMapMaskDataSourceKey()).HiddenMapPointKeys;
            foreach (var key in keys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    _hiddenMapPointIds.Add(key);
                }
            }
        }

        private void SaveHiddenMapPointKeysToStorage()
        {
            if (_isRestoringMapMaskState)
            {
                return;
            }

            var hiddenMapPointKeys = _hiddenMapPointIds
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();
            MapMaskStateStorage.Update(GetMapMaskDataSourceKey(), state => state.HiddenMapPointKeys = hiddenMapPointKeys);
        }

        private void SyncPointInfoPopupHiddenState()
        {
            var currentPoint = PointInfoPopup.CurrentPoint;
            if (currentPoint != null)
            {
                PointInfoPopup.SetHiddenState(IsMapPointHidden(currentPoint));
            }
        }

        partial void OnMapPointsChanged(ObservableCollection<MaskMapPoint> value)
        {
            NotifyMapPointVisibilityPropertiesChanged();
        }

        private void NotifyMapPointVisibilityPropertiesChanged()
        {
            OnPropertyChanged(nameof(MapPointTotalCount));
            OnPropertyChanged(nameof(VisibleMapPointCount));
            OnPropertyChanged(nameof(HiddenMapPointCount));
            OnPropertyChanged(nameof(HasMapPoints));
            OnPropertyChanged(nameof(HasHiddenMapPoints));
            OnPropertyChanged(nameof(MapPointVisibilitySummary));
        }
    }

    public partial class MapLabelCategoryVm : ObservableObject
    {
        public string Id { get; }
        public string Name { get; }
        public string IconUrl { get; }
        public ObservableCollection<MapLabelItemVm> Items { get; }

        public MapLabelCategoryVm(MaskMapPointLabel category)
        {
            Id = category.LabelId;
            Name = category.Name;
            IconUrl = category.IconUrl;
            Items = new ObservableCollection<MapLabelItemVm>((category.Children ?? Array.Empty<MaskMapPointLabel>()).Select(x => new MapLabelItemVm(x)));
        }
    }

    public partial class MapLabelItemVm : ObservableObject
    {
        public string Id { get; }
        public IReadOnlyList<string> LabelIds { get; }
        public string Name { get; }
        public string IconUrl { get; }
        public int PointCount { get; }
        public string ParentId { get; }

        public MapLabelItemVm(MaskMapPointLabel item)
        {
            Id = item.LabelId;
            LabelIds = item.LabelIds is { Count: > 0 } ? item.LabelIds : new[] { item.LabelId };
            Name = item.Name;
            IconUrl = item.IconUrl;
            PointCount = item.PointCount;
            ParentId = item.ParentId;
        }

        public MaskMapPointLabel ToModel()
        {
            return new MaskMapPointLabel
            {
                LabelId = Id,
                LabelIds = LabelIds,
                ParentId = ParentId,
                Name = Name,
                IconUrl = IconUrl,
                PointCount = PointCount
            };
        }
    }
}
