using BetterGenshinImpact.GameTask.KeyMacro.Script;
using BetterGenshinImpact.Model;
using Microsoft.Extensions.Logging;
using System;
using System.Windows.Input;

namespace BetterGenshinImpact.GameTask.KeyMacro;

/// <summary>
/// KeyMacroRunner 的 WPF 部分：快捷键模型为 WPF 的 <see cref="HotKey"/>
/// </summary>
internal sealed partial class KeyMacroRunner
{
    /// <summary>
    /// 创建 Runner
    /// </summary>
    /// <param name="definition">宏定义</param>
    /// <param name="hotKey">已注册的快捷键</param>
    /// <param name="isGameForeground">判断游戏是否在前台</param>
    /// <param name="getGameArea">取游戏画面在屏幕上的区域，取不到时返回 null</param>
    public KeyMacroRunner(KeyMacroDefinition definition, HotKey hotKey, Func<bool> isGameForeground, Func<PixelRect?> getGameArea)
        : this(definition, CreateParseOptions(definition.TriggerMode, hotKey), isGameForeground, getGameArea)
    {
    }

    private static partial ILogger CreateLogger() => App.GetLogger<KeyMacroRunner>();

    /// <summary>
    /// 按快捷键生成解析选项：单键快捷键不能出现在脚本里。
    /// 组合键（只有全局热键可用）单独按主键不会触发，不做这项检查
    /// </summary>
    public static KeyMacroParseOptions CreateParseOptions(KeyMacroTriggerMode mode, HotKey hotKey)
    {
        if (hotKey.MouseButton is MouseButton.XButton1 or MouseButton.XButton2)
        {
            return new KeyMacroParseOptions(mode, null,
                hotKey.MouseButton == MouseButton.XButton1 ? KeyMacroMouseButton.X1 : KeyMacroMouseButton.X2);
        }

        if (hotKey.Key == Key.None || hotKey.Modifiers != ModifierKeys.None)
        {
            return new KeyMacroParseOptions(mode);
        }

        return new KeyMacroParseOptions(mode, (GameKey)KeyInterop.VirtualKeyFromKey(hotKey.Key));
    }
}
