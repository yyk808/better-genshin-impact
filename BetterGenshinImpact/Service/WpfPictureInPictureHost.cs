using OpenCvSharp;

namespace BetterGenshinImpact.Service;

/// <summary><see cref="IPictureInPictureHost"/> 的 WPF 实现，原样转发到 <see cref="PictureInPictureService"/></summary>
public sealed class WpfPictureInPictureHost : IPictureInPictureHost
{
    public static readonly WpfPictureInPictureHost Instance = new();

    public bool IsManuallyClosed => PictureInPictureService.IsManuallyClosed;

    public void Update(Mat frame) => PictureInPictureService.Update(frame);

    public void Hide(bool resetManual = false) => PictureInPictureService.Hide(resetManual);
}
