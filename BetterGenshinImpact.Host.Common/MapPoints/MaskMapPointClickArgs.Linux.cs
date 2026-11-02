namespace BetterGenshinImpact.Model.MaskMap;

/// <summary>点击点位（同 WPF MaskMapPointClickArgs；锚点为遮罩坐标，Linux 用与界面框架无关的点）</summary>
public sealed class MaskMapPointClickArgs
{
    public MaskMapPointClickArgs(MaskMapPoint point, System.Drawing.PointF anchorPosition)
    {
        Point = point;
        AnchorPosition = anchorPosition;
    }

    public MaskMapPoint Point { get; }

    public System.Drawing.PointF AnchorPosition { get; }
}
