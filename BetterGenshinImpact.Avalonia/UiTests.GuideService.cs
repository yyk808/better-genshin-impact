using BetterGenshinImpact.Avalonia.Services;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Service.Guide;
using Microsoft.Extensions.DependencyInjection;

namespace BetterGenshinImpact.Avalonia;

/// <summary>
/// 悬浮小窗协调服务（--ui-test guide-service）：Linux 上编译与 Windows 相同的 GuideWindowService，窗口换成记录调用的假窗口，
/// 核对打开（网址、显示、默认位置在主显示器右上角）、跟随游戏窗口与停靠、播放状态、任务模式隐藏与恢复、用户隐藏 / 显示、
/// 剪贴板打开、字幕浮窗的参照区域、遮罩变化后重新置顶、关闭。游戏窗口由截图器（假游戏）提供
/// </summary>
internal sealed partial class UiTests
{
    private sealed class FakeGuidePlayer : IGuidePlayer
    {
        public readonly List<string> Navigated = [];
        public readonly List<GuidePlayerCommand> Commands = [];
        public GuidePlayerState? State { get; private set; }
        public string? CurrentUrl { get; private set; }
        public bool CanGoBack => false;
        public bool IsMuted { get; set; }
        public event EventHandler<GuidePlayerState>? StateChanged;
        public event EventHandler<string>? SubtitleChanged;
        public event EventHandler<GuidePlayerEvent>? EventRaised;

        public Task<GuideCommandResult> ExecuteAsync(GuidePlayerCommand command, double value = 0)
        {
            Commands.Add(command);
            return Task.FromResult(GuideCommandResult.Success);
        }

        public void Navigate(string url)
        {
            CurrentUrl = url;
            Navigated.Add(url);
        }

        public void GoBack() { }
        public void Reload() { }
        public void SetZoom(double factor) { }

        public void Report(GuidePlayerState state)
        {
            State = state;
            StateChanged?.Invoke(this, state);
        }

        public void Subtitle(string text) => SubtitleChanged?.Invoke(this, text);
        public void Event(GuidePlayerEventKind kind) => EventRaised?.Invoke(this, new GuidePlayerEvent(kind));
    }

    private sealed class FakeGuideView : IGuideWindowView
    {
        public FakeGuidePlayer Fake { get; } = new();
        public IGuidePlayer Player => Fake;
        public nint Handle => 0;
        public bool IsShown { get; private set; }
        public System.Drawing.Rectangle Bounds;
        public int TopmostCount;
        public bool Closed_;
        public void ShowWithoutActivation() => IsShown = true;
        public void HideWindow() => IsShown = false;

        public void CloseWindow()
        {
            IsShown = false;
            Closed_ = true;
            Closed?.Invoke(this, EventArgs.Empty);
        }

        public void SetBounds(System.Drawing.Rectangle bounds) => Bounds = bounds;

        public bool TryGetBounds(out System.Drawing.Rectangle bounds)
        {
            bounds = Bounds;
            return bounds.Width > 0;
        }

        public void BringToTopmost() => TopmostCount++;
        public void ActivateForInput() { }
        public event EventHandler? Closed;
    }

    private sealed class FakeSubtitleView : IGuideSubtitleView
    {
        public bool IsShown { get; private set; }
        public System.Drawing.Rectangle Anchor;
        public void ShowWithoutActivation() => IsShown = true;
        public void HideWindow() => IsShown = false;
        public void CloseWindow() => IsShown = false;
        public void UpdateAnchor(System.Drawing.Rectangle anchor) => Anchor = anchor;
        public void BringToTopmost() { }
    }

    private async Task GuideServiceAsync()
    {
        var host = vm.Host;
        Check("宿主可用", host is not null);
        if (host is null) return;
        var config = ConfigHost.Config!.GuideWindowConfig;
        var saved = (config.PositionMode, config.FreeLeft, config.FreeTop, config.WindowWidth, config.WindowHeight, config.FollowAnchor,
            config.FollowOffsetX, config.FollowOffsetY, config.AutoOpenOnGameStart, config.SubtitleWindowEnabled, config.WasOpen,
            config.HideWhenGameInactive);
        var oldWindow = GuideViews.CreateWindow;
        var oldSubtitle = GuideViews.CreateSubtitle;
        var oldWeb = GuideLinuxPlatform.IsWebViewAvailable;
        var views = new List<FakeGuideView>();
        FakeSubtitleView? subtitle = null;
        GuideViews.CreateWindow = () =>
        {
            var v = new FakeGuideView();
            views.Add(v);
            return v;
        };
        GuideViews.CreateSubtitle = () => subtitle = new FakeSubtitleView();
        GuideLinuxPlatform.IsWebViewAvailable = () => true;
        try
        {
            config.AutoOpenOnGameStart = GuideAutoOpenMode.Never;
            config.PositionMode = GuidePositionMode.Free;
            config.FreeLeft = null;
            config.FreeTop = null;
            config.WindowWidth = null;
            config.WindowHeight = null;
            // 前台判断依赖窗口管理器，这里不测自动隐藏
            config.HideWhenGameInactive = false;
            config.SubtitleWindowEnabled = false;
            await Until(() => host.RuntimeService.IsRunning, 30000);
            Check("截图器已启动（假游戏）", host.RuntimeService.IsRunning);
            host.StartGuide();
            var guide = host.Guide;
            Check("服务可用（主实例）", guide.IsAvailable);

            // 1. 打开：网址交给网页、显示、默认位置在主显示器工作区右上角
            const string url = "https://www.bilibili.com/video/BV1GJ411x7h7";
            guide.Open(new GuideOpenRequest(url));
            var view = await WaitFor(() => views.FirstOrDefault());
            await Settle(300);
            Check("打开后创建窗口并显示", view is { IsShown: true } && guide.State.IsOpen && guide.State.IsShown, view?.IsShown);
            if (view is null) return;
            Check("网址交给网页", view.Fake.Navigated.LastOrDefault()?.Contains("BV1GJ411x7h7") == true, string.Join(",", view.Fake.Navigated));
            var area = GuideLinuxPlatform.WorkingAreas()[0];
            var scale = GuideLinuxPlatform.Scale();
            var width = (int)Math.Round(480 * scale);
            var margin = (int)Math.Round(16 * scale);
            Check("默认位置：主显示器工作区右上角", view.Bounds.Width == width && view.Bounds.Right == area.Right - margin && view.Bounds.Top == area.Top + margin,
                $"{view.Bounds} 工作区 {area}");
            Check("打开后记住“上次打开”", config.WasOpen);

            // 2. 跟随游戏：停靠到左上角后按游戏画面偏移
            guide.SetPositionMode(GuidePositionMode.FollowGame);
            guide.Dock(GuideAnchor.TopLeft);
            await Settle(300);
            var game = host.RuntimeService.Current?.Window.Viewport.ScreenRect;
            Check("跟随游戏：左上角停靠在游戏画面内", game is { } g && view.Bounds.Left == g.X + margin && view.Bounds.Top == g.Y + margin,
                $"{view.Bounds} 游戏 {game}");
            Check("跟随游戏：记录停靠角", config.PositionMode == GuidePositionMode.FollowGame && config.FollowAnchor == GuideAnchor.TopLeft);
            Check("停靠后的提示", guide.State.OsdText == "已停靠到左上角", guide.State.OsdText);
            guide.SetPositionMode(GuidePositionMode.Free);
            await Settle(200);
            Check("改回自由放置并记录屏幕位置", config.PositionMode == GuidePositionMode.Free && config.FreeLeft == view.Bounds.Left, config.FreeLeft);

            // 3. 播放状态
            view.Fake.Report(new GuidePlayerState { Href = url, Title = "测试视频", Site = "bilibili", VideoKey = "bilibili:BV1GJ411x7h7", HasVideo = true, Paused = false, Duration = 120, CurrentTime = 3 });
            await Settle(200);
            Check("网页上报播放中", guide.State.IsPlaying && guide.State.Title == "测试视频", guide.State.Title);

            // 4. 任务模式：开始时暂停并隐藏，结束后恢复显示
            view.Fake.Commands.Clear();
            host.Dispatcher.BeginTask();
            await Until(() => !view.IsShown, 3000);
            Check("任务开始：隐藏小窗并暂停", !view.IsShown && guide.State.IsHiddenByTask && view.Fake.Commands.Contains(GuidePlayerCommand.Pause),
                string.Join(",", view.Fake.Commands));
            host.Dispatcher.EndTask();
            await Until(() => view.IsShown, 3000);
            Check("任务结束：恢复显示", view.IsShown && !guide.State.IsHiddenByTask);

            // 5. 用户隐藏 / 显示
            guide.Hide();
            await Settle(200);
            Check("隐藏：保留网页", !view.IsShown && guide.State.IsOpen && guide.State.IsHiddenByUser);
            guide.ToggleVisible();
            await Settle(200);
            Check("再次显示", view.IsShown && !guide.State.IsHiddenByUser);

            // 6. 剪贴板打开
            UiHost.Clipboard.SetText("看这个 https://www.bilibili.com/video/BV1xx411c7mD 攻略");
            guide.OpenFromClipboard();
            await Settle(300);
            Check("从剪贴板打开链接", view.Fake.Navigated.LastOrDefault()?.Contains("BV1xx411c7mD") == true, view.Fake.Navigated.LastOrDefault());

            // 7. 字幕浮窗：开启后有字幕时显示，参照区域为游戏画面
            config.SubtitleWindowEnabled = true;
            view.Fake.Subtitle("测试字幕");
            await Settle(300);
            Check("字幕浮窗显示，参照区域为游戏画面", subtitle is { IsShown: true } s && game is { } gg && s.Anchor == new System.Drawing.Rectangle(gg.X, gg.Y, gg.Width, gg.Height),
                subtitle?.Anchor);

            // 8. 遮罩显示或移动后重新置顶
            var before = view.TopmostCount;
            host.Services.GetRequiredService<MaskWindowStateSource>().Publish(new MaskWindowState(true, new System.Drawing.Rectangle(0, 0, 100, 100)));
            Check("遮罩变化后重新置顶小窗", view.TopmostCount == before + 1, $"{before} → {view.TopmostCount}");

            // 9. 关闭
            guide.Close();
            await Settle(300);
            Check("关闭：释放窗口", view.Closed_ && !guide.State.IsOpen && subtitle?.IsShown != true);
        }
        finally
        {
            (config.PositionMode, config.FreeLeft, config.FreeTop, config.WindowWidth, config.WindowHeight, config.FollowAnchor,
                config.FollowOffsetX, config.FollowOffsetY, config.AutoOpenOnGameStart, config.SubtitleWindowEnabled, config.WasOpen,
                config.HideWhenGameInactive) = saved;
            GuideViews.CreateWindow = oldWindow;
            GuideViews.CreateSubtitle = oldSubtitle;
            GuideLinuxPlatform.IsWebViewAvailable = oldWeb;
        }
    }
}
