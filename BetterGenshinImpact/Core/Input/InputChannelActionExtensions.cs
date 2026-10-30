using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Simulator.Extensions;
using System.Threading;

namespace BetterGenshinImpact.Core.Input;

/// <summary>
/// 需要自行解释游戏动作的通道（例如手柄后端把动作映射成手柄按键），实现此接口即可接管 <see cref="InputChannelActionExtensions.SimulateAction"/>
/// </summary>
public interface IGameActionInputChannel : IInputChannel
{
    IInputChannel SimulateAction(GIActions action, KeyType type = KeyType.KeyPress);
}

/// <summary>
/// 按用户键位配置模拟游戏动作。
/// <para>
/// 原先是 <c>IInputChannel.SimulateAction</c> / <c>InputChannelBase.SimulateAction</c>：
/// 它依赖 GIActions 与键位配置（业务层），所以在 IInputChannel 移入平台无关的 BetterGenshinImpact.Core 后改为扩展方法，
/// 调用写法 <c>InputHub.Foreground.SimulateAction(GIActions.X)</c> 不变，行为与原实现一致。
/// </para>
/// </summary>
public static class InputChannelActionExtensions
{
    public static IInputChannel SimulateAction(this IInputChannel channel, GIActions action, KeyType type = KeyType.KeyPress)
    {
        if (channel is IGameActionInputChannel custom)
        {
            return custom.SimulateAction(action, type);
        }

        var key = action.ToActionKey();
        if (key is KeyId.None or KeyId.Unknown)
        {
            return channel;
        }

        // KeyId 的鼠标键取值与 VK 一致，鼠标键由 Keyboard 接口转发到鼠标
        var vk = key.ToVK();
        var keyboard = channel.Keyboard;
        switch (type)
        {
            case KeyType.KeyPress:
                keyboard.KeyPress(vk);
                break;
            case KeyType.KeyDown:
                keyboard.KeyDown(vk);
                break;
            case KeyType.KeyUp:
                keyboard.KeyUp(vk);
                break;
            case KeyType.Hold:
                keyboard.KeyDown(vk);
                Thread.Sleep(1000);
                keyboard.KeyUp(vk);
                break;
        }

        return channel;
    }
}
