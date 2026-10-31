using Avalonia.Controls;
using Avalonia.Input;
using BetterGenshinImpact.Avalonia.ViewModels;
using BetterGenshinImpact.GameTask.Music.Model;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>自动演奏页（对应 WPF MusicPage）</summary>
public partial class MusicPage : UserControl
{
    public MusicPage()
    {
        InitializeComponent();
        // 进度条松开时定位（与 WPF SliderSeekBehavior 相同：拖动中不跟随播放进度）
        SeekSlider.AddHandler(PointerReleasedEvent, (_, _) => (DataContext as MusicPageViewModel)?.Seek(SeekSlider.Value), handledEventsToo: true);
    }

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: PerformanceScore score } && DataContext is MusicPageViewModel vm)
        {
            vm.PlaySelectedCommand.Execute(score);
        }
    }
}
