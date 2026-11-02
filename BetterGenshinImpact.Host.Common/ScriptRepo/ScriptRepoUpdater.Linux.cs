using System;
using System.Threading.Tasks;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Pulonia.Services;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Script;

/// <summary>
/// 脚本仓库更新：Linux 部分。仓库网页与仓库设置窗口由界面宿主（Avalonia）经 <see cref="ScriptRepoUi"/> 提供；
/// Git 凭据保存在系统密钥环（与账户密码相同的 Secret Service 凭据库），目标名与 Windows 凭据管理器相同
/// </summary>
public partial class ScriptRepoUpdater
{
    /// <summary>Git 凭据的目标名（与 Windows 版凭据管理器中的名称相同）</summary>
    public const string GitCredentialTarget = "BetterGenshinImpact.GitCredentials";

    private async partial Task<bool> ConfirmImportAsync(string pathJson, bool formClipboard)
    {
        var result = await UiHost.Dialogs.ShowAsync(
            $"检测到{(formClipboard ? "剪切板上存在" : "")}脚本订阅链接，解析后需要导入的脚本为：{pathJson}。\n是否导入并覆盖此文件或者文件夹下的脚本？",
            "脚本订阅", DialogKind.Question, DialogButtons.OkCancel);
        return result == DialogResult.Ok;
    }

    private static partial GitCredential? ReadGitCredential()
    {
        try
        {
            return PuloniaTaskStore.DefaultCredentialVaultFactory().Read(GitCredentialTarget) is { } credential
                ? new GitCredential(credential.UserName, credential.Password)
                : null;
        }
        catch (Exception e)
        {
            // 密钥环不可用时按没有凭据处理（公开仓库不需要凭据），与 Windows 版读不到凭据相同
            CoreServices.GetLogger<ScriptRepoUpdater>().LogWarning("读取 Git 凭据失败：{Message}", e.Message);
            return null;
        }
    }

    /// <summary>desktop.ini 文件夹图标是 Windows 资源管理器的功能，Linux 上不需要处理</summary>
    private partial void DealWithIconFolder(string folderPath)
    {
    }

    /// <summary>打开本地仓库网页（Avalonia：WebKitGTK 窗口）</summary>
    public void OpenLocalRepoInWebView()
    {
        UpdateSubscribedScriptPaths();
        ScriptRepoUi.OpenRepoWebView?.Invoke();
    }

    /// <summary>打开脚本仓库设置窗口</summary>
    public void OpenScriptRepoWindow() => ScriptRepoUi.OpenRepoWindow?.Invoke();
}

/// <summary>界面宿主提供的脚本仓库窗口入口（在界面线程调用）</summary>
public static class ScriptRepoUi
{
    /// <summary>打开（或激活）本地仓库网页窗口</summary>
    public static Action? OpenRepoWebView { get; set; }

    /// <summary>打开脚本仓库设置窗口</summary>
    public static Action? OpenRepoWindow { get; set; }
}
