using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Platform.Linux;

namespace BetterGenshinImpact.Core.UnitTest.Platform;

public class UiServicesTests
{
    [Fact]
    public void UiHost_Defaults_AreHeadless()
    {
        Assert.NotNull(UiHost.Dispatcher);
        Assert.NotNull(UiHost.Dialogs);
        Assert.NotNull(UiHost.Clipboard);
        Assert.Throws<ArgumentNullException>(() => UiHost.Dispatcher = null!);
    }

    [Fact]
    public async Task InlineDispatcher_RunsOnCallingThread()
    {
        var d = new InlineUiDispatcher();
        var thread = Environment.CurrentManagedThreadId;
        var ran = 0;
        d.Invoke(() => { Assert.Equal(thread, Environment.CurrentManagedThreadId); ran++; });
        d.Post(() => ran++);
        Assert.Equal(5, d.Invoke(() => 5));
        Assert.Equal("x", await d.InvokeAsync(() => "x"));
        Assert.True(d.CheckAccess());
        Assert.Equal(2, ran);
        await Assert.ThrowsAsync<InvalidOperationException>(() => d.InvokeAsync<int>(() => throw new InvalidOperationException()));
        // GameRuntimeService 的写法：InvokeAsync(() => Task).Unwrap()
        Assert.Equal(7, await d.InvokeAsync(() => Task.FromResult(7)).Unwrap());
    }

    [Fact]
    public async Task HeadlessDialogs_ReportMessagesWithDefaultTitles()
    {
        var dialogs = new HeadlessDialogService();
        var shown = new List<(DialogKind, string, string)>();
        dialogs.Shown += (k, t, m) => shown.Add((k, t, m));

        dialogs.Error("e");
        Assert.Equal(DialogResult.None, await dialogs.ErrorAsync("e2", "自定义"));
        dialogs.Warning("w");
        await dialogs.InformationAsync("i");
        dialogs.Toast(DialogKind.Success, "t");

        Assert.Equal(
        [
            (DialogKind.Error, "错误", "e"),
            (DialogKind.Error, "自定义", "e2"),
            (DialogKind.Warning, "警告", "w"),
            (DialogKind.Information, "信息", "i"),
            (DialogKind.Success, "", "t"),
        ], shown);
    }

    [Fact]
    public void InMemoryClipboard_SetGetClear()
    {
        var c = new InMemoryClipboard();
        Assert.Null(c.GetText());
        c.SetText("兑换码 ABC");
        Assert.Equal("兑换码 ABC", c.GetText());
        c.Clear();
        Assert.Null(c.GetText());
    }

    [Fact]
    public void CommandLineClipboard_WithoutTools_FallsBackToMemory()
    {
        var c = new CommandLineClipboard(commands: null);
        if (c.Active is not null)
        {
            return; // 本机装有真实剪贴板工具时不覆盖用户剪贴板
        }

        c.SetText("a");
        Assert.Equal("a", c.GetText());
        c.Clear();
        Assert.Null(c.GetText());
    }

    [Fact]
    public void CommandLineClipboard_UsesConfiguredCommands()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var dir = Directory.CreateTempSubdirectory("bgi-clip-").FullName;
        try
        {
            var store = Path.Combine(dir, "clip");
            string Script(string name, string body)
            {
                var p = Path.Combine(dir, name);
                File.WriteAllText(p, "#!/bin/sh\n" + body + "\n");
                File.SetUnixFileMode(p, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                return p;
            }

            var commands = new CommandLineClipboard.Commands(
                [Script("set", $"cat > '{store}'")],
                [Script("get", $"[ -f '{store}' ] || exit 1; cat '{store}'")],
                [Script("clear", $"rm -f '{store}'")]);
            var c = new CommandLineClipboard(commands);

            Assert.Null(c.GetText());
            c.SetText("原神 Genshin\n第二行");
            Assert.Equal("原神 Genshin\n第二行", File.ReadAllText(store));
            Assert.Equal("原神 Genshin\n第二行", c.GetText());
            c.Clear();
            Assert.False(File.Exists(store));
            Assert.Null(c.GetText());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CommandLineClipboard_BuiltInCommandSets()
    {
        Assert.Equal("wl-copy", CommandLineClipboard.WlClipboard.Set[0]);
        Assert.Contains("clipboard", CommandLineClipboard.XClip.Get);
        Assert.Contains("--clear", CommandLineClipboard.XSel.Clear);
    }
}
