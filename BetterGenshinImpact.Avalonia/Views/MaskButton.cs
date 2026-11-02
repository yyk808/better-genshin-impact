using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>
/// 遮罩上的按钮：只用 Border 画（Wayland 下遮罩内容脱离窗口离屏渲染，模板控件没有样式），
/// 点击由遮罩自己的命中测试分发（X11 来自窗口的指针事件，Wayland 来自覆盖层的 wl_pointer），悬停时换背景（同 WPF 按钮的 IsMouseOver）
/// </summary>
public sealed class MaskButton : Border
{
    private IBrush? _normal;
    private bool _hovered;

    /// <summary>点击（左键松开时）</summary>
    public Action? Click { get; set; }

    /// <summary>悬停时的背景（null = 不变）</summary>
    public IBrush? HoverBackground { get; set; }

    /// <summary>悬停时显示的子元素（如已选标签右上角的删除标记）</summary>
    public Control? HoverOnly { get; set; }

    /// <summary>悬停变化时（如已选标签悬停时换边框色）</summary>
    public Action<bool>? HoverChanged { get; set; }

    /// <summary>是否可用（不可用时半透明、不响应）</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            Opacity = value ? 1 : 0.4;
        }
    }

    private bool _enabled = true;

    public bool IsHovered
    {
        get => _hovered;
        set
        {
            if (_hovered == value) return;
            if (value) _normal = Background;
            _hovered = value;
            if (HoverBackground is not null) Background = value ? HoverBackground : _normal;
            if (HoverOnly is not null) HoverOnly.IsVisible = value;
            HoverChanged?.Invoke(value);
        }
    }

    public static MaskButton Text(string text, double fontSize = 12, FontFamily? font = null) => new()
    {
        Child = new TextBlock { Text = text, FontSize = fontSize, FontFamily = font ?? FontFamily.Default, Foreground = Brushes.White, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center },
    };
}
