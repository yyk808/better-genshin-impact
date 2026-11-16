using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;

namespace BetterGenshinImpact.Core.Recognition;

public static class RecognitionAssets
{
    private static readonly ConcurrentDictionary<RecognitionAssetCacheKey, RecognitionObject> Cache = new();

    public static RecognitionObject Get(string taskName, string objectName, Region region)
    {
        ArgumentNullException.ThrowIfNull(region);
        return Get(taskName, objectName, region.Width, region.Height);
    }

    /// <summary>
    /// 按运行环境的画面尺寸（归一到 1080P 后的整帧大小）获取识别对象
    /// </summary>
    public static RecognitionObject Get(string taskName, string objectName, ISystemInfo systemInfo)
    {
        ArgumentNullException.ThrowIfNull(systemInfo);
        var captureRect = systemInfo.ScaleMax1080PCaptureRect;
        return Get(taskName, objectName, captureRect.Width, captureRect.Height);
    }

    public static RecognitionObject Get(string taskName, string objectName, int captureWidth, int captureHeight)
    {
        var key = new RecognitionAssetCacheKey(taskName, objectName, captureWidth, captureHeight);
        var recognitionObject = Cache.GetOrAdd(key, static cacheKey => Load(cacheKey));
        return recognitionObject;
    }

    public static void Clear(string taskName, string objectName)
    {
        foreach (var key in Cache.Keys)
        {
            if (string.Equals(key.TaskName, taskName, StringComparison.Ordinal)
                && string.Equals(key.ObjectName, objectName, StringComparison.Ordinal))
            {
                Cache.TryRemove(key, out _);
            }
        }
    }

    public static void ClearTask(string taskName)
    {
        foreach (var key in Cache.Keys)
        {
            if (string.Equals(key.TaskName, taskName, StringComparison.Ordinal))
            {
                Cache.TryRemove(key, out _);
            }
        }
    }

    public static void ClearAll()
    {
        Cache.Clear();
    }

    private static RecognitionObject Load(RecognitionAssetCacheKey key)
    {
        return RecognitionObjectJsonLoader.LoadFromFile(
            Global.Absolute($@"GameTask\{key.TaskName}\Assets\Recognition.json"),
            key.ObjectName,
            new RecognitionObjectJsonLoadContext
            {
                CaptureHeight = key.CaptureHeight,
                CaptureWidth = key.CaptureWidth,
                TemplateLoader = (template, mode) => LoadTemplateImage(key.TaskName, template, key.CaptureWidth, key.CaptureHeight, mode),
            });
    }

    private static Mat LoadTemplateImage(string taskName, string assetName, int captureWidth, int captureHeight, ImreadModes flags)
    {
        var assetsFolder = Global.Absolute($@"GameTask\{taskName}\Assets\{captureWidth}x{captureHeight}");
        if (!Directory.Exists(assetsFolder))
        {
            assetsFolder = Global.Absolute($@"GameTask\{taskName}\Assets\1920x1080");
        }

        if (!Directory.Exists(assetsFolder))
        {
            throw new FileNotFoundException($"未找到{taskName}的素材文件夹");
        }

        // Recognition.json 中的模板路径可能含 Windows 分隔符（如 other\确定.png）。
        // 在 Linux 上必须先规范化分隔符再拼路径；大小写仅在精确路径不存在时，
        // 对当前目录下该路径段做一次非递归匹配：0 个返回空，1 个采用，多个拒绝猜测。
        // Windows 上 File.Exists 本就大小写不敏感，不会走到回退，行为保持不变。
        // 解析结果随 RecognitionObject 缓存，不会在每帧识别时重复遍历目录。
        var filePath = ResolveTemplateFilePath(assetsFolder, assetName);
        if (filePath is null)
        {
            throw new FileNotFoundException($"未找到{taskName}中的{assetName}文件");
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


    /// <summary>
    /// 将 JSON 中的模板相对路径解析为实际文件。
    /// 规范化 / 与 \；精确路径优先；精确失败后仅在当前层级按大小写不敏感匹配，不递归搜索。
    /// 同一层级出现多个仅大小写不同的条目时抛出，以免缓存到错误文件。真实缺失返回 null。
    /// </summary>
    internal static string? ResolveTemplateFilePath(string assetsFolder, string assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName))
        {
            return null;
        }

        var normalized = assetName.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var exact = Path.Combine(assetsFolder, normalized);
        if (File.Exists(exact))
        {
            return exact;
        }

        var current = assetsFolder;
        foreach (var part in normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var exactPart = Path.Combine(current, part);
            if (Directory.Exists(exactPart) || File.Exists(exactPart))
            {
                current = exactPart;
                continue;
            }

            if (!Directory.Exists(current))
            {
                return null;
            }

            var matches = Directory.EnumerateFileSystemEntries(current)
                .Where(entry => string.Equals(Path.GetFileName(entry), part, StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => Path.GetFileName(entry), StringComparer.Ordinal)
                .ToList();
            if (matches.Count == 0)
            {
                return null;
            }

            if (matches.Count > 1)
            {
                var names = string.Join(", ", matches.Select(Path.GetFileName));
                throw new InvalidOperationException(
                    $"模板路径 '{assetName}' 在 '{current}' 下对 '{part}' 存在 {matches.Count} 个仅大小写不同的条目，拒绝猜测: {names}");
            }

            current = matches[0];
        }

        return File.Exists(current) ? current : null;
    }

    private readonly record struct RecognitionAssetCacheKey(string TaskName, string ObjectName, int CaptureWidth, int CaptureHeight);
}
