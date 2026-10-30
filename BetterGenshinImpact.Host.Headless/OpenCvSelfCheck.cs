using System.Diagnostics;
using BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using Serilog;

namespace BetterGenshinImpact.Host.Headless;

/// <summary>
/// 检查当前加载的 OpenCvSharpExtern 是否具备 BGI 用到的模块：
/// 大地图特征匹配（SIFT / SURF 特征 + Flann 匹配 + FindHomography）、OCR 预处理（dnn）、连通域、相位相关。
/// 用合成纹理图做“已知位置”的匹配，结果偏差超过 2 像素视为失败
/// </summary>
internal static class OpenCvSelfCheck
{
    public static bool Run(ILogger log)
    {
        log.Information("OpenCV 版本：{Version}", Cv2.GetVersionString());
        var ok = true;

        using var train = MakeTexture(1200, 900);
        var queryRect = new Rect(420, 310, 360, 260);
        using var query = new Mat(train, queryRect).Clone();
        var expected = new Point2f(queryRect.X + queryRect.Width / 2f, queryRect.Y + queryRect.Height / 2f);

        foreach (var type in new[] { Feature2DType.SIFT, Feature2DType.SURF })
        {
            ok &= Check($"{type} 特征匹配", () =>
            {
                var feature = Feature2DFactory.Get(type);
                using var trainDescriptors = new Mat();
                feature.DetectAndCompute(train, null, out var trainKeyPoints, trainDescriptors);
                var center = feature.KnnMatch(trainKeyPoints, trainDescriptors, query);
                var error = Math.Sqrt(Math.Pow(center.X - expected.X, 2) + Math.Pow(center.Y - expected.Y, 2));
                return (error <= 2, $"关键点 {trainKeyPoints.Length}，匹配中心 ({center.X:0.0},{center.Y:0.0})，期望 ({expected.X:0.0},{expected.Y:0.0})，偏差 {error:0.00}");
            }, log);
        }

        ok &= Check("dnn BlobFromImage", () =>
        {
            using var color = new Mat();
            Cv2.CvtColor(query, color, ColorConversionCodes.GRAY2BGR);
            using var blob = CvDnn.BlobFromImage(color, 1 / 255.0, new Size(64, 32), default, true, false);
            return (blob.Dims == 4 && blob.Size(1) == 3 && blob.Size(2) == 32 && blob.Size(3) == 64, $"blob 维度 {blob.Dims}，形状 [{blob.Size(0)},{blob.Size(1)},{blob.Size(2)},{blob.Size(3)}]");
        }, log);

        ok &= Check("连通域 ConnectedComponentsWithStats", () =>
        {
            using var bin = new Mat(100, 100, MatType.CV_8UC1, Scalar.All(0));
            Cv2.Rectangle(bin, new Rect(10, 10, 20, 20), Scalar.All(255), -1);
            Cv2.Rectangle(bin, new Rect(60, 60, 30, 10), Scalar.All(255), -1);
            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            var n = Cv2.ConnectedComponentsWithStats(bin, labels, stats, centroids);
            return (n == 3, $"连通域 {n - 1} 个");
        }, log);

        ok &= Check("相位相关 PhaseCorrelate", () =>
        {
            using var a = new Mat();
            using var b = new Mat();
            train[new Rect(100, 100, 256, 256)].ConvertTo(a, MatType.CV_32FC1);
            train[new Rect(105, 103, 256, 256)].ConvertTo(b, MatType.CV_32FC1);
            using var window = new Mat();
            var shift = Cv2.PhaseCorrelate(a, b, window, out _);
            return (Math.Abs(Math.Abs(shift.X) - 5) < 0.5 && Math.Abs(Math.Abs(shift.Y) - 3) < 0.5, $"位移 ({shift.X:0.00},{shift.Y:0.00})，期望 (±5,±3)");
        }, log);

        log.Information("OpenCV 自检{Result}", ok ? "通过" : "失败");
        return ok;
    }

    private static bool Check(string name, Func<(bool Ok, string Detail)> action, ILogger log)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var (ok, detail) = action();
            if (ok) log.Information("[通过] {Name}：{Detail}（{Ms} ms）", name, detail, sw.ElapsedMilliseconds);
            else log.Error("[失败] {Name}：{Detail}", name, detail);
            return ok;
        }
        catch (Exception e)
        {
            log.Error("[失败] {Name}：{Type}: {Message}", name, e.GetType().Name, e.Message);
            return false;
        }
    }

    /// <summary>固定种子的随机纹理（模糊后的噪声 + 几何图形），灰度图</summary>
    private static Mat MakeTexture(int width, int height)
    {
        var mat = new Mat(height, width, MatType.CV_8UC1);
        var rng = new Random(12345);
        var bytes = new byte[width * height];
        rng.NextBytes(bytes);
        mat.SetArray(bytes);
        Cv2.GaussianBlur(mat, mat, new Size(7, 7), 2);
        for (var i = 0; i < 120; i++)
        {
            var c = new Point(rng.Next(width), rng.Next(height));
            Cv2.Circle(mat, c, rng.Next(5, 40), Scalar.All(rng.Next(256)), rng.Next(1, 4));
            Cv2.Rectangle(mat, new Rect(rng.Next(width), rng.Next(height), rng.Next(10, 80), rng.Next(10, 80)), Scalar.All(rng.Next(256)), 2);
        }

        return mat;
    }
}
