# 架构说明（第一阶段之后）

## 项目结构

```
BetterGenshinImpact.sln
├─ BetterGenshinImpact.Core              net10.0（纯跨平台）
│   ├─ Platform/                         平台抽象：GameKey、PixelRect、IPlatformWindow、IWindowManager、
│   │                                     IGameCapture、GameCaptureFrame、IHotkeyService、IPlatformServices…
│   └─ Core/Input/                       输入抽象：IInputBackend、IInputChannel、IKeyboardInput、IMouseInput、
│                                         InputChannelBase、UnsupportedInputChannel
├─ BetterGenshinImpact.Platform.Windows  net10.0-windows（Win32 实现）
│   ├─ Core/Input/Backends/Win32/        SendInputChannel、PostMessageChannel、Win32InputBackend（从主项目移入）
│   ├─ Core/Simulator/                   PostMessageSimulator、ExtendedKeys（移入）、Win32Input（共享 InputSimulator）
│   ├─ Helpers/PrimaryScreen.cs          （移入）
│   └─ Platform/Windows/                 Win32PlatformWindow、Win32WindowManager、Win32HotkeyService、
│                                         Win32PlatformServices、Win32Environment（Wine 检测）、RECT↔PixelRect 转换
├─ BetterGenshinImpact.Platform.Linux    net10.0（X11 / XWayland 实现）
│   └─ X11/                              X11Connection、X11Window、X11WindowManager、X11Capture、
│                                         XTestInputBackend/XTestInputChannel、X11HotkeyService、X11PlatformServices
├─ Fischless.GameCapture                 net10.0-windows：BitBlt / WGC / DWM 截图器，实现 Core 的 IGameCapture
├─ Fischless.HotkeyCapture               net10.0-windows：RegisterHotKey（被 Win32HotkeyService 包装）
├─ Fischless.WindowsInput                netstandard2.0：SendInput 封装（仍使用 User32.VK，只在 Win32 实现内部使用）
├─ BetterGenshinImpact                   net10.0-windows：WPF 主程序 + 业务（下一阶段继续向 Core 拆分）
└─ Test/*                                单元测试 / 手动测试
```

依赖方向：`Core` ← `Platform.Windows` / `Platform.Linux` / `Fischless.GameCapture` ← 主程序。Core 不引用任何 Windows 专属包（Vanara、WPF、WinForms、System.Drawing.Common）。

命名空间：移入 Core / Platform.Windows 的文件**保留原命名空间**（例如 `BetterGenshinImpact.Core.Input`），调用方无需修改；
新的平台类型在 `BetterGenshinImpact.Platform`，主项目通过 `GlobalUsings.Platform.cs` 全局导入。

## 平台抽象

| 抽象 | 作用 | Windows | Linux（X11） |
|---|---|---|---|
| `GameKey` | 键码，数值与名称都与 Win32 VK 相同（`VK_A`、`VK_LSHIFT`…），配置、脚本中按名称保存的值保持兼容 | `(User32.VK)key` 直接转换 | `KeyMap`：VK → KeySym → keycode |
| `PixelRect` / `PixelPoint` / `PixelSize` | 物理像素几何，替代 `RECT` / `System.Drawing.Rectangle` | `ToPixelRect()` / `ToRECT()` 只在 WPF/Win32 边界使用 | — |
| `IPlatformWindow` | 窗口对象，替代 `nint hWnd`。`Handle` 为不透明标识（HWND / XID） | `Win32PlatformWindow`；`IGameWindow` 继承它 | `X11Window` |
| `IWindowManager` + `WindowQuery` | 枚举 / 按标题、类名（WM_CLASS）、pid、进程名查找 | `Win32WindowManager`（EnumWindows） | `X11WindowManager`（`_NET_CLIENT_LIST`） |
| `IGameCapture` | `Start(IPlatformWindow, settings)` / `Capture()` / `Stop()`，帧为 `Mat` + 截图区域 `PixelRect?`，`Capabilities` 说明能否截被遮挡窗口 / HDR | BitBlt、DWM、WGC、WGC V2 | `X11Capture`（MIT-SHM，回退 XGetImage） |
| `IInputBackend` / `IInputChannel` | 前台通道 + **可空**的后台通道；`Capabilities` 说明是否需要前台、是否支持相对移动 / 键状态查询、绝对移动是否会转视角 | `Win32InputBackend`（SendInput + PostMessage） | `XTestInputBackend`（只有前台） |
| `IHotkeyService` + `HotkeyGesture` | 注册全局热键，返回 `IDisposable` 用于注销 | `Win32HotkeyService`（包装 HotkeyHook） | `X11HotkeyService`（XGrabKey） |
| `IPlatformServices` | 平台服务工厂 + 环境探测（`PlatformKind`：Windows / WindowsOnWine / LinuxX11…） | `Win32PlatformServices` | `X11PlatformServices` |

### 约定

- **截图帧格式**：所有截图器输出 BGR 三通道 `Mat`（`CV_8UC3`），与识别代码的既有假设一致。`X11Capture` 可通过设置 `pixelFormat = "bgra"` 输出 BGRA；`GameCaptureFrame.WrapBgra` 可零拷贝包装平台给出的 BGRA 缓冲区。
- **坐标**：`IMouseInput.MoveMouseTo(double, double)` 保持 SendInput 的 0~65535 归一化语义（现有调用方不变）；新增 `MoveMouseToPixel(int, int)` 按虚拟桌面物理像素定位。
- **AbsoluteMoveAffectsCamera**：Wine 会把绝对移动转换成相对原始输入，游戏会转动视角。Win32 通道在 Wine 下、XTest 通道面对 Wine 窗口时该标志为 true，上层在需要“只移动光标”的场景应先检查它。
- **后台输入**：`IInputBackend.Background` 可为 null（原生 Linux 没有 PostMessage 等价物）。`InputHub.Background` 此时返回 `UnsupportedInputChannel`：记录告警（限频）并忽略操作，不抛异常。
- **SimulateAction**：依赖业务层的按键绑定配置，不放在 Core 接口里，改为主项目中的扩展方法 `IInputChannel.SimulateAction(GIActions, KeyType)`，语义与原实现相同。
- **日志**：Core 中的 `InputChannelBase` 通过 `InputChannelBase.LoggerFactory` 获取 logger，主程序在模块初始化时注入 `App.GetLogger`，未注入时使用 `NullLogger`。
- **Wine 窗口**：X11 侧的 `ClassName` 是 WM_CLASS（小写 exe 名，如 `yuanshen.exe/YuanShen.exe`），不是 Win32 窗口类名（`UnityWndClass`）；X11 侧的进程名来自 `/proc/<pid>/cmdline` 中的 exe 文件名。
- **线程**：Xlib 连接不保证线程安全，Linux 实现中窗口管理、每个截图器、每个输入后端、热键线程各自持有独立连接，并用连接上的锁串行化。

## 运行时如何选择平台（规划）

第一阶段只提供实现，**主程序尚未改为通过 `IPlatformServices` 获取平台能力**（Win32 运行时仍直接构造 `Win32InputBackend` 与截图器）。第二阶段在 DI 中注册：

```csharp
IPlatformServices platform = OperatingSystem.IsWindows()
    ? new Win32PlatformServices(captureMode)      // 包括 BGI 运行在 Wine 中的情况
    : new X11PlatformServices();                  // Linux 原生（X11 / XWayland）
```

`IGameRuntimeProvider` 再通过 `platform.Windows` 查找游戏窗口、`platform.CreateCapture()` 与 `platform.CreateInput(window)` 组装 `GameRuntime`。
