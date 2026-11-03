using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

namespace BetterGenshinImpact.Platform.Linux.Wine;

/// <summary>
/// 在游戏所在的 Wine 前缀里运行后台输入辅助程序（<see cref="WinePostHelperImage"/>），由它对游戏窗口调用 PostMessageW。
/// 与 Windows 版 PostMessageChannel 等价：实测游戏不在前台时按键与点击都送达、不抢前台；
/// XSendEvent 不行（按键被 Wine 忽略，点击会把游戏窗口激活）。
/// <para>
/// 启动方式：读取游戏进程的环境（/proc/&lt;pid&gt;/environ，含 WINEPREFIX、esync / fsync 等，须与游戏的 wineserver 一致）与
/// 可执行文件位置（/proc/&lt;pid&gt;/exe 所在目录的 wine64 / wine，或环境里的 WINELOADER），用同一个 wine 启动辅助程序。
/// Steam Proton 的游戏在 pressure-vessel 容器里运行，容器外的 wine 连不上容器里的 wineserver，这种情况在启动前按挂载命名空间 / 环境标记识别并返回原因（<see cref="DescribeContainer"/>）
/// </para>
/// </summary>
public sealed class WinePostMessageClient : IWinePostSink
{
    private readonly Process _process;
    private readonly Stream _in;
    private readonly object _gate = new();
    private bool _dead;

    private WinePostMessageClient(Process process, nint hwnd)
    {
        _process = process;
        _in = process.StandardInput.BaseStream;
        Hwnd = hwnd;
    }

    /// <summary>辅助程序找到的游戏窗口 HWND（Wine 内的句柄）</summary>
    public nint Hwnd { get; }

    /// <summary>辅助程序是否还在运行</summary>
    public bool IsAlive => !_dead && !_process.HasExited;

    /// <summary>标志：仅当游戏不在前台时投递</summary>
    public const uint OnlyIfBackground = 1;

    /// <summary>标志：按键消息，扫描码与扩展键位由 Wine 的键盘布局填入 lParam</summary>
    public const uint FillScanCode = 2;

    /// <summary>
    /// 启动辅助程序并按窗口标题找到游戏窗口
    /// </summary>
    /// <param name="pid">游戏进程号（X 窗口 _NET_WM_PID）</param>
    /// <param name="title">窗口标题（Wine 窗口的 X 标题即 Win32 标题）</param>
    /// <param name="className">Win32 窗口类名，null 或空为不限</param>
    /// <param name="error">失败原因</param>
    /// <param name="helperDir">辅助程序写到的目录，默认 $XDG_CACHE_HOME/bettergi（或 ~/.cache/bettergi）</param>
    /// <param name="procRoot">/proc 根目录（测试可替换）</param>
    public static WinePostMessageClient? TryStart(int pid, string title, string? className, out string? error,
        string? helperDir = null, string procRoot = "/proc", int timeoutMs = 30000)
    {
        error = null;
        Dictionary<string, string> env;
        string? exeDir;
        try
        {
            env = ReadEnviron(Path.Combine(procRoot, pid.ToString(), "environ"));
            var exe = new FileInfo(Path.Combine(procRoot, pid.ToString(), "exe")).LinkTarget;
            exeDir = exe is null ? null : Path.GetDirectoryName(exe);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = $"读取游戏进程 {pid} 的环境失败：{e.Message}";
            return null;
        }

        if (DescribeContainer(procRoot, pid, env) is { } container)
        {
            error = container;
            return null;
        }

        var loader = FindLoader(env, exeDir);
        if (loader is null)
        {
            error = $"找不到游戏进程 {pid} 使用的 wine（WINELOADER 未设置，{exeDir ?? "?"} 下也没有 wine64 / wine）";
            return null;
        }

        string helper;
        try
        {
            helper = WriteHelper(helperDir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = $"写入后台输入辅助程序失败：{e.Message}";
            return null;
        }

        var psi = new ProcessStartInfo(loader)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = "/",
        };
        psi.ArgumentList.Add(helper);
        psi.Environment.Clear();
        foreach (var (k, v) in env) psi.Environment[k] = v;
        psi.Environment["WINEDEBUG"] = "-all";
        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new IOException("Process.Start 返回 null");
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            error = $"启动 {loader} 失败：{e.Message}";
            return null;
        }

        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        try
        {
            var head = new byte[512];
            Encoding.Unicode.GetBytes(Truncate(className ?? "", 127)).CopyTo(head, 0);
            Encoding.Unicode.GetBytes(Truncate(title, 127)).CopyTo(head, 256);
            var stdin = process.StandardInput.BaseStream;
            stdin.Write(head);
            stdin.Flush();
            var reply = new byte[8];
            var read = process.StandardOutput.BaseStream.ReadAtLeastAsync(reply, 8, throwOnEndOfStream: false).AsTask();
            if (!read.Wait(timeoutMs) || read.Result < 8)
            {
                error = read.IsCompleted ? $"后台输入辅助程序提前退出（{loader}，前缀 {env.GetValueOrDefault("WINEPREFIX", "~/.wine")}）" : "后台输入辅助程序没有响应（超时）";
                Kill(process);
                return null;
            }

            var hwnd = (nint)BinaryPrimitives.ReadInt64LittleEndian(reply);
            if (hwnd == 0)
            {
                error = $"后台输入辅助程序在 Wine 里找不到标题为“{title}”的窗口";
                Kill(process);
                return null;
            }

            return new WinePostMessageClient(process, hwnd);
        }
        catch (Exception e) when (e is IOException or AggregateException or ObjectDisposedException)
        {
            error = $"与后台输入辅助程序通信失败：{e.Message}";
            Kill(process);
            return null;
        }
    }

    /// <summary>投递一条消息；辅助程序已退出时返回 false</summary>
    public bool Post(uint msg, ulong wParam, ulong lParam, uint flags = 0)
    {
        Span<byte> rec = stackalloc byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(rec, msg);
        BinaryPrimitives.WriteUInt32LittleEndian(rec[4..], flags);
        BinaryPrimitives.WriteUInt64LittleEndian(rec[8..], wParam);
        BinaryPrimitives.WriteUInt64LittleEndian(rec[16..], lParam);
        lock (_gate)
        {
            if (_dead) return false;
            try
            {
                _in.Write(rec);
                _in.Flush();
                return true;
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
                _dead = true;
                return false;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (!_dead)
            {
                _dead = true;
                try { _in.Dispose(); } catch (IOException) { }
            }
        }

        // 关闭 stdin 后辅助程序自行退出；没退出就结束它
        if (!_process.WaitForExit(2000)) Kill(_process);
        _process.Dispose();
    }

    /// <summary>辅助程序文件：内容不同时重写</summary>
    public static string WriteHelper(string? dir = null)
    {
        dir ??= Path.Combine(Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } c ? c : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"), "bettergi");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "bettergi-post-helper.exe");
        var bytes = WinePostHelperImage.Build();
        if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
        {
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, path, overwrite: true);
        }

        return path;
    }

    /// <summary>wine 启动器：WINELOADER，否则游戏进程可执行文件所在目录（或其上级的 bin）里的 wine64 / wine</summary>
    public static string? FindLoader(IReadOnlyDictionary<string, string> env, string? exeDir)
    {
        if (env.TryGetValue("WINELOADER", out var l) && File.Exists(l)) return l;
        if (exeDir is null) return null;
        foreach (var dir in new[] { exeDir, Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "bin")), Path.GetFullPath(Path.Combine(exeDir, "..", "bin")) })
        {
            foreach (var name in new[] { "wine64", "wine" })
            {
                var p = Path.Combine(dir, name);
                if (File.Exists(p)) return p;
            }
        }

        return null;
    }

    /// <summary>
    /// 游戏在容器里（与本进程的挂载命名空间不同，或环境里有 pressure-vessel / Flatpak 标记）时返回不能使用的原因，否则 null。
    /// 容器里的 wineserver 套接字与 wine 路径在容器外不可见，从外面启动的辅助程序连不上游戏的 wineserver
    /// </summary>
    public static string? DescribeContainer(string procRoot, int pid, IReadOnlyDictionary<string, string> env)
    {
        string? kind = null;
        if (env.Keys.Any(k => k.StartsWith("PRESSURE_VESSEL_", StringComparison.Ordinal))
            || env.GetValueOrDefault("container") == "pressure-vessel")
            kind = "Steam Proton 的 pressure-vessel 容器（Steam Linux Runtime）";
        else if (env.ContainsKey("FLATPAK_ID") || env.GetValueOrDefault("container") == "flatpak")
            kind = "Flatpak 沙盒";
        else
        {
            var theirs = ReadLink(Path.Combine(procRoot, pid.ToString(), "ns", "mnt"));
            var ours = ReadLink(Path.Combine(procRoot, "self", "ns", "mnt"));
            if (theirs is not null && ours is not null && theirs != ours)
                kind = env.ContainsKey("STEAM_COMPAT_DATA_PATH") ? "Steam Proton 的容器（Steam Linux Runtime）" : "独立的挂载命名空间（容器）";
        }

        return kind is null
            ? null
            : $"游戏进程 {pid} 运行在{kind}里，容器外启动的 wine 连不上容器内的 wineserver，Wine 后台输入不可用"
              + "（Steam 可在游戏属性里关闭 Steam Linux Runtime，或改用 Lutris / 系统 Wine 启动）";
    }

    private static string? ReadLink(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static Dictionary<string, string> ReadEnviron(string path)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in File.ReadAllText(path).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = kv.IndexOf('=');
            if (i > 0) env[kv[..i]] = kv[(i + 1)..];
        }

        return env;
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];

    private static void Kill(Process p)
    {
        try { p.Kill(); } catch (InvalidOperationException) { }
    }
}
