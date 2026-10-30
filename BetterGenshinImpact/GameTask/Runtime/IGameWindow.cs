using System;

namespace BetterGenshinImpact.GameTask.Runtime;

/// <summary>
/// 游戏画面所在的顶层窗口，网页版为宿主窗口。
/// <para>
/// 平台无关的成员（句柄、进程、标题、类名、画面区域、前台/最小化/存活、激活、请求关闭、移动通知）
/// 都定义在 <see cref="IPlatformWindow"/>（BetterGenshinImpact.Core）里，截图器 <see cref="IGameCapture.Start"/> 直接接收窗口对象。
/// 这里保留 BGI 运行环境的语义说明：
/// </para>
/// <list type="bullet">
/// <item><see cref="IPlatformWindow.Handle"/>：顶层窗口句柄，用于截图、遮罩定位与焦点恢复</item>
/// <item><see cref="IPlatformWindow.ProcessId"/>：窗口所属进程，网页版为当前 BetterGI 进程</item>
/// <item><see cref="IPlatformWindow.IsAlive"/>：Win32 为游戏进程未退出；网页版为宿主窗口未关闭且 RTC 通道为 open</item>
/// <item><see cref="IPlatformWindow.RequiresForeground"/>：Win32 为 true（SendInput 只发往前台窗口）；网页版为 false（输入经 RTC 发往云端），失焦后任务继续运行</item>
/// <item><see cref="IPlatformWindow.Activate"/>：Win32 还原并置前；网页版只从最小化还原，不抢前台</item>
/// </list>
/// <para>
/// 成员都是实时查询，会在截图调度的线程池线程上调用，实现不能访问 WPF 对象。
/// </para>
/// </summary>
public interface IGameWindow : IPlatformWindow
{
}
