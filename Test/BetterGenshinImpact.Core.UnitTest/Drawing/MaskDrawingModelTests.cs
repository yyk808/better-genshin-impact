using System.Drawing;
using BetterGenshinImpact.Core.Drawing;
using BetterGenshinImpact.Core.Mask;

namespace BetterGenshinImpact.Core.UnitTest.Drawing;

public class MaskDrawingModelTests
{
    [Fact]
    public void MaskRect_RejectsNegativeSizeLikeWpfRect()
    {
        var r = new MaskRect(1, 2, 3, 4);
        Assert.Equal(4, r.Right);
        Assert.Equal(6, r.Bottom);
        Assert.Throws<ArgumentException>(() => new MaskRect(0, 0, -1, 1));
        Assert.Throws<ArgumentException>(() => new MaskRect(0, 0, 1, -1));
        Assert.Equal(new MaskRect(1, 2, 3, 4), r);
    }

    [Fact]
    public void Stroke_FromPen_KeepsColorAndWidth()
    {
        Assert.Null(MaskWindowDrawingStroke.FromPen(null));
        var s = MaskWindowDrawingStroke.FromPen(new DrawPen(Color.FromArgb(128, 10, 20, 30), 2.5f))!.Value;
        Assert.Equal(MaskColor.FromArgb(128, 10, 20, 30), s.Color);
        Assert.Equal(2.5, s.Thickness);
        Assert.Equal(MaskColor.FromRgb(255, 0, 0), MaskWindowDrawingStyle.DefaultStroke.Color);
        Assert.Equal(MaskColor.Black, MaskWindowDrawingStyle.Default.TextColor);
    }

    [Fact]
    public void DrawingBoard_SetsAndClearsGroups()
    {
        var board = new MaskWindowDrawingBoard();
        var changed = 0;
        board.Changed += () => changed++;
        var rect = new OpenCvSharp.Rect(1, 2, 3, 4).ToMaskWindowDrawingRect(DrawPens.Lime);
        board.Set("g", [rect, new MaskWindowDrawingText("t", new MaskPoint(5, 6))]);
        Assert.Single(board.Current.Entries);
        Assert.Equal(new MaskRect(1, 2, 3, 4), ((MaskWindowDrawingRect)board.Current.Entries[0].Shapes[0]).Bounds);
        // 内容相同不产生新版本
        var version = board.Current.Version;
        board.Set("g", [rect, new MaskWindowDrawingText("t", new MaskPoint(5, 6))]);
        Assert.Equal(version, board.Current.Version);
        board.Clear("g");
        Assert.True(board.Current.IsEmpty);
        Assert.True(changed >= 2);
    }
}
