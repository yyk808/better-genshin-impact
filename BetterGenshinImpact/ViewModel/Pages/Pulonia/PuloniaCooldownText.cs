using System;
using System.Collections.Generic;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 路线 CD 在界面上的统一文字（实现在 Core PuloniaCooldownDisplay，与 Avalonia 共用）：状态名称、时间格式与作用域名称；任务树标记、CD 总览、运行预览和设置表单共用。
/// </summary>
internal static class PuloniaCooldownText
{
    /// <summary>状态的中文名称。</summary>
    public static string State(PuloniaCollectionCooldownState state) => PuloniaCooldownDisplay.State(state);

    /// <summary>按本机时区显示的绝对时间；为空时显示“无”。</summary>
    public static string Time(DateTimeOffset? value) => PuloniaCooldownDisplay.Time(value);

    /// <summary>距下次可执行的简短说明。</summary>
    public static string Remaining(DateTimeOffset next, DateTimeOffset now) => PuloniaCooldownDisplay.Remaining(next, now);

    /// <summary>下次可执行的完整说明。</summary>
    public static string Next(DateTimeOffset next, DateTimeOffset now) => PuloniaCooldownDisplay.Next(next, now);

    /// <summary>作用域的显示名。</summary>
    public static string Scope(string scopeKey, IReadOnlyDictionary<string, string> accountNamesByUid)
        => PuloniaCooldownDisplay.Scope(scopeKey, accountNamesByUid);
}
