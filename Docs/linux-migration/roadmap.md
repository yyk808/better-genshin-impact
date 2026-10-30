# 迁移路线图

总体思路：**不推翻上游现有的 `GameRuntime` / `InputHub` 抽象**，先在 Windows 版里把 Win32 类型从接口中剥离（行为不变），再把平台无关代码逐步移入纯 `net10.0` 的 Core，最后在 Linux 平台实现与 Avalonia UI 上组装出 Linux 版。每个阶段都要求 Windows 版可编译、功能不变。

Linux 上原神运行在 Wine / Proton 中，游戏窗口是 X11（或 XWayland）窗口。因此第一版 Linux 支持只承诺 **X11 / XWayland**；纯 Wayland（portal）放到后面。

## 阶段 0：调研与环境（已完成）

- Windows 依赖统计、各子系统的 Linux 替代方案（见 [survey.md](survey.md)）。
- 原版 BetterGI 在 Wine 中运行成功；上游 refactor 分支可以在 Linux 上用 `EnableWindowsTargeting` 编译。

## 阶段 0.5：平台功能验证原型（已完成，未入库）

独立的小程序 + 两个“假游戏”（Wine 中运行的 Windows 版、X11 原生版，会把收到的输入写进日志并把内部状态编码进画面），
验证 X11 窗口查找、MIT-SHM 截图、XTest 键鼠（含相对移动 → Wine 原始输入）、XGrabKey 全局热键、Wine 内 PostMessage 后台输入。
结论：X11 / XWayland 路线可行，XTest 相对移动在 Wine 中被转换为数值精确的 WM_INPUT；原生 Linux 侧没有可用的后台输入。

## 阶段 1：平台抽象地基（本次完成，详见 [phase1-foundation.md](phase1-foundation.md)）

- 新增 `BetterGenshinImpact.Core`（纯 `net10.0`）：`GameKey`、`PixelRect`、`IPlatformWindow`、`IGameCapture`、`IInputBackend`/`IInputChannel`、`IHotkeyService`、`IPlatformServices` 等。
- 接口中的 `User32.VK` / `RECT` / `nint hWnd` 换成平台无关类型，调用方批量更新。
- 新增 `Platform.Windows`（包装现有实现）与 `Platform.Linux`（X11 实现，从原型移植）。
- 验收：全解决方案在 Linux 上 0 错误编译；Core 与 Platform.Linux 不带 Windows 目标可编译；单元测试结果与改动前一致；
  Platform.Linux 在两个假游戏上的端到端回归与原型结果一致。

## 阶段 2：接入平台服务 + 继续清理 Win32 依赖（进行中，进展见 [phase2-progress.md](phase2-progress.md)）

目标：业务层（GameTask / Core）不再直接调用 Win32，所有平台能力都经由 `IPlatformServices`。

1. ✅ **平台服务注册**（里程碑 1）：在 DI 中注册 `IPlatformServices`（Windows: `Win32PlatformServices`；Wine 下仍用 Win32 实现），`Win32RuntimeProvider` 改为通过 `IWindowManager` 查找、激活窗口。
2. ✅ **`SystemControl` 拆分**（里程碑 1）：找窗迁到 `IGameWindowFinder`，Win32 窗口函数迁到 `Platform.Windows` 的 `Win32WindowOps`，进程名 / 启动参数迁到 Core；剩余调用方逐步改用 `IPlatformWindow`。
3. ✅ **键状态与录制**（里程碑 2，`RawInputMonitor` 相对鼠标与 Wine 轮询未动）：`Simulation.IsKeyDown`、`RawInputMonitor`、`MouseKeyMonitor`、`KeyMouseMacroPlayer`、`GlobalKeyMouseRecord` 抽象为 `IGlobalInputHook` + `IInputChannel.IsKeyDown`。
4. **音游输入**（大部分完成）：`KeyInputTransports` 已经走 `InputHub` 的前台 / 后台通道（`IInputChannel`）；封面、跟弹键盘钩子与取色已抽象，音游模型 / 服务和自动音游移入 Core（里程碑 18）。`RawInputMonitor` 的 Linux 对应实现 `X11RelativeMouseInputMonitor`（XInput2 `XI_RawMotion`）已完成（里程碑 17）。
5. ✅ **热键**（里程碑 3；UI 的 `HotKey` 模型仍用 WPF `Key`，配置文本格式不变，Core 用 `HotkeyText` 解析）：`HotkeyHolder` 改用 `IHotkeyService`（`Win32HotkeyService` 已就绪），热键配置类型从 WinForms `Keys` 迁到 `HotkeyGesture`。
6. ✅ **UI 线程与对话框**（里程碑 5；与 WPF 窗口直接相关的部分留给 Avalonia 宿主）：`Dispatcher` / `Application.Current` → `IUiDispatcher`；`MessageBox` → `IDialogService`；剪贴板、打开链接 → 对应服务。
7. ✅ **`System.Drawing`**（里程碑 6；Primitives 中的跨平台类型保留，GDI+ 只剩两处调试图标注）：业务代码中的 `Rectangle`/`Point`/`Bitmap` 换成 OpenCvSharp 或 Core 中的几何类型。
8. ✅ **注册表**（里程碑 4，只读部分；写注册表留待 Linux 宿主）：原神分辨率 / HDR / 安装路径读取抽象为 `IGameSettingsStore`（Linux 实现解析 Wine 前缀的 `user.reg`）。
9. 每完成一组就把对应代码从主项目移入 Core，直到识别、任务、脚本引擎都在 Core 中。（进行中，按 git 跟踪的 `.cs` 统计，Core 共 983 个，主项目剩 759 个（里程碑 32）。
   - 里程碑 7a–9 已移入识别基础、配置根与全部配置、服务基础设施，以及识别核心：`Region` / `ImageRegion`、OCR / ONNX / YOLO、`Bv`、`GameRuntime`。
   - `Region` 的点击 / 移动已改走 `PlatformHost` + `IInputChannel`。
   - ClearScript 的 Linux 原生包（`Microsoft.ClearScript.V8.Native.linux-x64` 7.4.5）已实测可用。
   - 里程碑 10–13 解开了任务层的界面映射、键鼠钩子、WPF 颜色、网页窗口和 Win32 `RECT` 引用，并把第一批 106 个任务层文件移入 Core。
   - 里程碑 14–16 又移入 206 个任务层文件：自动战斗、路径追踪、大地图、脚本组、七圣召唤、秘境等。
   - 里程碑 18–19 移入音游与剧情语音的播放 / 解码；Linux 的 MP3 解码用 NLayer，音频输出先用空实现（里程碑 36 改为 PulseAudio / PipeWire 输出，不可用时静音；剧情语音会话与 TTS 引擎移入 Core，Linux 可选 espeak-ng 系统语音）。
   - 里程碑 20 移入 `TaskRunner`、`SoloTaskExecutionService`、`RedeemCodeManager`。
   - 里程碑 21 移入 `TaskTriggerDispatcher`、`GameTaskManager`；里程碑 22 移入 `GameRuntimeService` 并新增基于 `IPlatformServices` 的 `PlatformRuntimeProvider`。
   - 里程碑 23–24 新增无界面宿主 `BetterGenshinImpact.Host.Headless`：X11 找窗、截图、CPU OCR / ONNX、按帧调度触发器，已在假游戏上验证自动拾取发出 F 键、自动剧情点击选项；剩余步骤见 phase2-progress.md 里程碑 24。
   - 里程碑 25：没有后台通道时后台输入在游戏前台时改走前台通道；新增 `Build/Linux/build-opencvsharp.sh` 自行编译与托管包 4.11 对齐、不依赖系统库的完整 OpenCvSharpExtern（含 SIFT / SURF / dnn）。
   - 剩余：一条龙、脚本仓库更新 / 路径录制的界面拆分、剧情语音的 SAPI 注册与会话工厂、跟弹会话，见 phase2-progress.md 里程碑 20。）

验收：Core 中包含识别（OpenCV / ONNX / OCR）、脚本引擎、主要 GameTask；单元测试中平台无关部分在 Linux 原生运行通过。

## 阶段 3：Linux 平台补全

- 截图：XComposite（可截被遮挡窗口，里程碑 28 已实现 `CompositeWindow`）；XDamage 按需截图、Vulkan 游戏全屏下的黑屏 / 撕裂待评估。
- 输入：uinput 后端（需要权限，X11 / Wayland 通用）；XWayland 下 XTest 注入的实测。
- 遮罩 overlay：Avalonia 透明窗口 + `_NET_WM_STATE_ABOVE` + SHAPE 输入区域置空（点击穿透），需要合成器（里程碑 27、28 已实现）。
- 推理：ONNX Runtime CPU 默认，CUDA / ROCm / OpenVINO 可选（替代 DirectML）。
- 音频：NAudio → OpenAL / SDL2 / PulseAudio（里程碑 36：libpulse-simple 输出已完成）；通知 → `org.freedesktop.Notifications`；凭据 → libsecret。
- `X11Window.ViewportChanged` 通过 ConfigureNotify 实现。

## 阶段 4：Avalonia UI（进行中，进展见 [phase4-avalonia.md](phase4-avalonia.md)）

1. ✅ 新建 `BetterGenshinImpact.Avalonia`（`net10.0`，Avalonia 12）宿主：DI（与无界面宿主共用 `Host.Common` 的 `GameHost`）、日志面板、MainWindow 外壳（Avalonia 自带 Fluent 主题，样式仿原版 NavigationView / CardControl）、`UiHost` 的 Avalonia 实现（里程碑 26）。
2. 首批页面：首页（启动截图器）、实时触发设置、遮罩 MaskWindow → 在 Linux 上跑通“启动截图 → 自动拾取 / 自动剧情”。（首页与实时触发已完成并在假游戏上跑通拾取 / 剧情；里程碑 27 补全首页的截图模式、手动选择窗口、同时启动游戏，并实现 Linux 上的配置持久化 `User/config.json`（与 Windows 格式相同）；遮罩窗口（透明、置顶、SHAPE 点击穿透）与识别框 / 日志框绘制已完成，截图改用 Composite 窗口图像避免截到遮罩（里程碑 28）；HTML 遮罩待 WebView）
3. 按页面逐个迁移：独立任务 → 一条龙 → 脚本 / JS → 地图追踪 → 热键 → 其它设置页。ViewModel（CommunityToolkit.Mvvm）大部分可复用。（里程碑 29：独立任务页与一条龙页主体已完成，执行走 Core 的 `SoloTaskExecutionService` / `OneDragonTask`；各任务的详细设置界面待迁移。里程碑 31：全自动页（调度器 / 配置组、JS 脚本、地图追踪、录制回放列表）与设置页常用项（通用、遮罩、快捷键、通知、关于）已完成，快捷键经 X11 XGrabKey 生效（里程碑 32 补齐其余动作与长按类快捷键；里程碑 33 新增不拦截按键的“键鼠监听”方式（XInput2 原始事件）与 Linux 键鼠录制）；Linux 通知（网络通知器与 freedesktop 系统通知）里程碑 32 接入；Windows 版新的“任务计划”（Pulonia 计划系统）里程碑 34 迁移：模型、存储、调度与执行在 Core，Avalonia 任务计划页可编辑、运行、停止；里程碑 35 补上任务参数与定时触发编辑，以及调度器的 JS 自定义设置、配置组设置、项目通用设置；共享设置、计划快捷键与 HTML 设置页待迁移）
4. WebView2 相关（云原神、HTML 遮罩、脚本设置页）最后迁移，Linux 用 Avalonia NativeWebView（WebKitGTK）。
5. WPF UI 在过渡期保留，Windows 版可以先继续使用。

## 阶段 5：打包与发布

AppImage / Flatpak、udev 规则（uinput）、Linux 使用文档；Wayland（portal：ScreenCast、RemoteDesktop、GlobalShortcuts）作为后续增强。

## 主要风险

- 上游 `refactor/20260930` 变化很快：抽象尽量放在新项目中，定期合并上游；阶段 1、2 的改动适合拆成小 PR 回馈上游。
- Wayland 下无法全局查找窗口 / 截图 / 输入，只能走 portal 并需要用户授权。
- 截图在合成器 unredirect / Vulkan 全屏时的表现需要在真实环境实测；没有 HDR。
- UI 工作量最大（151 个 XAML，大量 WPF-UI 控件）。
- 反作弊对 uinput / XTest 的态度无法完全评估。
