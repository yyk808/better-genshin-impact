using BetterGenshinImpact.Core.Input;
using System;

namespace BetterGenshinImpact.GameTask.Music.Service;

/// <summary>
/// 引导式跟弹用的玩家键盘监听：只旁观、不吞键，忽略注入事件与自动重复。
/// Windows 实现为主程序中的 MusicPlayerInputHook（WH_KEYBOARD_LL）。
/// </summary>
public interface IMusicPlayerKeyHook : IDisposable
{
    /// <summary>玩家按下了一个键（已经过滤掉自动重复与注入事件）</summary>
    event Action<WinFormsKeys>? KeyPressed;

    /// <summary>玩家松开了一个键</summary>
    event Action<WinFormsKeys>? KeyReleased;

    /// <summary>钩子是否已经安装成功</summary>
    bool IsRunning { get; }

    /// <summary>安装钩子；失败返回 false。重复调用是安全的。</summary>
    bool Start();
}

/// <summary>引导式跟弹键盘监听的宿主入口，由宿主程序在启动时设置</summary>
public static class MusicPlayerKeyHook
{
    /// <summary>创建新的监听实例；默认是无法启动的空实现（跟弹会提示无法安装键盘钩子）</summary>
    public static Func<IMusicPlayerKeyHook> Factory { get; set; } = static () => new NullMusicPlayerKeyHook();

    /// <summary>当前按住的修饰键（Control / Alt / Shift），默认视为没有按下</summary>
    public static Func<WinFormsKeys> ModifierKeys { get; set; } = static () => WinFormsKeys.None;

    private sealed class NullMusicPlayerKeyHook : IMusicPlayerKeyHook
    {
        public event Action<WinFormsKeys>? KeyPressed { add { } remove { } }

        public event Action<WinFormsKeys>? KeyReleased { add { } remove { } }

        public bool IsRunning => false;

        public bool Start() => false;

        public void Dispose()
        {
        }
    }
}
