# Windows 依赖调研（精简版）

> 基于 `refactor/20260930@09d0dfa`（2026-10-30）的静态统计（rg），用于规划迁移。数字是迁移开始前的状态；第一阶段之后的变化见 [phase1-foundation.md](phase1-foundation.md)。

## 概况

- `refactor/20260930` 是上游默认分支和最活跃的开发线：`net10.0-windows`，151 个 `.xaml`，约 1857 个 `.cs`。迁移分支从它切出。
- 上游已经有一部分平台抽象：`GameRuntime`（`IGameWindow` + `IGameCapture` + `IInputBackend`）、`InputHub` 门面（业务代码约 600 多处调用经它）、`IOcrService`、`Wine/WinePlatformAddon`。问题在于接口签名里有 Win32 类型（`User32.VK`、`RECT`、`nint hWnd`）。
- 识别 / 任务逻辑（OpenCV + ONNX + Paddle OCR + ClearScript V8 + 任务状态机）大体与平台无关，但约 60 个 GameTask/Core 文件引用了 WPF / Dispatcher，约 30 个引用 `System.Drawing`。
- 难点：UI（151 个 XAML + WPF-UI + WebView2）、截图（BitBlt / WGC / DWM）、输入（SendInput / PostMessage）、遮罩 overlay、DirectML、注册表。
- 原版 BetterGI 可以在 Wine（11.0 wow64 + .NET Desktop Runtime）中运行；refactor 分支可以在 Linux 上用 `-p:EnableWindowsTargeting=true` 编译。

## 解决方案项目（迁移前）

| 项目 | TargetFramework | 说明 |
|---|---|---|
| BetterGenshinImpact | `net10.0-windows10.0.22621.0`，WPF + WinForms | 主程序（UI + 全部业务） |
| Fischless.GameCapture | `net10.0-windows…` | BitBlt / WGC / WGC-HDR / WGC V2 / DwmGetDxSharedSurface |
| Fischless.HotkeyCapture | `net10.0-windows…` | `RegisterHotKey` |
| Fischless.WindowsInput | `netstandard2.0` | SendInput 封装（Vanara.User32） |
| BetterGenshinImpact.Test / UnitTest | `net10.0-windows` | 手动测试 / xunit |
| BetterGenshinImpact.I18nSync | `net10.0` | 构建工具 |

## UI 层

| 类别 | 数量 |
|---|---|
| `.xaml` | 151（窗口约 57，页面约 45，控件约 37） |
| `using System.Windows*` | 353 个文件 / 683 处 |
| `using Wpf.Ui` | 114 个文件 |
| `Dispatcher` / `Application.Current` | 130 个文件 / 320 处 |
| `MessageBox.Show` | 32 个文件 / 71 处 |
| View 中绑定 Windows 的 `.cs` | 219 / 228；ViewModel 76 / 223（CommunityToolkit.Mvvm 可复用） |

WPF 库 → Avalonia：WPF-UI → FluentAvaloniaUI（或 SukiUI / Semi.Avalonia），托盘用 Avalonia `TrayIcon`；Microsoft.Xaml.Behaviors.Wpf → Avalonia.Xaml.Behaviors；AvalonEdit → Avalonia.AvaloniaEdit；
gong-wpf-dragdrop → Avalonia DragDrop；OpenCvSharp4.WpfExtensions → 自己写 Mat → `WriteableBitmap`；WebView2（38 个文件）→ Avalonia NativeWebView（WebKitGTK），最后迁移；OxyPlot.Wpf → OxyPlot.Avalonia / ScottPlot。

## P/Invoke 与 Win32

| 类别 | 文件 / 处 | 说明 |
|---|---|---|
| `[DllImport]` | 14 / 49 | user32、kernel32、pdh（GPU 计数器）、wtsapi32（子会话）、gdi32、WinRT（WGC）、mmdevapi、d3d12 |
| Vanara | 140 个文件 | User32、Gdi32、DwmApi、SHCore、CoreAudio、NtDll |
| `User32.` 调用 | 105 / 689 | 最多：InputBuilder、SystemControl、PostMessageSimulator、KeyInputTransports、RawInputMonitor、AutoMusicGameTask |
| `User32.VK` | 67 / 422 | 出现在 `IInputChannel` / `IKeyboardInput` 签名中 → 第一阶段已替换为 `GameKey` |
| HWND | 36 / 300 | `IGameWindow.Handle` 为 `nint` |
| 窗口样式（SetWindowLong / SetWindowPos / WS_EX_*） | 19 / 65 | 遮罩：透明、点击穿透、置顶、跟随游戏窗口 |
| `SystemControl.` | 35 / 58 | 窗口管理的事实入口 |
| 注册表 | 39 / 102 | 原神分辨率 / HDR / 安装路径、BitBlt 注册表、URL 协议、通知抑制、子会话 |
| 剪贴板 / ShellExecute / 凭据 / 管理员权限 | 12 / 38 / 7 / 4 个文件 | Avalonia `IClipboard`、`xdg-open`、libsecret、Linux 不需要 |

## 截图、输入、热键

- 截图：BitBlt（13 个文件）、WGC（含 HDR、V2）、DWM 共享表面、SharpDX（D3D11 / DirectInput）。消费方拿到的都是 `Mat`（`CaptureToRectArea` 105 个文件 / 422 处），与平台无关。
- 输入：SendInput（20 个文件）、PostMessage / SendMessage（23 个文件，后台输入）、`InputHub` 门面（`SimulateAction` 321 处、`.Mouse` 169、`.Keyboard` 97、`.Background` 40）。
- 其它：`RawInputMonitor`、键鼠录制与 DirectInput 校准、音游 `KeyInputTransports`。
- 热键 `RegisterHotKey`（8 个文件）；全局钩子 MouseKeyHook / SetWindowsHookEx（约 6 个文件）；托盘 WPF-UI.Tray；Toast（1 处）。

## 原生依赖在 Linux 上的情况

| 依赖 | Linux |
|---|---|
| OpenCvSharp4 4.11 | ✅ 需按 RID 引用 Linux native 运行时；去掉 `.Extensions`（System.Drawing）、`.WpfExtensions` |
| ONNX Runtime（+ DirectML） | ✅ CPU；GPU 用 CUDA / ROCm / OpenVINO；设备发现代码需要按平台拆分 |
| YoloSharp、Paddle OCR（自实现）、LibGit2Sharp、MailKit、Serilog 等 | ✅ |
| ClearScript V8 | ✅ `Microsoft.ClearScript.V8.Native.linux-x64` |
| SkiaSharp | ✅ `SkiaSharp.NativeAssets.Linux` |
| NAudio、Vanara.CoreAudio | ❌ → OpenAL / SDL2 / PulseAudio |
| DryWetMidi | ⚠️ 文件读写可用，MIDI 设备不支持 Linux（演奏实际是发按键，影响小） |
| PresentMon（ETW）、pdh GPU 计数器、LibreHardwareMonitor | ❌ / ⚠️ Linux 版不提供或用 hwmon |
| DeviceId.Windows | → DeviceId.Linux |

## 平台无关程度（按目录）

| 目录 | `.cs` 总数 | 绑定 Windows | 结论 |
|---|---|---|---|
| Core | 206 | 49 | 识别、脚本引擎、配置可先拆出 |
| GameTask | 574 | 110 | 主体平台无关；问题集中在 VK、Dispatcher、System.Drawing、SystemControl |
| Service | 166 | 31 | 通知、Notifier 大多可直接用 |
| Pulonia | 119 | 10 | 基本平台无关 |
| Genshin | 14 | 6 | 注册表 → 抽象（可解析 Wine 前缀的 `user.reg`） |
| ViewModel | 223 | 76 | 约 70% 可复用 |
| View | 228 | 219 | 重写为 Avalonia |

## Linux 上的关键前提

原神在 Linux 上运行于 Wine / Proton：

- **窗口**：游戏窗口是 winex11.drv 创建的 X11 窗口（Wayland 会话下是 XWayland）。可按 `WM_CLASS`（`yuanshen.exe` / `genshinimpact.exe`）、`_NET_WM_PID`、标题查找；进程名需要从 `/proc/<pid>/cmdline` 中匹配 exe。若游戏使用 winewayland.drv，X11 API 看不到它，只能走 portal。
- **截图**：X11 / XWayland 下 `XShmGetImage`（快，要求窗口可见）或 XComposite + XDamage（可截被遮挡窗口）；纯 Wayland 用 ScreenCast portal + PipeWire（需授权）。BGI 也跑在 Wine 中时，BitBlt 可用，WGC / DWM 共享表面不可用。
- **输入**：XTest 只对有焦点的 X11 / XWayland 窗口有效；相对移动经 Wine 转为原始输入（已在验证原型中确认）。uinput 最通用但需要权限；libei / RemoteDesktop portal 是 Wayland 的新方案。原生 Linux 没有 PostMessage 式的后台输入，只有 BGI 在 Wine 内运行时才可用。
- **DPI / 坐标**：Wine 窗口客户区与 X 窗口的映射、Wine 的 DPI 设置、多显示器统一在 `GameViewport` 中处理。
- **游戏设置**：分辨率 / HDR 等写在 Wine 前缀 `user.reg` 的 `HKCU\Software\miHoYo\原神` 下。

## Linux 实现思路

| 抽象 | 第一优先（X11 / XWayland） | 第二优先 | Wayland 原生 |
|---|---|---|---|
| 窗口管理 | libX11：`_NET_CLIENT_LIST`、`WM_CLASS`、`_NET_WM_PID`、`_NET_ACTIVE_WINDOW` | `/proc` 扫描 Wine 进程 | 只能通过 portal 让用户选择 |
| 截图 | `XShmGetImage` → Mat | XComposite + XDamage | PipeWire + ScreenCast portal |
| 输入 | XTest | uinput | libei / RemoteDesktop portal |
| 热键 | `XGrabKey` | evdev | GlobalShortcuts portal |
| 全局键鼠监听 | SharpHook（XRecord） | evdev | 基本不可行 |
| 遮罩 | Avalonia 透明窗口 + `_NET_WM_STATE_ABOVE` + XFixes 输入区域置空（需要合成器） | — | 只有 wlr-layer-shell |
| 推理 | ORT CPU / CUDA / ROCm / OpenVINO | — | — |
| 托盘 / 通知 | Avalonia TrayIcon（SNI）/ `org.freedesktop.Notifications` | — | 同左 |
