# 第一阶段：平台抽象地基

目标：在不改变 Windows 行为的前提下，把输入 / 截图 / 窗口接口中的 Win32 类型换成平台无关类型，拆出纯 `net10.0` 的 Core，
并提供 Windows（包装现有实现）和 Linux（X11）两套平台实现。本阶段**不迁移 UI**。

## 完成内容

### 1. 新增 `BetterGenshinImpact.Core`（纯 net10.0）

- `GameKey`：由 Vanara `User32.VK` 生成，220 个成员，名称与数值完全一致（包括别名），外加 `None = 0`。
  这样 `Enum.Parse` / `ToString()` 的结果不变，配置文件、键鼠录制脚本、JS 脚本中的按键名无需迁移。`GameKeys` 提供宽松解析（可省略 `VK_` 前缀，支持 `esc` 等别名）。
- `PixelRect`（含 `Left/Top/Right/Bottom`、`FromLTRB`、`Intersect`、与 `System.Drawing.Rectangle` 和 OpenCV `Rect` 互转）、`PixelPoint`、`PixelSize`。
- `GameViewport`（从主项目移入）、`IPlatformWindow`、`WindowQuery`、`IWindowManager`。
- `IGameCapture`、`GameCaptureFrame`、`CaptureCapabilities`（从 `Fischless.GameCapture` 移入并修改）。
- `InputCapabilities`、`KeyModifiers`、`HotkeyGesture`、`IHotkeyService`、`IPlatformServices`、`PlatformKind`、环境探测。
- 输入抽象（`IInputBackend`、`IInputChannel`、`IKeyboardInput`、`IMouseInput`、`InputChannelBase`、`InputEnums`）从主项目移入，改用 `GameKey`；
  新增 `MoveMouseToPixel`、通道 `Capabilities`、`UnsupportedInputChannel`，`Background` 改为可空。

### 2. 接口去 Win32 类型

| 原签名 | 新签名 |
|---|---|
| `IKeyboardInput.KeyDown(User32.VK)` 等、`IInputChannel.IsKeyDown(User32.VK)` | `GameKey` |
| `IGameCapture.Start(nint hWnd, …)` | `Start(IPlatformWindow window, …)` |
| `GameCaptureFrame(Mat, RECT?)` | `GameCaptureFrame(Mat, PixelRect?)` |
| `IGameWindow`（独立接口） | `IGameWindow : IPlatformWindow`，新增 `Title`、`ClassName`、`RequestClose()` |
| `IInputBackend.Background`（非空） | `IInputChannel?`；`InputHub.Background` 为空时返回告警并忽略的通道 |
| `IInputChannel.SimulateAction(...)`（接口成员） | 主项目中的扩展方法，行为不变 |
| `ISystemInfo.CaptureAreaRect` / `GameScreenSize`（`RECT`） | `PixelRect` |
| `KeyMouseScript.Adapt(RECT, …)`、`WebSdkInputBackend(Func<RECT>)` | `PixelRect` |

调用方更新：

- `User32.VK` → `GameKey`：用脚本批量替换，主项目与测试项目 **62 个文件、358 处**（最多的是 `OverrideController` 109 处、
  `KeyBindingsSettingsPageViewModel` 44 处、`KeyInputTransports` 33 处、`AutoMusicGameTask` 22 处）。业务代码中直接调用
  Fischless.WindowsInput / PostMessage 的地方显式转换 `(User32.VK)key`。
- `RECT` / `hWnd`：约 30 处手工修改（4 个截图器、2 个运行时提供者、`SystemInfo`、`TaskTriggerDispatcher`、`KeyMouseScript`、
  `Genshin.cs` 脚本 API、`ScanPickTask`、音游会话、截图测试窗口、两个 WPF 窗口等）。只在 WPF / Win32 边界（遮罩定位、`SystemControl`、`GameWindowState`）保留 `RECT`，用 `ToPixelRect()` / `ToRECT()` 转换。
- 统计（`.cs` 文件，改动前 → 改动后）：`User32.VK` 61 个文件 / 260 处 → 9 个文件 / 74 处（剩余的都在 `Fischless.WindowsInput`
  与 `Platform.Windows` 的 Win32 实现内部，以及注释）；`RECT` 34 个文件 / 84 处 → 20 个文件 / 42 处（UI / Win32 边界）。

### 3. `BetterGenshinImpact.Platform.Windows`

- 从主项目移入：`SendInputChannel`、`PostMessageChannel`、`Win32InputBackend`、`PostMessageSimulator`、`ExtendedKeys`、`PrimaryScreen`（保留原命名空间）。
- `Win32Input`：SendInput 通道与旧的 `Simulation` 静态入口共享同一个 `InputSimulator` 实例（与改动前一致），并提供 `IsKeyDown`（GetAsyncKeyState）、`ReleaseAllKeys`。
- 新增 `Win32PlatformWindow`、`Win32WindowManager`、`Win32HotkeyService`、`Win32PlatformServices`、`Win32Environment`（`wine_get_version` 检测）。
- 截图器声明能力：BitBlt 不能截被遮挡窗口；DWM / WGC / WGC V2 可以；WGC 报告 HDR 状态。

### 4. `BetterGenshinImpact.Platform.Linux`（X11 / XWayland）

从平台验证原型移植，改为实现 Core 接口：

- `X11WindowManager` / `X11Window`：`_NET_CLIENT_LIST` 枚举（无窗口管理器时回退 `XQueryTree`），WM_CLASS / `_NET_WM_PID` / `/proc` 进程名，
  EWMH `_NET_ACTIVE_WINDOW` 激活（超时回退 `XSetInputFocus`）、`_NET_CLOSE_WINDOW` 关闭。
- `X11Capture`：MIT-SHM `XShmGetImage` 截取根窗口上的客户区，失败自动回退 `XGetImage`；可选直接读窗口 drawable；输出 BGR `Mat`（可选 BGRA）。
- `XTestInputBackend` / `XTestInputChannel`：键鼠、相对移动、按像素 / 归一化绝对移动、滚轮、`XQueryKeymap` / `XQueryPointer` 键状态；没有后台通道。
- `X11HotkeyService`：独立连接 + 事件线程的 `XGrabKey`，同时抓取 NumLock / CapsLock 组合。
- `KeyMap`：GameKey（VK）↔ KeySym，比原型多了 F13–F24、小键盘运算键、OEM_102、媒体键。
- 只依赖 libX11 / libXext / libXtst（`LibraryImport`，运行时加载）。

## 验证结果

| 项 | 结果 |
|---|---|
| 全解决方案在 Linux 上编译（`-p:EnableWindowsTargeting=true`） | ✅ 0 错误（警告均为既有警告） |
| `BetterGenshinImpact.Core`、`BetterGenshinImpact.Platform.Linux` 不带 Windows 目标单独编译 | ✅ 0 错误、0 警告 |
| 单元测试（Linux 原生，用去掉 WindowsDesktop 框架引用的 runtimeconfig 运行 908 个用例） | 改动前后**逐条结果完全一致**：776 通过 / 127 失败 / 5 跳过。失败的都是 Linux 环境原因（缺 WPF/WinForms 程序集、COM 互操作、Windows native 库、超时），在改动前的提交上同样失败 |
| Platform.Linux 端到端回归（临时驱动把 BGI 的 Platform.Linux 适配到原型的测试套件，独立 Xvfb 显示） | Wine 版假游戏 **17 PASS**；X11 原生假游戏 **16 PASS / 1 SKIP**（SKIP 为原生没有后台输入通道，符合预期）。与原型实现的结果一致 |
| 默认截图格式 | BGR `CV_8UC3`，与 BGRA 输出逐像素比对一致 |
| 修改后的 Windows 版在 Wine 中启动 | ✅ 主窗口正常显示，识别到 Wine 环境，OCR 模型加载正常（需在前缀中安装 .NET 10 Desktop 与 ASP.NET Core 运行时） |

端到端用例覆盖：窗口查找、激活、SHM / XGetImage 截图（颜色标记 + 画面条码校验）、截图性能（约 7 ms/帧）、按住 / 序列按键、键状态查询、
绝对移动 + 点击、右键 / 中键、滚轮、普通与锁定视角下的相对移动（Wine 中转换为精确的原始输入）、全局热键（含组合键与注销）、Wine 内 PostMessage、画面随输入变化。

## 遗留的 Windows 依赖（下一阶段处理）

- **UI**：全部 WPF / WPF-UI / WebView2 页面与窗口、遮罩 `MaskWindow`、托盘、`Dispatcher` / `Application.Current`、`MessageBox`。
- **窗口管理**：`SystemControl`（查找、激活、DPI、`GameWindowState` 仍为 `RECT`）、`DpiAwarenessController`、Guide 窗口服务。
- **输入**：`RawInputMonitor`、`MouseKeyMonitor`、键鼠录制（`GlobalKeyMouseRecord`、DirectInput 校准）、音游 `KeyInputTransports`（直接 SendInput / PostMessage）、`Simulation` 旧静态入口、JS `PostMessage` API。
- **热键**：`HotkeyHolder` / 热键配置仍基于 WinForms `Keys` 与 `Fischless.HotkeyCapture`（`Win32HotkeyService` 已就绪，尚未接入）。
- **截图**：`Fischless.GameCapture` 内部仍是 BitBlt / WGC / DWM；Linux 截图器尚未接入运行时。
- **其它**：注册表（原神设置 / HDR / 安装路径）、`System.Drawing`、NAudio、DirectML 推理设备发现、PresentMon、硬件监控、凭据管理、子会话（wtsapi32）。
- 主程序尚未通过 `IPlatformServices` 选择平台实现；业务代码仍在主项目（`net10.0-windows`）中，Core 目前只有抽象层与输入基类。
