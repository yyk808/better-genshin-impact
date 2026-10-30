using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.GameTask.Music.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.Music.Service;

public abstract class KeyInputTransportBase : IKeyInputTransport
{
    private readonly object _syncRoot = new();
    private readonly HashSet<char> _pressedKeys = [];

    public abstract MusicInputMode Mode { get; }

    public void KeyDown(char key)
    {
        key = char.ToUpperInvariant(key);
        lock (_syncRoot)
        {
            if (!_pressedKeys.Add(key))
            {
                return;
            }

            SendKeyDown(ToVirtualKey(key));
        }
    }

    public void KeyUp(char key)
    {
        key = char.ToUpperInvariant(key);
        lock (_syncRoot)
        {
            if (!_pressedKeys.Remove(key))
            {
                return;
            }

            SendKeyUp(ToVirtualKey(key));
        }
    }

    public void ReleaseAll()
    {
        lock (_syncRoot)
        {
            foreach (var key in _pressedKeys.ToArray())
            {
                SendKeyUp(ToVirtualKey(key));
            }

            _pressedKeys.Clear();
        }
    }

    protected abstract void SendKeyDown(GameKey key);

    protected abstract void SendKeyUp(GameKey key);

    private static GameKey ToVirtualKey(char key)
    {
        return key switch
        {
            'A' => GameKey.VK_A,
            'B' => GameKey.VK_B,
            'C' => GameKey.VK_C,
            'D' => GameKey.VK_D,
            'E' => GameKey.VK_E,
            'F' => GameKey.VK_F,
            'G' => GameKey.VK_G,
            'H' => GameKey.VK_H,
            'I' => GameKey.VK_I,
            'J' => GameKey.VK_J,
            'K' => GameKey.VK_K,
            'L' => GameKey.VK_L,
            'M' => GameKey.VK_M,
            'N' => GameKey.VK_N,
            'O' => GameKey.VK_O,
            'P' => GameKey.VK_P,
            'Q' => GameKey.VK_Q,
            'R' => GameKey.VK_R,
            'S' => GameKey.VK_S,
            'T' => GameKey.VK_T,
            'U' => GameKey.VK_U,
            'V' => GameKey.VK_V,
            'W' => GameKey.VK_W,
            'X' => GameKey.VK_X,
            'Y' => GameKey.VK_Y,
            'Z' => GameKey.VK_Z,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "不支持的原琴按键")
        };
    }
}

/// <summary>
/// 后台模式：走 InputHub.Background，每次调用时取通道
/// </summary>
public sealed class PostMessageKeyInputTransport : KeyInputTransportBase
{
    public override MusicInputMode Mode => MusicInputMode.BackgroundPostMessage;

    protected override void SendKeyDown(GameKey key)
    {
        InputHub.Background.Keyboard.KeyDown(key);
    }

    protected override void SendKeyUp(GameKey key)
    {
        InputHub.Background.Keyboard.KeyUp(key);
    }
}

/// <summary>
/// 前台模式：走 InputHub.Foreground，每次调用时取通道
/// </summary>
public sealed class SendInputKeyInputTransport : KeyInputTransportBase
{
    public override MusicInputMode Mode => MusicInputMode.ForegroundSendInput;

    protected override void SendKeyDown(GameKey key)
    {
        InputHub.Foreground.Keyboard.KeyDown(key);
    }

    protected override void SendKeyUp(GameKey key)
    {
        InputHub.Foreground.Keyboard.KeyUp(key);
    }
}
