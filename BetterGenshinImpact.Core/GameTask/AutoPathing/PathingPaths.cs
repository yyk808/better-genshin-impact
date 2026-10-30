using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.GameTask.AutoPathing;

/// <summary>地图追踪相关目录（原 PathingPaths.PathJsonPath）</summary>
public static class PathingPaths
{
    /// <summary>用户地图追踪脚本根目录</summary>
    public static readonly string PathJsonPath = Global.Absolute(@"User\AutoPathing");
}
