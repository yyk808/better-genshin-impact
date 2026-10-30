using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using BetterGenshinImpact.GameTask.Model.Area;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Runtime;

namespace BetterGenshinImpact.GameTask.AutoWood.Utils;

internal sealed class Login3rdParty
{
    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 创建第三方渠道登录辅助
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境</param>
    public Login3rdParty(GameRuntime runtime)
    {
        _runtime = runtime;
    }

    public enum The3rdPartyType
    {
        None,
        Bilibili,
    }

    public bool IsAvailabled => Type != The3rdPartyType.None;
    public The3rdPartyType Type { get; private set; } = default;
    private (double x1080, double y1080)? lastAgreementClickPos = null;

    public void RefreshAvailabled()
    {
        Type = The3rdPartyType.None;

        try
        {
            if (Process.GetProcessesByName("YuanShen").FirstOrDefault() is Process p)
            {
                string? imagePath = GameDesktopInterop.Current.QueryProcessImagePathOfWindow(p.MainWindowHandle);

                if (imagePath != null)
                {
                    FileInfo fileInfo = new(imagePath);

                    if (fileInfo.Exists)
                    {
                        string? configIni = Path.Combine(fileInfo.DirectoryName!, "config.ini");
                        string[] lines = File.ReadAllLines(configIni);

                        foreach (string line in lines)
                        {
                            string kv = line.Trim();
                            if (kv.StartsWith("channel=") && kv.EndsWith("14"))
                            {
                                Type = The3rdPartyType.Bilibili;
                                break;
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            ///
        }
    }

    public void Login(CancellationToken ct)
    {
        int failCount = default;

        while (!LoginPrivate(ct))
        {
            // It is necessary to support exitable trying.
            // Can exit trying when over than 10s.
            if (++failCount > 20)
            {
                Debug.WriteLine("[AutoWood] Give up to check login button and don't try again.");
                break;
            }

            Debug.WriteLine($"[AutoWood] Fail to check login button {failCount} time(s).");
            _runtime.Sleep(500, ct);
        }

        Debug.WriteLine("[AutoWood] Exit while check login button.");
    }

    private bool LoginPrivate(CancellationToken ct)
    {
        if (Type == The3rdPartyType.Bilibili)
        {
            if (Process.GetProcessesByName("YuanShen").FirstOrDefault() is Process process)
            {
                // B服登录
                var (loginWindow, windowType) = GetBiliLoginWindow(process);
                if (loginWindow != IntPtr.Zero)
                {
                    var dpiScale = _runtime.SystemInfo.DpiScale;
                    if (windowType.Contains("协议"))
                    {
                        _runtime.GameRegion1080PPosClick(960 + 70 * dpiScale, 540 + 75 * dpiScale);

                        // 检查窗口是否还存在
                        var (remainingWindow, remainingType) = GetBiliLoginWindow(process);
                        if (remainingWindow == IntPtr.Zero || !remainingType.Contains("协议"))
                        {
                            // 协议窗口已消失，继续等待登录窗口
                            return false;
                        }
                        // 协议窗口仍然存在，继续尝试
                        return false;
                    }
                    if (windowType.Contains("登录"))
                    {
                        Thread.Sleep(2000);
                        _runtime.GameRegion1080PPosClick(960, 540 + 90 * dpiScale);
                        Thread.Sleep(2000);

                        // 检查窗口是否还存在
                        var (remainingWindow, remainingType) = GetBiliLoginWindow(process);
                        if (remainingWindow == IntPtr.Zero)
                        {
                            Debug.WriteLine("[AutoWood] Bilibili login successful");
                            return true; // 登录成功
                        }

                        return false; // 登录窗口仍然存在，继续尝试
                    }
                }

                return false; // 没有找到登录窗口
            }

            return false;
        }
        else
        {
            // Ignore and exit with OK
            return true;
        }
    }

    static (IntPtr windowHandle, string windowType) GetBiliLoginWindow(Process process)
    {
        return GameDesktopInterop.Current.FindBiliLoginWindow(process, ex => Debug.WriteLine($"[AutoWood] 枚举窗口时出错: {ex.Message}"));
    }
}
