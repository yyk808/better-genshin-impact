using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.Runtime;

/// <summary>
/// 运行环境宿主中任务代码会用到的部分。Windows 实现为 GameRuntimeService
/// </summary>
public interface IGameRuntimeHost
{
    /// <summary>当前运行环境。截图器未启动时为 null</summary>
    GameRuntime? Current { get; }

    /// <summary>获取运行环境并启动截图器，返回绑定的运行环境；不能启动时返回 null。已在运行时直接返回当前运行环境</summary>
    Task<GameRuntime?> StartAsync(CancellationToken ct = default);
}
