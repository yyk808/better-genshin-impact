using System;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using Xunit;

namespace BetterGenshinImpact.Core.UnitTest.CoreTests.RecognitionTests;

/// <summary>
/// Linux 上 Recognition.json 的模板路径必须规范化分隔符，并在大小写不一致时确定性回退。
/// 先复现「不规范化则找不到文件」，再锁定修复后的契约。不访问网络或用户目录。
/// </summary>
[Collection("GlobalPathIsolation")]
public sealed class RecognitionAssetsPathTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bgi-tmpl-path-" + Guid.NewGuid().ToString("N"));
    private readonly string _startup = Global.StartUpPath;

    public RecognitionAssetsPathTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Global.StartUpPath = _startup;
        RecognitionAssets.ClearAll();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// 复现：未规范化时，JSON 中的 other\确定.png 在 Linux 上会被当成单段文件名，File.Exists 失败。
    /// </summary>
    [Fact]
    public void UnnormalizedBackslash_IsALiteralNameOnCaseSensitiveFileSystems()
    {
        var other = Directory.CreateDirectory(Path.Combine(_root, "other")).FullName;
        var real = Path.Combine(other, "确定.png");
        File.WriteAllBytes(real, [1, 2, 3]);

        var literal = Path.Combine(_root, @"other\确定.png");
        if (Path.DirectorySeparatorChar == '/')
        {
            Assert.False(File.Exists(literal));
            Assert.True(File.Exists(real));
        }
        else
        {
            // Windows 会把 \ 当分隔符，本条只验证真实文件存在。
            Assert.True(File.Exists(real));
        }
    }

    /// <summary>
    /// 复现：仅存在 Space.png 时，字面 space.png 在大小写敏感文件系统上不存在。
    /// </summary>
    [Fact]
    public void LiteralLowercase_DoesNotExistWhenFileIsPascalCase_OnCaseSensitiveFileSystems()
    {
        var pascal = Path.Combine(_root, "Space.png");
        File.WriteAllBytes(pascal, [1, 2, 3]);
        var lower = Path.Combine(_root, "space.png");
        if (IsCaseSensitiveDirectory(_root))
        {
            Assert.False(File.Exists(lower));
            Assert.True(File.Exists(pascal));
        }
        else
        {
            Assert.True(File.Exists(lower));
        }
    }

    [Fact]
    public void Resolve_OtherBackslashConfirm_FindsForwardSlashFile()
    {
        var other = Directory.CreateDirectory(Path.Combine(_root, "other")).FullName;
        var real = Path.Combine(other, "确定.png");
        File.WriteAllBytes(real, [1, 2, 3]);

        var resolved = RecognitionAssets.ResolveTemplateFilePath(_root, @"other\确定.png");
        Assert.NotNull(resolved);
        Assert.True(File.Exists(resolved));
        Assert.Equal("确定.png", Path.GetFileName(resolved));
        Assert.Equal("other", Path.GetFileName(Path.GetDirectoryName(resolved)));
    }

    [Fact]
    public void Resolve_SpaceLowercase_FindsUniquePascalCaseFile()
    {
        File.WriteAllBytes(Path.Combine(_root, "Space.png"), [1, 2, 3]);

        var resolved = RecognitionAssets.ResolveTemplateFilePath(_root, "space.png");
        Assert.NotNull(resolved);
        Assert.True(File.Exists(resolved));
        Assert.Equal("Space.png", Path.GetFileName(resolved));
    }

    [Fact]
    public void Resolve_ExactCorrectCase_ReturnsExactPathWithoutGuessing()
    {
        var exact = Path.Combine(_root, "Space.png");
        File.WriteAllBytes(exact, [1, 2, 3]);

        var resolved = RecognitionAssets.ResolveTemplateFilePath(_root, "Space.png");
        Assert.Equal(exact, resolved);
    }

    [Fact]
    public void Resolve_MissingFile_ReturnsNull()
    {
        Directory.CreateDirectory(Path.Combine(_root, "other"));
        Assert.Null(RecognitionAssets.ResolveTemplateFilePath(_root, @"other\不存在.png"));
        Assert.Null(RecognitionAssets.ResolveTemplateFilePath(_root, "no-such.png"));
        Assert.Null(RecognitionAssets.ResolveTemplateFilePath(_root, " "));
        Assert.Null(RecognitionAssets.ResolveTemplateFilePath(_root, ""));
    }

    /// <summary>
    /// 回退不得全盘递归：子目录里的同名文件不能被顶层引用命中。
    /// </summary>
    [Fact]
    public void Resolve_DoesNotRecurseIntoNestedDirectories()
    {
        var nested = Directory.CreateDirectory(Path.Combine(_root, "nested")).FullName;
        File.WriteAllBytes(Path.Combine(nested, "Space.png"), [1, 2, 3]);

        Assert.Null(RecognitionAssets.ResolveTemplateFilePath(_root, "space.png"));
        Assert.Null(RecognitionAssets.ResolveTemplateFilePath(_root, "Space.png"));
    }

    [Fact]
    public void Resolve_ExactName_WinsEvenIfCaseSiblingExists()
    {
        if (!TryCreateCaseAmbiguousPair(_root, "Space.png", "space.png", out var created))
        {
            return;
        }

        Assert.Equal(2, created);
        var resolved = RecognitionAssets.ResolveTemplateFilePath(_root, "space.png");
        Assert.NotNull(resolved);
        Assert.Equal("space.png", Path.GetFileName(resolved));
    }

    [Fact]
    public void Resolve_CaseAmbiguity_ThrowsRatherThanGuessing()
    {
        if (!TryCreateCaseAmbiguousPair(_root, "Space.png", "space.png", out var created))
        {
            return;
        }

        Assert.Equal(2, created);
        // 精确路径 SPACE.png 不存在，但有两个仅大小写不同的条目，必须拒绝猜测。
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RecognitionAssets.ResolveTemplateFilePath(_root, "SPACE.png"));
        Assert.Contains("拒绝猜测", ex.Message);
        Assert.Contains("Space.png", ex.Message);
        Assert.Contains("space.png", ex.Message);
    }

    /// <summary>
    /// 生产入口：用仓库真实 Recognition.json 初始化 SpaceButton / ConfirmButton，并在单帧上 Find。
    /// 这是资产加载 + 识别对象入口，不是裸 MatchTemplate。
    /// </summary>
    [Fact]
    public void ProductionAssets_SpaceButtonAndConfirmButton_InitializeAndFindOnSingleFrame()
    {
        var assetsRoot = FindRepoAssetsRoot();
        if (assetsRoot is null)
        {
            return;
        }

        var previous = Global.StartUpPath;
        try
        {
            Global.StartUpPath = assetsRoot;
            RecognitionAssets.ClearAll();

            using var frame = new Mat(1080, 1920, MatType.CV_8UC3, new Scalar(18, 22, 30));
            using var region = new ImageRegion(frame, 0, 0);

            var space = RecognitionAssets.Get("AutoFishing", "SpaceButton", region);
            Assert.False(space.TemplateImageMat is null || space.TemplateImageMat.Empty());
            using var spaceHit = region.Find(space);
            Assert.False(spaceHit.IsExist());

            var confirm = RecognitionAssets.Get("AutoGeniusInvokation", "ConfirmButton", region);
            Assert.False(confirm.TemplateImageMat is null || confirm.TemplateImageMat.Empty());
            using var confirmHit = region.Find(confirm);
            Assert.False(confirmHit.IsExist());
        }
        finally
        {
            RecognitionAssets.ClearAll();
            Global.StartUpPath = previous;
        }
    }

    private static string? FindRepoAssetsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "BetterGenshinImpact");
            var fishing = Path.Combine(candidate, "GameTask", "AutoFishing", "Assets", "Recognition.json");
            var space = Path.Combine(candidate, "GameTask", "AutoFishing", "Assets", "1920x1080", "Space.png");
            var genius = Path.Combine(candidate, "GameTask", "AutoGeniusInvokation", "Assets", "1920x1080", "other", "确定.png");
            if (File.Exists(fishing) && File.Exists(space) && File.Exists(genius))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static bool IsCaseSensitiveDirectory(string directory)
    {
        var probe = Path.Combine(directory, "CaseProbe.tmp");
        File.WriteAllBytes(probe, [0]);
        try
        {
            return !File.Exists(Path.Combine(directory, "caseprobe.tmp"));
        }
        finally
        {
            File.Delete(probe);
        }
    }

    private static bool TryCreateCaseAmbiguousPair(string directory, string first, string second, out int created)
    {
        created = 0;
        if (!IsCaseSensitiveDirectory(directory))
        {
            return false;
        }

        File.WriteAllBytes(Path.Combine(directory, first), [1]);
        File.WriteAllBytes(Path.Combine(directory, second), [2]);
        var names = Directory.EnumerateFiles(directory)
            .Select(Path.GetFileName)
            .Where(name => name is not null && name.Equals(first, StringComparison.OrdinalIgnoreCase))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        created = names.Count;
        return created >= 2;
    }
}
