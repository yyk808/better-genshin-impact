using OpenCvSharp;
using OpenCvSharp.Features2D;
using OpenCvSharp.XFeatures2D;
using Serilog;

namespace BetterGenshinImpact.Host.Common;

/// <summary>
/// 当前加载的 libOpenCvSharpExtern.so 是否为完整构建。
/// Sdcb 精简构建没有 features2d / xfeatures2d（SIFT、SURF），大地图特征匹配（地图追踪、传送、一条龙中依赖大地图的任务）不可用
/// </summary>
public static class OpenCvCapabilities
{
    public const string BuildHint =
        "完整构建：在仓库根目录运行 Build/Linux/build-opencvsharp.sh（产物 opencv-build/out/libOpenCvSharpExtern.so），" +
        "然后重新构建宿主；或用构建属性 / 环境变量 OpenCvSharpExternPath 指向已有的完整构建";

    private static readonly Lazy<(bool Ok, string Detail)> FeatureMatchingLazy = new(ProbeFeatureMatching);

    /// <summary>SIFT 与 SURF 均可创建</summary>
    public static bool FeatureMatchingAvailable => FeatureMatchingLazy.Value.Ok;

    /// <summary>探测结果说明（可用时为“SIFT / SURF 可用”，否则为缺失的入口或异常信息）</summary>
    public static string FeatureMatchingDetail => FeatureMatchingLazy.Value.Detail;

    private static (bool, string) ProbeFeatureMatching()
    {
        try
        {
            using (SIFT.Create())
            using (SURF.Create(100))
            {
            }

            return (true, "SIFT / SURF 可用");
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException or TypeInitializationException or OpenCVException)
        {
            return (false, $"{e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>启动时记录：完整构建记一条信息；精简构建给出警告与构建方法</summary>
    public static void LogStartupStatus(ILogger log)
    {
        if (FeatureMatchingAvailable)
        {
            log.Information("OpenCV 特征匹配：{Detail}", FeatureMatchingDetail);
            return;
        }

        log.Warning("OpenCV 特征匹配不可用（{Detail}）：当前 libOpenCvSharpExtern.so 为精简构建，地图追踪、传送及一条龙中依赖大地图的任务无法运行。{Hint}",
            FeatureMatchingDetail, BuildHint);
    }
}
