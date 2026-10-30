using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.Model;
using OpenCvSharp;
using System;
using System.IO;

namespace BetterGenshinImpact.GameTask;

/// <summary>任务素材图片的加载（原 GameTaskManager.LoadAssetImage）</summary>
public static class GameTaskAssets
{
    /// <summary>
    /// 获取素材图片并缩放
    /// </summary>
    /// <returns></returns>
    /// <exception cref="FileNotFoundException"></exception>
    public static Mat LoadAssetImage(string featName, string assertName, ISystemInfo systemInfo, ImreadModes flags = ImreadModes.Color)
    {
        var assetsFolder = Global.Absolute($@"GameTask\{featName}\Assets\{systemInfo.GameScreenSize.Width}x{systemInfo.GameScreenSize.Height}");
        if (!Directory.Exists(assetsFolder))
        {
            assetsFolder = Global.Absolute($@"GameTask\{featName}\Assets\1920x1080");
        }

        if (!Directory.Exists(assetsFolder))
        {
            throw new FileNotFoundException($"未找到{featName}的素材文件夹");
        }

        var filePath = Path.Combine(assetsFolder, assertName);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"未找到{featName}中的{assertName}文件");
        }

        var mat = Mat.FromStream(File.OpenRead(filePath), flags);
        if (systemInfo.GameScreenSize.Width != 1920)
        {
            mat = ResizeHelper.Resize(mat, systemInfo.AssetScale);
        }

        return mat;
    }

    /// <summary>
    /// 根据捕获区域宽高加载素材图片并缩放
    /// </summary>
    /// <returns></returns>
    /// <exception cref="FileNotFoundException"></exception>
    public static Mat LoadAssetImage(string featName, string assertName, int captureWidth, int captureHeight, ImreadModes flags = ImreadModes.Color)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(captureWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(captureHeight);

        var assetsFolder = Global.Absolute($@"GameTask\{featName}\Assets\{captureWidth}x{captureHeight}");
        if (!Directory.Exists(assetsFolder))
        {
            assetsFolder = Global.Absolute($@"GameTask\{featName}\Assets\1920x1080");
        }

        if (!Directory.Exists(assetsFolder))
        {
            throw new FileNotFoundException($"未找到{featName}的素材文件夹");
        }

        var filePath = Path.Combine(assetsFolder, assertName);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"未找到{featName}中的{assertName}文件");
        }

        using var stream = File.OpenRead(filePath);
        var mat = Mat.FromStream(stream, flags);
        if (captureWidth < 1920)
        {
            using (mat)
            {
                return ResizeHelper.Resize(mat, captureWidth / 1920d);
            }
        }

        return mat;
    }
}
