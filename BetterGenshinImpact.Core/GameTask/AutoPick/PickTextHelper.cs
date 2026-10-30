using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoPick;

/// <summary>拾取文字的图像处理（原 AutoPickTrigger.GetWhiteTextBoundingRect）</summary>
public static class PickTextHelper
{
    /// <summary>取纯白色文字的外接矩形</summary>
    public static Rect GetWhiteTextBoundingRect(Mat textMat)
    {
        // 预处理提取纯白色文字
        var processedMat = new Mat();
        // 提取白色文字 (255,255,255)
        Cv2.InRange(textMat, new Scalar(254, 254, 254), new Scalar(255, 255, 255), processedMat);
        // 形态学操作，先腐蚀后膨胀，去除噪点并保持文字完整
        var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2, 2));
        Cv2.MorphologyEx(processedMat, processedMat, MorphTypes.Open, kernel, iterations: 1);
        Cv2.Dilate(processedMat, processedMat, kernel, iterations: 1);
        // 寻找非零区域，即文字区域
        Rect boundingRect = Cv2.BoundingRect(processedMat);
        return boundingRect;
    }
}
