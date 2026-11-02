using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.KeyMacro;

/// <summary>
/// KeyMacroRunner 的 Linux 部分：日志取自宿主容器（解析选项见 <see cref="Service.KeyMacro.KeyMacroHotkeyHost.CreateParseOptions"/>）
/// </summary>
internal sealed partial class KeyMacroRunner
{
    private static partial ILogger CreateLogger() => CoreServices.GetLogger<KeyMacroRunner>();
}
