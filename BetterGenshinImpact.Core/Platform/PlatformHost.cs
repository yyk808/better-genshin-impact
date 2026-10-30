using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Platform;

/// <summary>屏幕信息</summary>
public interface IScreenInfo
{
    /// <summary>主屏幕当前的物理分辨率（像素）。每次调用重新读取，分辨率变化后立即生效</summary>
    PixelSize PrimaryScreenSize { get; }
}

/// <summary>
/// 平台服务中不经依赖注入使用的部分（区域点击、键鼠宏等静态代码）的静态入口。
/// Windows 主程序在程序集加载时设置为 Win32 实现（行为与原来直接调用 User32 / Gdi32 相同）；
/// Linux 宿主启动时调用 <see cref="Use"/>。未设置时为无界面实现：没有按键按下、取不到光标、屏幕为 1920x1080
/// </summary>
public static class PlatformHost
{
    private static IKeyStateProvider _keyState = NullKeyStateProvider.Instance;
    private static IScreenInfo _screen = new FixedScreenInfo(new PixelSize(1920, 1080));

    /// <summary>系统（物理）键鼠状态与光标位置</summary>
    public static IKeyStateProvider KeyState
    {
        get => _keyState;
        set => _keyState = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>屏幕信息</summary>
    public static IScreenInfo Screen
    {
        get => _screen;
        set => _screen = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>用一个平台服务集合设置全部入口</summary>
    public static void Use(IPlatformServices platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        KeyState = platform.KeyState;
        Screen = platform.Screen;
    }
}

/// <summary>固定尺寸的屏幕（无界面 / 测试）</summary>
public sealed class FixedScreenInfo(PixelSize size) : IScreenInfo
{
    public PixelSize PrimaryScreenSize { get; set; } = size;
}

/// <summary>没有任何按键按下、取不到光标的键状态（无界面 / 测试）</summary>
public sealed class NullKeyStateProvider : IKeyStateProvider
{
    public static NullKeyStateProvider Instance { get; } = new();

    public bool IsKeyDown(GameKey key) => false;

    public IReadOnlySet<GameKey> GetPressedKeys() => new HashSet<GameKey>();

    public PixelPoint? GetCursorPosition() => null;
}
