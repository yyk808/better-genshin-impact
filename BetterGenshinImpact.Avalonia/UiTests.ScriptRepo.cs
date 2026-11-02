using System.Diagnostics;
using Avalonia.Controls;
using BetterGenshinImpact.Avalonia.Views;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Pulonia.Services;

namespace BetterGenshinImpact.Avalonia;

/// <summary>
/// 脚本仓库（--ui-test script-repo，对应 WPF ScriptRepoWindow 与 WebView2 仓库网页）：用本机临时 Git 仓库（file://，release 分支）作自定义渠道，
/// 更新仓库、Git 凭据写入凭据库（自测换成内存实现，不碰系统密钥环）、打开仓库网页（WebKitGTK，bgi-repo:// 协议与 repoWebBridge 桥接）、
/// 网页里订阅脚本（ImportUri → 确认框 → 复制到 User/JsScript）、远端有新提交后再更新仓库并一键更新订阅、重置仓库、从 zip 导入。
/// 结束时删除 Repos/bettergi-scripts-list、订阅文件、导入的脚本与临时网页文件，恢复脚本配置
/// </summary>
internal sealed partial class UiTests
{
    private sealed class MemoryVault : IPuloniaCredentialVault
    {
        public readonly Dictionary<string, PuloniaCredential> Items = new();
        public PuloniaCredential? Read(string target) => Items.GetValueOrDefault(target);
        public void Write(string target, string userName, string password) => Items[target] = new PuloniaCredential(userName, password);
        public void Delete(string target) => Items.Remove(target);
    }

    /// <summary>最小的 Git 智能 HTTP 服务（CGI 调用 git http-backend），只监听 127.0.0.1，端口由系统分配并打印到标准输出</summary>
    /// <summary>
    /// 原生 WebKitGTK 内容不经 Avalonia 渲染，RenderTargetBitmap 截不到。
    /// 测试显示器（Xvfb，无合成器）上用 XGetImage 从根窗口截取该窗口所在区域（仅用 libX11 与 PIL，不需要额外系统包）
    /// </summary>
    private const string X11GrabWindow = """
        import ctypes, sys
        from PIL import Image
        x = ctypes.cdll.LoadLibrary('libX11.so.6')
        x.XOpenDisplay.restype = ctypes.c_void_p; x.XOpenDisplay.argtypes = [ctypes.c_char_p]
        x.XDefaultRootWindow.restype = ctypes.c_ulong; x.XDefaultRootWindow.argtypes = [ctypes.c_void_p]
        class XImage(ctypes.Structure):
            _fields_ = [('width', ctypes.c_int), ('height', ctypes.c_int), ('xoffset', ctypes.c_int), ('format', ctypes.c_int), ('data', ctypes.c_void_p),
                        ('byte_order', ctypes.c_int), ('bitmap_unit', ctypes.c_int), ('bitmap_bit_order', ctypes.c_int), ('bitmap_pad', ctypes.c_int),
                        ('depth', ctypes.c_int), ('bytes_per_line', ctypes.c_int), ('bits_per_pixel', ctypes.c_int)]
        class Attr(ctypes.Structure):
            _fields_ = [('x', ctypes.c_int), ('y', ctypes.c_int), ('width', ctypes.c_int), ('height', ctypes.c_int), ('pad', ctypes.c_byte * 200)]
        x.XGetImage.restype = ctypes.POINTER(XImage)
        x.XGetImage.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_int, ctypes.c_int, ctypes.c_uint, ctypes.c_uint, ctypes.c_ulong, ctypes.c_int]
        x.XGetWindowAttributes.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.POINTER(Attr)]
        x.XTranslateCoordinates.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_ulong, ctypes.c_int, ctypes.c_int,
                                            ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_ulong)]
        d = x.XOpenDisplay(None); win = int(sys.argv[1], 0); root = x.XDefaultRootWindow(d); a = Attr()
        x.XGetWindowAttributes(d, win, ctypes.byref(a))
        rx, ry, child = ctypes.c_int(), ctypes.c_int(), ctypes.c_ulong()
        x.XTranslateCoordinates(d, win, root, 0, 0, ctypes.byref(rx), ctypes.byref(ry), ctypes.byref(child))
        img = x.XGetImage(d, root, rx.value, ry.value, a.width, a.height, 0xFFFFFFFF, 2).contents
        buf = ctypes.string_at(img.data, img.bytes_per_line * img.height)
        Image.frombuffer('RGB', (img.width, img.height), buf, 'raw', 'BGRX', img.bytes_per_line, 1).save(sys.argv[2]); print(img.width, img.height)
        """;

    /// <summary>X11 下用 XGetImage 截取窗口（含原生子窗口内容）；非 X11 或失败时返回 false</summary>
    private bool ShotX11(Window target, string file)
    {
        if (dir is null || target.TryGetPlatformHandle() is not { HandleDescriptor: "XID" } handle) return false;
        try
        {
            Directory.CreateDirectory(dir);
            var script = Path.Combine(Path.GetTempPath(), "bgi-uitest-x11grab.py");
            File.WriteAllText(script, X11GrabWindow);
            var psi = new ProcessStartInfo("python3") { ArgumentList = { script, "0x" + handle.Handle.ToString("x"), Path.Combine(dir, file) }, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit(15000);
            File.Delete(script);
            if (p.ExitCode != 0) log.Warning("XGetImage 截图失败：{Err}", err);
            return p.ExitCode == 0;
        }
        catch (Exception e)
        {
            log.Warning(e, "XGetImage 截图失败");
            return false;
        }
    }

    private const string GitHttpServer = """
        import http.server, os, subprocess, sys
        root = sys.argv[1]
        class H(http.server.BaseHTTPRequestHandler):
            def handle_one(self):
                path, _, query = self.path.partition('?')
                length = int(self.headers.get('Content-Length') or 0)
                body = self.rfile.read(length) if length else b''
                env = dict(os.environ, GIT_PROJECT_ROOT=root, GIT_HTTP_EXPORT_ALL='1', REQUEST_METHOD=self.command,
                           PATH_INFO=path, QUERY_STRING=query, CONTENT_TYPE=self.headers.get('Content-Type') or '',
                           CONTENT_LENGTH=str(length), REMOTE_ADDR='127.0.0.1', GIT_HTTP_MAX_REQUEST_BUFFER='100M')
                if self.headers.get('Content-Encoding'): env['HTTP_CONTENT_ENCODING'] = self.headers.get('Content-Encoding')
                if self.headers.get('Git-Protocol'): env['GIT_PROTOCOL'] = self.headers.get('Git-Protocol')
                out = subprocess.run(['git', 'http-backend'], input=body, env=env, capture_output=True).stdout
                head, _, data = out.partition(b'\r\n\r\n')
                status = 200
                headers = []
                for line in head.split(b'\r\n'):
                    k, _, v = line.decode().partition(':')
                    if k.lower() == 'status': status = int(v.strip().split()[0])
                    elif k: headers.append((k, v.strip()))
                self.send_response(status)
                for k, v in headers: self.send_header(k, v)
                self.send_header('Content-Length', str(len(data)))
                self.end_headers()
                self.wfile.write(data)
            do_GET = handle_one
            do_POST = handle_one
            def log_message(self, *a): pass
        s = http.server.ThreadingHTTPServer(('127.0.0.1', 0), H)
        print(s.server_address[1], flush=True)
        s.serve_forever()
        """;

    private static string Git(string workDir, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = workDir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "-c", "user.name=bgi-test", "-c", "user.email=bgi-test@localhost", "-c", "init.defaultBranch=release" }.Concat(args)) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return output;
    }

    private async Task ScriptRepoAsync()
    {
        var config = ConfigHost.Config!.ScriptConfig;
        var saved = (config.SelectedChannelName, config.CustomRepoUrl, config.ScriptRepoHintDotVisible, config.WebviewLeft, config.WebviewTop,
            config.WebviewWidth, config.WebviewHeight, config.WebviewState, config.GuideStatus);
        var oldFactory = PuloniaTaskStore.DefaultCredentialVaultFactory;
        var vault = new MemoryVault();
        PuloniaTaskStore.DefaultCredentialVaultFactory = () => vault;

        var root = Path.Combine(Path.GetTempPath(), "bgi-m64-srepo");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        var work = Directory.CreateDirectory(Path.Combine(root, "work")).FullName;
        var origin = Path.Combine(root, "origin.git");
        var scriptDir = Directory.CreateDirectory(Path.Combine(work, "repo", "js", "M64RepoTest")).FullName;
        File.WriteAllText(Path.Combine(scriptDir, "manifest.json"), """{"manifest_version":1,"name":"M64 仓库自测","version":"1.0","main":"main.js","authors":[{"name":"bgi-test"}]}""");
        File.WriteAllText(Path.Combine(scriptDir, "main.js"), "log.info('m64 v1');\n");
        File.WriteAllText(Path.Combine(work, "repo.json"), """
            {"time":"20261103000000","url":"","file":"repo.zip","indexes":[{"name":"js","type":"directory","children":[
              {"name":"M64RepoTest","type":"directory","version":"1.0","author":"bgi-test","description":"M64 仓库自测脚本","tags":[],"hasUpdate":true,"lastUpdated":"2026-11-03 00:00:00","children":[]}]}]}
            """);
        Git(work, "init", "-q");
        Git(work, "add", "-A");
        Git(work, "commit", "-q", "-m", "v1");
        Git(root, "clone", "-q", "--bare", work, origin);
        // 仓库地址只接受 HTTPS / HTTP / SSH：用 git http-backend 在本机回环地址提供 Git 智能 HTTP 协议
        File.WriteAllText(Path.Combine(root, "srv.py"), GitHttpServer);
        var server = Process.Start(new ProcessStartInfo("python3") { ArgumentList = { Path.Combine(root, "srv.py"), root }, RedirectStandardOutput = true, UseShellExecute = false })!;
        var port = (await server.StandardOutput.ReadLineAsync())?.Trim();
        var originUrl = $"http://127.0.0.1:{port}/origin.git";
        Check("准备本机 Git 仓库（HTTP）", Directory.Exists(origin) && int.TryParse(port, out _), originUrl);

        // 仓库网页文件随发布包提供（CI 构建）；开发目录没有时临时复制发布包里的网页
        var webDir = Global.Absolute(@"Assets\Web\ScriptRepo");
        var webIndex = Path.Combine(webDir, "index.html");
        var copiedWeb = false;
        if (!File.Exists(webIndex) && File.Exists("/workspace/bgi/release/BetterGI/Assets/Web/ScriptRepo/index.html"))
        {
            Directory.CreateDirectory(webDir);
            File.Copy("/workspace/bgi/release/BetterGI/Assets/Web/ScriptRepo/index.html", webIndex);
            copiedWeb = true;
        }

        var repoDir = ScriptRepoUpdater.CenterRepoPath;
        var userScript = Global.Absolute(@"User\JsScript\M64RepoTest");
        var subscriptionFile = Path.Combine(ScriptRepoUpdater.SubscriptionsPath, Path.GetFileName(repoDir) + ".json");
        var hadSubscriptionFile = File.Exists(subscriptionFile);
        var oldSubscriptions = hadSubscriptionFile ? File.ReadAllText(subscriptionFile) : null;
        var hadRepo = Directory.Exists(repoDir);
        Check("测试前没有官方仓库副本（不覆盖用户数据）", !hadRepo && !Directory.Exists(userScript), repoDir);
        if (hadRepo || Directory.Exists(userScript))
        {
            PuloniaTaskStore.DefaultCredentialVaultFactory = oldFactory;
            return;
        }

        try
        {
            // 1. 调度器“更多功能 → 打开脚本仓库”
            vm.Navigate("调度器");
            await Settle(500);
            vm.ScriptControl.OpenScriptRepoCommand.Execute(null);
            var repoWindow = await WaitFor(() => ScriptRepoWindow.Current);
            Check("打开脚本仓库窗口", repoWindow is { IsVisible: true });
            var rvm = repoWindow?.ViewModel;
            if (repoWindow is null || rvm is null) return;
            await Task.Delay(400);
            Check("渠道与 Windows 版相同（另有“自定义”）", rvm.RepoChannels.Count == ScriptRepoUpdater.RepoChannels.Count + 1
                && rvm.RepoChannels[^1].Name == "自定义", string.Join(",", rvm.RepoChannels.Select(c => c.Name)));
            Check("非自定义渠道地址只读", NamedIn<TextBlock>(repoWindow, "RepoUrlText") is not null && NamedIn<TextBox>(repoWindow, "CustomRepoUrlBox") is null);

            rvm.SelectedRepoChannel = rvm.RepoChannels[^1];
            await Task.Delay(300);
            Check("自定义渠道可编辑地址并写入配置", NamedIn<TextBox>(repoWindow, "CustomRepoUrlBox") is not null && config.SelectedChannelName == "自定义");
            config.CustomRepoUrl = "https://example.com/custom-repo";
            await Click(NamedIn<Button>(repoWindow, "UpdateRepoButton"), "更新仓库（占位地址）");
            Check("占位地址不更新", rvm.LastResult == "" && !Directory.Exists(repoDir), rvm.LastResult);

            config.CustomRepoUrl = originUrl;
            NamedIn<TextBox>(repoWindow, "GitUsernameBox")!.Text = "m64-user";
            NamedIn<TextBox>(repoWindow, "GitTokenBox")!.Text = "m64-token";
            await Task.Delay(200);
            await Click(NamedIn<Button>(repoWindow, "UpdateRepoButton"), "更新仓库");
            Check("更新前保存 Git 凭据到凭据库", vault.Read(ScriptRepoUpdater.GitCredentialTarget) is { UserName: "m64-user", Password: "m64-token" });
            await Until(() => rvm.LastResult != "" && !rvm.IsUpdating, 60000);
            Check("从本机 Git 仓库更新", rvm.LastResult is "updated" or "latest" && File.Exists(Path.Combine(repoDir, "repo.json")), rvm.LastResult);
            Shot(repoWindow, "scriptrepo-window.png");

            // 2. 打开仓库网页（WebKitGTK），经桥接读取仓库
            await Click(NamedIn<Button>(repoWindow, "OpenRepoButton"), "打开仓库");
            var web = await WaitFor(() => ScriptRepoWebWindow.Current);
            Check("打开仓库网页窗口并关闭设置窗口", web is { IsVisible: true } && ScriptRepoWindow.Current is null, web?.Title);
            Check("关闭设置窗口时凭据仍在", vault.Items.Count == 1);
            if (web is null) return;
            var loaded = await Until(() => web.LoadedPage, 20000);
            Check("仓库网页加载完成（bgi-repo:// 协议）", loaded, web.LastError);

            async Task<string?> Bridge(string expression)
            {
                await web.EvalAsync("window.__m64 = undefined; Promise.resolve(" + expression + ").then(v => window.__m64 = JSON.stringify({ v: v }), e => window.__m64 = JSON.stringify({ e: String(e) })); 'ok';");
                for (var i = 0; i < 100; i++)
                {
                    var result = await web.EvalAsync("window.__m64 || ''");
                    if (!string.IsNullOrEmpty(result) && result != "\"\"") return result;
                    await Task.Delay(100);
                }

                return null;
            }

            var repoJson = await Bridge("chrome.webview.hostObjects.repoWebBridge.GetRepoJson()");
            Check("桥接 GetRepoJson 读到仓库索引", repoJson?.Contains("M64RepoTest") == true, repoJson?[..Math.Min(120, repoJson.Length)]);
            var file = await Bridge("chrome.webview.hostObjects.repoWebBridge.GetFile('js/M64RepoTest/main.js')");
            Check("桥接 GetFile 读脚本文件", file?.Contains("m64 v1") == true, file);
            var escape = await Bridge("chrome.webview.hostObjects.repoWebBridge.GetFile('../../../etc/passwd')");
            Check("桥接 GetFile 拒绝目录外路径", escape?.Contains("404") == true, escape);
            var guide = await Bridge("chrome.webview.hostObjects.repoWebBridge.GetGuideStatus()");
            Check("桥接 GetGuideStatus 返回布尔值", guide is "{\"v\":true}" or "{\"v\":false}" or "\"{\\\"v\\\":true}\"" or "\"{\\\"v\\\":false}\"" || guide?.Contains("\\\"v\\\":") == true, guide);

            // 网页订阅：与网页“订阅”按钮相同调用 ImportUri，确认后复制到 User/JsScript
            var import = "bettergi://script?import=" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Uri.EscapeDataString("[\"js/M64RepoTest\"]")));
            var last = Services.FluentMessageBox.Current;
            await web.EvalAsync($"chrome.webview.hostObjects.repoWebBridge.ImportUri('{import}'); 'ok';");
            var confirm = await WaitFor(() => Services.FluentMessageBox.Current is { } c && c != last ? c : null);
            Check("订阅链接弹出确认框", confirm is not null);
            confirm?.Hide(FluentAvalonia.UI.Controls.FAContentDialogResult.Primary);
            await Until(() => File.Exists(Path.Combine(userScript, "main.js")), 15000);
            Check("确认后脚本复制到 User/JsScript", File.ReadAllText(Path.Combine(userScript, "main.js")).Contains("m64 v1"));
            var subscribed = await Bridge("chrome.webview.hostObjects.repoWebBridge.GetSubscribedScriptPaths()");
            Check("订阅列表包含脚本", subscribed?.Contains("js/M64RepoTest") == true, subscribed);
            await Settle(800);
            Check("仓库网页截图（XGetImage）", ShotX11(web, "scriptrepo-web.png"));
            web.Close();
            await Settle(300);

            // 3. 远端有新提交：再次更新仓库，一键更新订阅
            File.WriteAllText(Path.Combine(scriptDir, "main.js"), "log.info('m64 v2');\n");
            Git(work, "commit", "-q", "-am", "v2");
            Git(work, "push", "-q", origin, "release");
            // 任务设置页七圣召唤卡组旁的“脚本仓库”按钮（与 WPF TaskSettingsPage 相同）
            ScriptRepoWindow.Current?.Close();
            await Settle(300);
            vm.Tasks.OpenLocalScriptRepoCommand.Execute(null);
            repoWindow = await WaitFor(() => ScriptRepoWindow.Current);
            rvm = repoWindow?.ViewModel;
            if (repoWindow is null || rvm is null) return;
            Check("再次打开时恢复自定义渠道与凭据", rvm.SelectedRepoChannel?.Name == "自定义" && rvm.GitUsername == "m64-user" && rvm.GitToken == "m64-token");
            await Click(NamedIn<Button>(repoWindow, "UpdateRepoButton"), "更新仓库（有新提交）");
            await Until(() => rvm.LastResult != "" && !rvm.IsUpdating, 60000);
            Check("仓库有新内容", rvm.LastResult == "updated", rvm.LastResult);
            await Click(NamedIn<Button>(repoWindow, "UpdateSubscribedButton"), "一键更新订阅");
            await Until(() => rvm.LastResult == "subscribed-updated" && !rvm.IsUpdating, 30000);
            Check("一键更新订阅后脚本为新版本", File.ReadAllText(Path.Combine(userScript, "main.js")).Contains("m64 v2"), rvm.LastResult);

            // 4. 重置仓库（确认框选“是”）
            last = Services.FluentMessageBox.Current;
            var reset = NamedIn<Button>(repoWindow, "ResetRepoButton");
            await Click(reset, "重置仓库");
            var resetBox = await WaitFor(() => Services.FluentMessageBox.Current is { } c && c != last ? c : null);
            Check("重置前确认", resetBox is not null);
            resetBox?.Hide(FluentAvalonia.UI.Controls.FAContentDialogResult.Primary);
            await Until(() => rvm.LastResult == "reset", 15000);
            Check("重置后仓库目录已删除", rvm.LastResult == "reset" && !File.Exists(Path.Combine(repoDir, "repo.json")), rvm.LastResult);

            // 5. 从 zip 导入
            var zip = Path.Combine(root, "repo.zip");
            System.IO.Compression.ZipFile.CreateFromDirectory(work, zip, System.IO.Compression.CompressionLevel.Fastest, includeBaseDirectory: false);
            rvm.PickZip = () => Task.FromResult<string?>(zip);
            await Click(NamedIn<Button>(repoWindow, "ImportZipButton"), "选择zip文件导入");
            await Until(() => rvm.LastResult != "reset" && !rvm.IsUpdating, 30000);
            Check("从 zip 导入仓库", rvm.LastResult == "imported" && Directory.GetFiles(repoDir, "repo.json", SearchOption.AllDirectories).Length > 0, rvm.LastResult);
            repoWindow.Close();
            await Settle(300);
        }
        finally
        {
            try { server.Kill(); } catch { }
            ScriptRepoWebWindow.Current?.Close();
            ScriptRepoWindow.Current?.Close();
            await Task.Delay(300);
            PuloniaTaskStore.DefaultCredentialVaultFactory = oldFactory;
            (config.SelectedChannelName, config.CustomRepoUrl, config.ScriptRepoHintDotVisible, config.WebviewLeft, config.WebviewTop,
                config.WebviewWidth, config.WebviewHeight, config.WebviewState, config.GuideStatus) = saved;
            try
            {
                if (Directory.Exists(repoDir)) Directory.Delete(repoDir, true);
                if (Directory.Exists(userScript)) Directory.Delete(userScript, true);
                if (hadSubscriptionFile) File.WriteAllText(subscriptionFile, oldSubscriptions!);
                else if (File.Exists(subscriptionFile)) File.Delete(subscriptionFile);
                if (!hadSubscriptionFile && Directory.Exists(ScriptRepoUpdater.SubscriptionsPath) && !Directory.EnumerateFileSystemEntries(ScriptRepoUpdater.SubscriptionsPath).Any())
                    Directory.Delete(ScriptRepoUpdater.SubscriptionsPath);
                if (Directory.Exists(ScriptRepoUpdater.ReposPath) && !Directory.EnumerateFileSystemEntries(ScriptRepoUpdater.ReposPath).Any())
                    Directory.Delete(ScriptRepoUpdater.ReposPath);
                if (copiedWeb)
                {
                    Directory.Delete(webDir, true);
                    var webParent = Path.GetDirectoryName(webDir)!;
                    if (Directory.Exists(webParent) && !Directory.EnumerateFileSystemEntries(webParent).Any()) Directory.Delete(webParent);
                }
                Directory.Delete(root, true);
            }
            catch (Exception e)
            {
                Check("清理脚本仓库自测数据", false, e.Message);
            }
        }
    }
}
