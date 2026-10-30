using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace BetterGenshinImpact.Avalonia;

public partial class App : Application
{
    /// <summary>由 Program 在启动前设置：创建主窗口（此时 Avalonia 已初始化，可以使用字体管理器、剪贴板等）</summary>
    public static Action<IClassicDesktopStyleApplicationLifetime>? Startup { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Startup is { } startup)
        {
            startup(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
