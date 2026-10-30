using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// <see cref="IGameDesktopInterop"/> 的 Win32 实现，代码原样取自 GameLoading / Login3rdParty / AutoWoodTask
/// </summary>
public sealed class Win32GameDesktopInterop : IGameDesktopInterop
{
    public static Win32GameDesktopInterop Instance { get; } = new();

    public (IntPtr windowHandle, string windowType) FindBiliLoginWindow(Process? process, Action<Exception> onEnumError)
    {
        IntPtr bHWnd = IntPtr.Zero;
        string windowType = "";

        User32.EnumWindows((hWnd, lParam) =>
        {
            try
            {
                // 获取窗口标题
                int titleLength = User32.GetWindowTextLength(hWnd);
                if (titleLength > 0)
                {
                    StringBuilder title = new StringBuilder(titleLength + 1);
                    User32.GetWindowText(hWnd, title, title.Capacity);

                    string titleText = title.ToString();

                    // 检查是否是B服登录窗口（通过标题匹配）
                    if (titleText.Contains("bilibili", StringComparison.OrdinalIgnoreCase))
                    {
                        // 检查窗口所有者是否是原神进程
                        var owner = User32.GetWindow(hWnd, User32.GetWindowCmd.GW_OWNER);
                        if (owner != IntPtr.Zero)
                        {
                            User32.GetWindowThreadProcessId(owner, out uint ownerPid);
                            if (ownerPid == process!.Id)
                            {
                                // 检查窗口是否可见和启用
                                bool isVisible = User32.IsWindowVisible(hWnd);
                                bool isEnabled = User32.IsWindowEnabled(hWnd);

                                // 检查协议窗口
                                if (titleText.Contains("协议", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (isEnabled)
                                    {
                                        bHWnd = hWnd.DangerousGetHandle();
                                        windowType = "协议";
                                        return false;
                                    }
                                }

                                // 检查登录窗口
                                if (titleText.Contains("登录", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (isEnabled)
                                    {
                                        bHWnd = hWnd.DangerousGetHandle();
                                        windowType = "登录";
                                        return false;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                onEnumError(ex);
            }

            return true;
        }, IntPtr.Zero);

        return (bHWnd, windowType);
    }

    public string? QueryProcessImagePathOfWindow(IntPtr windowHandle)
    {
        uint tid = User32.GetWindowThreadProcessId(windowHandle, out uint pid);

        if (tid != 0)
        {
            using Kernel32.SafeHPROCESS hProcess = Kernel32.OpenProcess(new ACCESS_MASK(Kernel32.ProcessAccess.PROCESS_QUERY_INFORMATION), false, pid);

            if (!hProcess.IsInvalid)
            {
                StringBuilder devicePath = new(260);
                uint size = (uint)devicePath.Capacity;

                if (Kernel32.QueryFullProcessImageName(hProcess, 0, devicePath, ref size))
                {
                    return devicePath.ToString();
                }

                Debug.WriteLine($"Error getting process image file name. Error code: {Marshal.GetLastWin32Error()}");
            }
        }

        return null;
    }

    public void SetKeepSystemAwake(bool keepAwake)
    {
        if (keepAwake)
        {
            Kernel32.SetThreadExecutionState(Kernel32.EXECUTION_STATE.ES_CONTINUOUS | Kernel32.EXECUTION_STATE.ES_SYSTEM_REQUIRED | Kernel32.EXECUTION_STATE.ES_DISPLAY_REQUIRED);
        }
        else
        {
            Kernel32.SetThreadExecutionState(Kernel32.EXECUTION_STATE.ES_CONTINUOUS);
        }
    }
}
