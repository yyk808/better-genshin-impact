using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace BetterGenshinImpact.Core.Input;

internal static class InputModuleInitializer
{
    /// <summary>
    /// InputChannelBase 已移入 BetterGenshinImpact.Core，不能直接访问 App；
    /// 主程序集加载时把日志来源接回 App.GetLogger，与迁移前的懒加载行为一致（首次记录日志时才解析）
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        InputChannelBase.LoggerFactory ??= static () => App.GetLogger<InputChannelBase>();
    }
}
