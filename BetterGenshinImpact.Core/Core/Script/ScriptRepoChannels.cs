using System.Collections.Generic;

namespace BetterGenshinImpact.Core.Script;

/// <summary>脚本仓库渠道（原 ScriptRepoUpdater.RepoChannels，ScriptRepoUpdater 仍以同名属性公开同一实例）</summary>
public static class ScriptRepoChannels
{
    public static readonly Dictionary<string, string> All = new()
    {
        { "CNB", "https://cnb.cool/bettergi/bettergi-scripts-list" },
        // { "GitCode", "https://gitcode.com/huiyadanli/bettergi-scripts-list" },  // 由于 10MB限制，下掉
        { "GitHub", "https://github.com/babalae/bettergi-scripts-list" },
    };
}
