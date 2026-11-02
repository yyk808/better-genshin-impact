using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.Core.Script.WebView;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Http;
using BetterGenshinImpact.Infrastructure.Network;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Helpers.Win32;
using BetterGenshinImpact.Infrastructure.Storage;
using BetterGenshinImpact.Infrastructure.Temp;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.View.Controls.Webview;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.View.Windows;
using LibGit2Sharp;
using BetterGenshinImpact.Core.Script.Repositories;
using LibGit2Sharp.Handlers;
using Vanara.PInvoke;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.Core.Script;

/// <summary>
/// 脚本仓库更新：WPF 部分（WebView2 仓库网页窗口、仓库设置窗口、订阅确认消息框、Windows 凭据管理器、desktop.ini 文件夹属性）
/// </summary>
public partial class ScriptRepoUpdater
{
    private WebpageWindow? _webWindow;

    private async partial Task<bool> ConfirmImportAsync(string pathJson, bool formClipboard)
    {
        var uiMessageBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = "脚本订阅",
            Content =
                $"检测到{(formClipboard ? "剪切板上存在" : "")}脚本订阅链接，解析后需要导入的脚本为：{pathJson}。\n是否导入并覆盖此文件或者文件夹下的脚本？",
            CloseButtonText = "关闭",
            PrimaryButtonText = "确认导入",
            Owner = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        uiMessageBox.SourceInitialized += (s, e) => WindowHelper.TryApplySystemBackdrop(uiMessageBox);
        WindowHelper.CenterOnVisibleOwner(uiMessageBox);

        var result = await uiMessageBox.ShowDialogAsync();
        return result == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }

    private static partial GitCredential? ReadGitCredential() =>
        CredentialManagerHelper.ReadCredential("BetterGenshinImpact.GitCredentials") is { } credential
            ? new GitCredential(credential.UserName, credential.Password)
            : null;

    public void OpenLocalRepoInWebView()
    {
        UpdateSubscribedScriptPaths();
        if (_webWindow is not { IsVisible: true })
        {
            var scriptConfig = Config.ScriptConfig;

            // 计算宽高（默认0.7屏幕宽高）
            double width = scriptConfig.WebviewWidth == 0
                ? SystemParameters.WorkArea.Width * 0.7
                : scriptConfig.WebviewWidth;

            double height = scriptConfig.WebviewHeight == 0
                ? SystemParameters.WorkArea.Height * 0.7
                : scriptConfig.WebviewHeight;

            // 计算位置（默认居中）
            double left = scriptConfig.WebviewLeft == 0
                ? (SystemParameters.WorkArea.Width - width) / 2
                : scriptConfig.WebviewLeft;

            double top = scriptConfig.WebviewTop == 0
                ? (SystemParameters.WorkArea.Height - height) / 2
                : scriptConfig.WebviewTop;
            
            WindowState state = (WindowState)scriptConfig.WebviewState;
            var screen = SystemParameters.WorkArea;
            bool isSmallScreen = screen.Width <= 1600 || screen.Height <= 900;
            // 如果未设置或非法值，则默认 Normal，小屏则直接最大化
            if (isSmallScreen)
            {
                state = WindowState.Maximized;
            }
            else if (!Enum.IsDefined(typeof(WindowDisplayState), scriptConfig.WebviewState))
            {
                state = WindowState.Normal;
            }
            else
            {
                state = (WindowState)scriptConfig.WebviewState;
            }
            
            _webWindow = new WebpageWindow
            {
                Title = "Genshin Copilot Scripts | BetterGI 脚本本地中央仓库",
                Width = width,
                Height = height,
                Left = left,
                Top = top,
                WindowStartupLocation = WindowStartupLocation.Manual,
                WindowState = state
            };
            // 关闭时保存窗口位置与大小
            _webWindow.Closed += (s, e) =>
            {
                if (_webWindow != null)
                {
                    scriptConfig.WebviewLeft = _webWindow.Left;
                    scriptConfig.WebviewTop = _webWindow.Top;
                    scriptConfig.WebviewWidth = _webWindow.Width;
                    scriptConfig.WebviewHeight = _webWindow.Height;
                    scriptConfig.WebviewState = (WindowDisplayState)_webWindow.WindowState;
                }

                _webWindow = null;
            };

            _webWindow.Panel!.DownloadFolderPath = PathingPaths.PathJsonPath;
            // _webWindow.NavigateToFile(Global.Absolute(@"Assets\Web\ScriptRepo\index.html"));
            _webWindow.Panel!.OnWebViewInitializedAction = () =>
            {
                var assetsPath = Global.Absolute(@"Assets\Web\ScriptRepo");
                _webWindow.Panel!.WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "bettergi.local",
                    assetsPath,
                    CoreWebView2HostResourceAccessKind.Allow
                );

                // _webWindow.Panel!.WebView.CoreWebView2.Navigate("https://bettergi.local/index.html");

                _webWindow.Panel!.WebView.CoreWebView2.AddHostObjectToScript("repoWebBridge", new RepoWebBridge(Config));

                // 内部外链使用默认浏览器打开：由 WebViewSetup 统一处理，只放行 http、https 地址
            };

            _webWindow.NavigateToUri(new Uri("https://bettergi.local/index.html"));
            _webWindow.Show();
        }
        else
        {
            _webWindow.Activate();
        }
    }

    public void OpenScriptRepoWindow()
    {
        var scriptRepoWindow = new ScriptRepoWindow { Owner = Application.Current.MainWindow };
        scriptRepoWindow.ShowDialog();
    }

    /// <summary>
    /// 处理带有 icon.ico 和 desktop.ini 的文件夹
    /// </summary>
    /// <param name="folderPath"></param>
    private partial void DealWithIconFolder(string folderPath)
    {
        if (Directory.Exists(folderPath)
            && File.Exists(Path.Combine(folderPath, "desktop.ini")))
        {
            // 使用 Vanara 库中的 SetFileAttributes 函数设置文件夹属性
            if (Kernel32.SetFileAttributes(folderPath, FileFlagsAndAttributes.FILE_ATTRIBUTE_READONLY))
            {
                Debug.WriteLine($"成功将文件夹设置为只读: {folderPath}");
            }
            else
            {
                Debug.WriteLine($"无法设置文件夹为只读: {folderPath}");
            }
        }
    }
}
