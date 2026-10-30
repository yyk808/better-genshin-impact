using OpenCvSharp;

namespace BetterGenshinImpact.Service;

/// <summary>画中画窗口（剧情自动播放时的小窗预览）的宿主能力</summary>
public interface IPictureInPictureHost
{
    bool IsManuallyClosed { get; }

    void Update(Mat frame);

    void Hide(bool resetManual = false);
}

/// <summary>画中画的宿主入口；WPF 主程序启动时设为转发到 PictureInPictureService 的实现，未设置时不显示</summary>
public static class PictureInPictureHost
{
    public static IPictureInPictureHost Current { get; set; } = new NullPictureInPictureHost();

    private sealed class NullPictureInPictureHost : IPictureInPictureHost
    {
        public bool IsManuallyClosed => false;

        public void Update(Mat frame)
        {
        }

        public void Hide(bool resetManual = false)
        {
        }
    }
}
