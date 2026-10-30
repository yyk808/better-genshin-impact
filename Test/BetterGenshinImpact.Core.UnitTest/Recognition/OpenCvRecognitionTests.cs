using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Recognition.OpenCv.TemplateMatch;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.UnitTest.Recognition;

/// <summary>迁入 Core 的 OpenCV 识别代码在 Linux / Windows 上原生运行（需要 OpenCvSharp 原生库）</summary>
public class OpenCvRecognitionTests
{
    private static Mat Noise(int w, int h, int seed)
    {
        var mat = new Mat(h, w, MatType.CV_8UC3);
        var rng = new Random(seed);
        var idx = mat.GetGenericIndexer<Vec3b>();
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            idx[y, x] = new Vec3b((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256));
        return mat;
    }

    [Theory]
    [InlineData(TemplateMatchModes.CCoeffNormed)]
    [InlineData(TemplateMatchModes.SqDiffNormed)]
    [InlineData(TemplateMatchModes.CCorrNormed)]
    public void MatchTemplate_FindsPlantedTemplate(TemplateMatchModes mode)
    {
        using var image = Noise(160, 120, 1);
        using var template = new Mat(image, new Rect(57, 33, 24, 18)).Clone();

        var (loc, val) = TemplateMatchHelper.MatchTemplate(image, template, mode);
        Assert.Equal(new Point(57, 33), loc);
        if (mode == TemplateMatchModes.SqDiffNormed) Assert.True(val < 1e-6, $"{val}");
        else Assert.True(val > 0.999, $"{val}");

        var (_, confidence) = TemplateMatchHelper.MatchTemplateNormalized(image, template, mode);
        Assert.True(confidence > 0.99, $"{confidence}");

        var (sub, _) = TemplateMatchHelper.MatchTemplateSubPix(image, template, mode, null);
        Assert.InRange(sub.X, 56.5f, 57.5f);
        Assert.InRange(sub.Y, 32.5f, 33.5f);
    }

    [Fact]
    public void CropHelper_CutsCorners()
    {
        using var src = Noise(100, 80, 2);
        using var rt = CropHelper.CutRightTop(src, 30, 20);
        Assert.Equal(new Size(30, 20), rt.Size());
        Assert.Equal(src.At<Vec3b>(0, 70), rt.At<Vec3b>(0, 0));
        using var lb = CropHelper.CutLeftBottom(src, 10, 5);
        Assert.Equal(src.At<Vec3b>(75, 0), lb.At<Vec3b>(0, 0));
    }

    [Fact]
    public void ImageDifferenceDetector_FindsOddOneOut()
    {
        Mat Gray(int seed)
        {
            using var c = Noise(40, 40, seed);
            var g = new Mat();
            Cv2.CvtColor(c, g, ColorConversionCodes.BGR2GRAY);
            return g;
        }

        using var a = Gray(3);
        using var b = a.Clone();
        using var c = a.Clone();
        using var odd = Gray(4);
        Assert.Equal(2, ImageDifferenceDetector.FindMostDifferentImage([a, b, odd, c]));
    }
}
