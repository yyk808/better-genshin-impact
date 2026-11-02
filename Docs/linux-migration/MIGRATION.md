# BetterGI 跨平台 / Linux 迁移说明（fork 自用，定期同步上游）

`avalonia-linux` 是自用 fork（不向上游提交 PR），定期把上游 `refactor/20260930`（以及 `main` 上的修复）合并进来。
本文说明这个分支相对上游做了什么、Windows 版为什么行为不变、Linux 版怎么构建和测试、还有哪些已知问题，
以及以后怎样同步上游（第 8 节）。各阶段的详细记录见同目录的 [phase1-foundation.md](phase1-foundation.md)、
[phase2-progress.md](phase2-progress.md)、[phase4-avalonia.md](phase4-avalonia.md)。

## 1. 概览

- 分支：`avalonia-linux`，从 `refactor/20260930` 切出。自有提交是线性的普通提交，同步上游时使用普通合并提交（不 rebase、不改写已推送的历史）。
- 已同步到：`refactor/20260930` 的 `9e3a0b00`（2026-11-02）和 `main` 的 `07d38b31`（2026-11-01）。
- 改动规模：约 1629 个文件，其中约 1163 个是**移动**（从 WPF 主项目移到 `BetterGenshinImpact.Core`，命名空间不变），
  新增约 300 个、修改约 155 个、删除 8 个。
- 目标：
  1. 业务代码（识别、任务、脚本、配置、触发器、任务计划等）与 WPF、Win32 解耦，放进纯 `net10.0` 的 Core；
  2. Windows 版 WPF 主程序继续使用同一套 Core，**界面与行为不变**；
  3. Linux（X11 / XWayland，游戏运行在 Wine / Proton 中，或者云原神网页版）原生运行，提供无界面宿主和 Avalonia 图形界面宿主。

## 2. 项目结构与职责

```
BetterGenshinImpact.sln
├─ BetterGenshinImpact.Core              net10.0     平台无关：平台抽象接口、输入抽象、识别 / OCR / ONNX、
│                                                     任务层（独立任务、一条龙、触发器、地图追踪、脚本引擎）、
│                                                     配置模型、任务计划（Pulonia）、剧情语音会话、视图模型（无界面类型）
├─ BetterGenshinImpact.Platform.Windows  net10.0-windows  Win32 实现：SendInput / PostMessage 输入、窗口查找、热键、
│                                                     Wine 检测；包装 Fischless.* 现有实现
├─ BetterGenshinImpact.Platform.Linux    net10.0     X11 / XWayland 实现：窗口查找、MIT-SHM / XGetImage / Composite 截图、
│                                                     XTest 键鼠、XGrabKey 热键、XInput2 原始鼠标、XRecord 键鼠监听、
│                                                     PulseAudio 播放、espeak-ng 语音、剪贴板
├─ BetterGenshinImpact.Host.Common       net10.0     Linux 宿主共用的启动引导（GameHost）：平台服务、配置读写、依赖注入、
│                                                     CPU 推理、截图器启停、任务计划调度、通知、云原神运行环境；
│                                                     引用自行编译的 OpenCvSharpExtern
├─ BetterGenshinImpact.Host.Headless     net10.0     无界面宿主（bgi-headless）：命令行运行触发器 / 独立任务，OpenCV 自检
├─ BetterGenshinImpact.Avalonia          net10.0     Linux 图形界面宿主（bgi-avalonia）：Avalonia 12 + Fluent 主题，
│                                                     页面对照 WPF 原版；WebKitGTK 网页设置页 / HTML 遮罩 / 云原神窗口
├─ BetterGenshinImpact                   net10.0-windows  WPF 主程序：界面、Windows 专属服务；业务改为调用 Core
├─ Fischless.GameCapture / HotkeyCapture / WindowsInput    原样保留，只在 Windows 实现中使用
└─ Test/BetterGenshinImpact.Core.UnitTest  新增：Core 单测，Linux 上原生运行
```

依赖方向：`Core` ← `Platform.Windows` ← `BetterGenshinImpact`（WPF）；`Core` ← `Platform.Linux` ← `Host.Common` ← `Host.Headless` / `Avalonia`。
Core 不引用任何 Windows 专属包（WPF、WinForms、Vanara、System.Drawing.Common）。平台抽象接口一览见 [architecture.md](architecture.md)。

## 3. 移入 Core 的主要模块，以及 WPF 行为不变的做法

移入 Core 的主要模块：

- 识别核心：`RecognitionObject`、Region / ImageRegion、OCR（PaddleOCR）/ ONNX 推理、Bv 视觉操作、素材缓存、`GameRuntime`。
- 任务层：全部独立任务（自动秘境、首领、地脉花、幽境危战、钓鱼、七圣召唤、伐木、音游、派遣、合成、尘歌壶等）、一条龙、
  通用 Job、地图追踪与路线模型、自动连招、触发器与调度器（`TaskTriggerDispatcher` / `GameTaskManager`）、任务执行入口。
- 脚本：脚本引擎依赖（`Genshin` 等）、脚本仓库存储、配置组、脚本设置模型与 HTML 设置页请求处理。
- 配置：`AllConfig` 及各子配置、配置读写基础设施。
- 任务计划（Pulonia）：存储、构建器、执行器、调度器、路线 CD 账本。
- 剧情语音：会话、路由、HTTP TTS 引擎、`QuestVoiceService`；通知器（Webhook、企业微信、邮件）。
- 不含界面类型的视图模型：任务设置、体力计划、自动配音设置等。

为保证 WPF 行为不变，统一采用以下做法：

1. **移动时保留命名空间**。调用方不用改，XAML 的 `clr-namespace` 只追加 `;assembly=BetterGenshinImpact.Core`。
2. **平台相关部分改为接口，由宿主注入**。WPF 注入的实现就是原来的代码，原样搬到主项目或 `Platform.Windows`。例如：
   - `IGameSystemControl`、`ITriggerDispatcher`；
   - `IUiDispatcher` / 消息框 / 剪贴板（`UiHost`）；
   - `QuestVoiceAudioHost.OutputFactory`（Windows 仍为 WasapiOut）；
   - `GameFrameGrabber`；
   - `TtsProviderRegistry` 的平台引擎（上游已移除 Windows SAPI；Linux 注册 espeak-ng）；
   - 凭据存储（Windows 仍为凭据管理器）。
3. **界面映射移出任务类**。独立任务的设置界面改由界面程序集上的 `SoloTaskSettingsMap` 声明，任务类不再引用 WPF 类型。
4. **WPF 颜色、键名、GDI+ 等改为平台无关表示**，在边界上转换：
   - `MaskColor` 取代 WPF Color；
   - `GameKey` 的数值与名称都和 Win32 VK 相同，配置与脚本中保存的值保持兼容；
   - `PixelRect` 取代 `RECT` / `Rectangle`。
5. **回归检查**：
   - 主项目单测在 Linux 上逐条与基线比较；
   - 新增 WPF 界面冒烟检查 `Service/WpfSmokeTest.cs`。它只在 `BGI_WPF_SMOKE=1` 时执行，默认关闭，不影响正常启动；运行时在 Wine 下打开主要页面与对话框，捕获未处理异常。

## 4. Linux 依赖与构建

### 运行时依赖（Debian / Ubuntu 包名）

| 用途 | 包 | 缺少时 |
| --- | --- | --- |
| .NET 10 运行时 / SDK | `dotnet-sdk-10.0`（或官方安装脚本） | 无法运行 |
| X11 窗口、截图、输入 | `libx11-6`、`libxext6`（MIT-SHM / SHAPE）、`libxtst6`（XTest）、`libxi6`（XInput2）、`libxcomposite1`、`libxfixes3` | 无法运行；环境页会显示缺少的扩展 |
| 中文字体 | `fonts-noto-cjk` | 界面中文显示为方块 |
| 网页设置页、HTML 遮罩、云原神窗口 | `libwebkit2gtk-4.1-0` | 这些功能提示安装，其余功能正常 |
| 遮罩透明 | 任意合成器，例如 `picom`（xfwm4、KWin、Mutter 自带合成也可） | 遮罩透明部分显示为黑色，日志里有警告 |
| 剧情语音播放 | `libpulse0`（PulseAudio 或 pipewire-pulse） | 静音输出，流程照常 |
| 系统语音（可选） | `espeak-ng` | 引擎列表中不出现 espeak-ng |
| 系统通知（可选） | `libnotify-bin`（notify-send），或 `gdbus` | 不发系统通知 |
| 剪贴板（可选） | `xclip` 或 `xsel`（Wayland 会话用 `wl-clipboard`） | 复制日志等操作不可用 |
| 游戏本体 | Wine / Proton（窗口须为 X11 或 XWayland 窗口；niri 下经 xwayland-satellite），或者云原神网页版 | — |

Wayland 会话（目前以 niri 为目标）的额外依赖与做法见下一节。

### Wayland（niri）

启动时按环境选择后端：`BGI_LINUX_BACKEND=x11|wayland` 优先；否则 `WAYLAND_DISPLAY` 非空或 `XDG_SESSION_TYPE=wayland` 时用 Wayland 后端，
其余情况与之前完全相同（X11 后端，行为不变）。代码在 `BetterGenshinImpact.Platform.Linux/Wayland/`。

Wine / Proton 里的游戏在 niri 下是经 xwayland-satellite 的 X 客户端，所以 Wayland 后端是混合实现：

| 功能 | X 窗口（Wine 游戏，经 xwayland-satellite） | 原生 Wayland 窗口 | 依赖 / 权限 |
| --- | --- | --- | --- |
| 找窗口 | X11（satellite 不提供 `_NET_CLIENT_LIST`，退回 XQueryTree）+ niri IPC 按标题 / app_id 对应 | niri IPC（`$NIRI_SOCKET`，与 `niri msg --json windows` 相同） | niri |
| 焦点 / 前台判断 | niri IPC `FocusWindow` / `is_focused` | 同左 | niri |
| 截图 | 启动页“截图模式”在 Wayland 下列出 XGetImage 窗口（推荐，默认）/ Composite 窗口图像 / ScreenCast 门户；配置里的 X11 默认值 `ShmRoot` / `GetImageRoot` 自动按 `GetImageWindow` 处理，不用改 `linux-host.json` | xdg-desktop-portal ScreenCast + PipeWire（没有 X 显示时只有这一项） | `gstreamer1.0-pipewire`、`gstreamer1.0-tools`、`gstreamer1.0-plugins-base`；niri 用 `xdg-desktop-portal-gnome` |
| 键鼠输入 | XTest（satellite 内的 Xwayland 收到，与 X11 相同） | uinput 虚拟键鼠（相对移动 + 0..65535 绝对坐标） | `/dev/uinput` 可写 |
| 全局热键 / 键状态 | evdev 直接读 `/dev/input/event*`，不依赖合成器；读不到时退回 X11 热键（只在 X 窗口有焦点时有效） | 同左 | 用户在 `input` 组 |
| 屏幕位置 | niri IPC：输出坐标 + `tile_pos_in_workspace_view` + `window_offset_in_tile`，乘输出缩放 | 同左 | 见下方“限制” |
| 遮罩 | Avalonia 遮罩内容离屏渲染成位图，提交到 wlr-layer-shell overlay 层（`LayerShellOverlay`：透明、空输入区域点击穿透、放在游戏所在输出上） | 同左 | 合成器支持 `zwlr_layer_shell_v1`（niri 支持） |

ScreenCast portal：第一次截图时 portal 弹出选择对话框（可选窗口或显示器），请求带 `persist_mode=2`，返回的 `restore_token`
存到 `$XDG_CONFIG_HOME/bettergi/screencast-restore-token`（默认 `~/.config/bettergi/`），之后直接恢复、不再询问；删掉这个文件即可重新选择。
请求的来源类型会先与 portal 的 `AvailableSourceTypes` 取交集（xdg-desktop-portal-wlr 只支持显示器，带“窗口”会被拒绝）。
PipeWire 帧通过 `gst-launch-1.0 pipewiresrc … ! videoconvert ! BGRA ! fdsink` 子进程读取，这样不用在 C# 里实现 SPA 格式协商和 DMA-BUF 映射；
代价是多一个进程和一次拷贝（1080p 约 8 MB/帧）。pipewiresrc 必须带 `media.type=Video` 等流属性，否则 WirePlumber 0.5 报 `target not found`。
在对话框里选“整个显示器”时（`source_type=1`），按流的 `position` 找到对应的 niri 输出，帧按输出缩放取物理分辨率，
再按 niri 报告的游戏客户区位置（每 500 ms 刷新）裁剪成游戏画面；游戏不在共享的显示器上时不出帧，并提示重新选择。
游戏平铺、niri 不报告位置时，每 2 秒用游戏 X 窗口本身的画面（XGetImage）在整屏帧里做模板比对定位（先缩小 4 倍粗定位，再原分辨率精定位，相关系数 ≥ 0.9）。
Portal 截的是合成后的画面，遮罩（layer-shell 覆盖层，namespace `bettergi-mask`）也会被截进去；同时用遮罩和 Portal 时请在 niri 配置里加
`layer-rule { match namespace="^bettergi-mask$"; block-out-from "screencast"; }`，或者用 XGetImage 窗口截图。
临时抓帧（音游引导等）不用 Portal（每次都要新建会话），X 窗口改用 `GetImageWindow`。Wayland 下遮罩是 layer-shell 覆盖层，不会被 X 窗口截图截到，所以不再像 X11 那样在启用遮罩时改用 Composite。

与 X11 后端逐项对照（Wayland 下的做法）：

- **窗口变化 / 失焦**：除了 X `ConfigureNotify`，还订阅 niri IPC 的 `EventStream`（独立连接，断开后每 2 秒重连）；
  游戏窗口的 `WindowLayoutsChanged` / `WindowOpenedOrChanged` / `WindowFocusChanged` / `WorkspaceActivated` 等事件触发 `ViewportChanged`，
  前台判断与位置换算用的窗口 / 输出列表按事件失效缓存（事件流在线时最长 1 秒，否则 100 ms），不再每次调用都连一次 niri。
- **DPI 缩放**：输出缩放 ≠ 1 时 satellite 把 X 窗口按缩放显示，位置统一按 niri 逻辑坐标 × 缩放换算；遮罩的缓冲缩放 = 输出缩放向上取整，文字按物理分辨率渲染。
- **光标锁定 / 相对鼠标**：游戏是 X 客户端，视角转动走 XTest 相对移动，经 satellite 与 X11 相同（驱动 e2e 测试覆盖锁定指针下的相对移动）。
- **快捷键录制**：设置页录制靠窗口自己的按键事件（经 satellite），与 evdev 无关；录制期间 evdev 全局热键的回调被抑制，
  避免按下要录的组合键时同时触发旧的动作。快捷键页说明写明当前是 evdev 还是退回了 X11，以及原因（如不在 `input` 组）。
- **Portal 授权失败**：保存的 `restore_token` 失效（对话框被拒、显示器已拔掉等）时自动作废 token 并重新询问一次；
  用户在对话框里取消或超时时给出明确提示（再次启动截图器会重新弹出对话框，或改用 XGetImage 窗口截图），不会卡住；
  PipeWire 流中途断开（读取进程退出）时不返回旧帧，自动重连（最多 3 次，间隔 ≥ 5 秒）；重新询问、断开 / 重连、显示器选错、拿不到窗口位置都会写进日志（警告“截图（ScreenCast 门户）：…”）。
- **uinput / input 组权限**：缺少时环境页、日志、快捷键页给出下面的命令。

权限（缺少时环境页 / 日志会给出同样的提示）：

```bash
sudo usermod -aG input $USER                      # evdev 全局热键；重新登录生效
echo uinput | sudo tee /etc/modules-load.d/uinput.conf
echo 'KERNEL=="uinput", GROUP="input", MODE="0660", OPTIONS+="static_node=uinput"' | sudo tee /etc/udev/rules.d/60-bettergi-uinput.rules
sudo modprobe uinput && sudo udevadm control --reload && sudo udevadm trigger
```

限制与取舍：

- **niri 只报告浮动窗口的位置**：平铺窗口的 `tile_pos_in_workspace_view` 为 null（只给出列 / 行序号），无法换算成屏幕坐标；
  查过 niri 主分支（2026-11）的 IPC：新增了 `EventStream` 的 `WindowLayoutsChanged`、`Casts`、`ScreenshotWindow` 等，但平铺窗口仍然没有屏幕位置。
  游戏全屏（窗口尺寸等于输出）时按输出原点处理；Portal 整屏截图时用画面比对兜底（见上）；遮罩和 uinput 绝对坐标拿不到位置时不工作，
  遮罩会在日志里提示。窗口化运行时请让游戏浮动，例如在 niri 配置（`~/.config/niri/config.kdl`）里加：

  ```kdl
  window-rule {
      match title="^原神$"
      match app-id="UnityWndClass"
      open-floating true
  }
  ```
  niri 也可能调整窗口大小（平铺列宽、浮动窗口初始尺寸），截图和坐标换算都以游戏自己的客户区为准。
- **遮罩**：Avalonia 没有 wlr-layer-shell 支持，X11 遮罩窗口经 satellite 会变成普通的平铺窗口，无法覆盖在游戏上。
  评估过的方案：(1) 直接按 Wayland 线协议实现一个 layer-shell 覆盖层（不依赖 libwayland / GTK，几百行），由遮罩模型推送内容；
  (2) GTK + gtk-layer-shell 写遮罩，需要再引入一套 UI 依赖；(3) 等 Avalonia 原生 Wayland 后端，时间不确定。
  采用 (1)：`LayerShellOverlay`（overlay 层、`keyboard_interactivity=none`、空输入区域 → 点击穿透、ARGB8888 预乘、双缓冲）。
  Wayland 会话下 `MaskWindowController` 不显示 X 遮罩窗口，而是把遮罩窗口的内容（识别框 / 线 / 文字、日志框、状态文字，和 X11 遮罩是同一套控件）
  在 UI 线程上用 Skia 离屏渲染（`RenderTargetBitmap`），整幅位图拷进 wl_shm 缓冲区提交（`WaylandMaskSurface`），每 500 ms 跟随窗口，绘制内容变化时立即重画。
  输出：取 niri 报告的游戏窗口所在输出（`wl_output` v4 的 `name` 与 niri 输出名相同），找不到时取包含窗口中心的输出；
  位置换成相对该输出左上角的逻辑坐标；缓冲缩放 = 输出缩放向上取整（分数缩放由合成器缩小），换输出时重建 layer surface。
  拿不到窗口位置（平铺窗口）时遮罩不显示，并在日志里提示把游戏设为浮动或全屏。
  合成器不支持 layer-shell 时日志警告一次、遮罩不显示，其它功能不受影响。HTML 遮罩（WebKitGTK）仍只支持 X11。
- **遮罩随游戏焦点显示 / 隐藏**（X11 与 Wayland 相同，`MaskFocusPolicy`）：规则与 WPF `MaskWindowHost.DecideVisibleLocked` 一致——截图器未运行隐藏；
  游戏在前台显示；前台属于 BetterGI 自身（主窗口、对话框、遮罩本身）或游戏进程、或没有前台窗口时保持原状；其他进程在前台隐藏。
  焦点每 100 ms 判断一次（X11：`_NET_ACTIVE_WINDOW`，没有窗口管理器时 `XGetInputFocus`；Wayland：niri 窗口 `is_focused`），niri 的 `WindowFocusChanged` 事件立即判断。
  WPF 没有防抖，这里加了防抖：隐藏前需稳定 300 ms、重新显示前稳定 120 ms，焦点短暂切走再回来不闪烁。
- **遮罩外观与 WPF 一致**（X11 与 Wayland 相同，`MaskWindow.ApplyOverlay` / `MaskLogView` / `MaskLogSink`）：状态栏和日志框的位置大小按 `MaskWindowConfig` 比例 × 遮罩大小
  （最小 160×24 / 240×80），圆角 4，背景、边框、阴影、文字颜色与透明度取自配置；日志框内外两层背景同 WPF。字号按 `ComputeEffectiveSize`（1080P 比例、显示缩放、`LogFontScale`）。
  日志是 Information 及以上、`[HH:mm:ss INF] 消息` 格式、Serilog Literate 配色，最新一行贴底；字体按 `LogFontFamily`（`monospace` 换成已装的等宽字体，中文回退系统字体），
  行距取首选字体行距（中文回退字形不撑高行，同 WPF）。状态栏五项（拾取 / 剧情 / 邀约 / 钓鱼 / 传送）按开关着色。识别文字用 MiSans。
  已知差异：Avalonia 遮罩日志里多几条启动信息（截图器、ONNX），WPF 在 Wine 下状态图标显示为方框（Wine 字体问题，原生 Windows 正常）。
- **遮罩布局编辑**（X11 与 Wayland 相同，`MaskWindow.Edit.cs`）：设置页“启用遮罩窗口”下的“启用拖拽调整位置大小”（`OverlayLayoutEditEnabled`，启动时关闭，同 WPF）。
  编辑模式下遮罩强制显示（BetterGI 在前台也显示），状态栏与日志框显示半透明白底和虚线框、中间显示“当前处于编辑模式”；拖动内部移动、拖动边缘 6 DIP 以内改变大小
  （最小尺寸同上），松开后按“位置 / 遮罩大小”写回比例；右键控件退出编辑并激活游戏窗口；“重置位置”恢复默认比例。
  输入区域只含这两个控件，其余部分仍点击穿透（同 WPF 透明部分不接收鼠标）：X11 用 SHAPE 输入区域（`X11Overlay.SetInputRegion`），
  Wayland 覆盖层设置 `wl_surface.set_input_region`，从 `wl_seat` / `wl_pointer` 读指针事件（编辑模式下每 15 ms 读一次），光标形状用 `wp_cursor_shape_v1`（合成器不支持时为默认光标）。
  编辑不需要键盘，所以覆盖层仍不接收键盘（`keyboard_interactivity none`）。
- **地图点位**（进行中）：点位服务（米游社 / 空荧酒馆 / HoYoLAB 三个数据源、文件缓存 `Cache/Files`）与界面模型 `MaskWindowViewModel.MapPoints.cs`、点位信息弹窗模型与 WPF 共用，在 Linux 宿主注册；选择的标签和隐藏的点位存到 `User/MapMask`（同 WPF）。界面模型只依赖“回到界面线程”的钩子（WPF 为 Dispatcher，Linux 为 Avalonia 的 UI 线程），弹窗位置与图片由各自界面提供。遮罩上的绘制与交互在后续提交。
- xwayland-satellite 会给没声明“无装饰”的 X 窗口加一条标题栏，niri 报告的窗口比游戏客户区高；屏幕位置按客户区大小对齐到窗口底部。
- 不支持合成器以外的焦点切换方式（目前只实现 niri IPC）；其它 Wayland 合成器上截图 / 输入 / 热键可用，但找窗口位置和聚焦会退化。
- 实机检查清单见 `wayland-niri-checklist.md`。


### 构建

```bash
# 1. 自行编译 OpenCvSharpExtern（含 SIFT/SURF、dnn，静态链接，只依赖 glibc ≥ 2.27），产物在 opencv-build/out/
Build/Linux/build-opencvsharp.sh          # 详见 Build/Linux/README.md；无编译器时脚本自动下载工具链，无需 root

# 2. 编译整个解决方案（Windows 目标项目需要 EnableWindowsTargeting，Linux 上只用于编译和单测）
dotnet build BetterGenshinImpact.sln -p:EnableWindowsTargeting=true

# 3. 运行（素材、模型从 Windows 主程序的输出目录读取）
dotnet BetterGenshinImpact.Avalonia/bin/Debug/net10.0/bgi-avalonia.dll --assets BetterGenshinImpact/bin/x64/Debug/net10.0-windows10.0.22621.0
dotnet BetterGenshinImpact.Host.Headless/bin/Debug/net10.0/bgi-headless.dll --check-opencv
```

找不到自行编译的 OpenCvSharpExtern 时，会退回 `Sdcb.OpenCvSharp4.mini.runtime.linux-x64`。这种情况下 dnn 可用，但没有 features2d，大地图特征匹配不可用；构建输出会提示当前用的是哪一种。

## 5. 与 Windows 的已知差异

| 项目 | Windows | Linux |
| --- | --- | --- |
| 后台输入（PostMessage） | 支持 | 原生 Linux 没有对应机制：游戏在前台时改走前台通道，不在前台时记录告警并忽略 |
| 截图 | BitBlt / DWM / WGC，HDR | `ShmRoot`（默认）/ `GetImageRoot` / `GetImageWindow` / `CompositeWindow`（不受遮挡和遮罩影响）；不支持 HDR |
| 推理设备 | DirectML / CUDA 等 | CPU，或 AMD 显卡 ROCm：直接用系统已装、带 MIGraphX 的 onnxruntime（如 Arch 的 `onnxruntime-rocm`），设置页“硬件加速”选择、重启生效，失败自动回退 CPU 并提示原因，见 [rocm-checklist.md](rocm-checklist.md)；配置里的 Windows 专有引擎在 Linux 上按 CPU 运行（只在本次运行生效，不写回配置） |
| 鼠标绝对移动 | — | Wine 会把绝对移动转换成原始输入并转动视角，上层按 `AbsoluteMoveAffectsCamera` 处理 |
| TTS | 与平台无关的 GPT-SoVITS / HTTP 引擎（上游已移除 SAPI） | 同左，另有 espeak-ng |
| 网页视图 | WebView2 | WebKitGTK 4.1（自定义协议 `bgi-settings://`、`bgi-mask://`、`bgi-repo://`，内容安全策略与 Windows 相同） |
| 通知 | Windows 通知 | 系统通知（notify-send / D-Bus）；Webhook、企业微信、邮件相同；微信 Clawbot 未接入 |
| 凭据（任务计划账号） | 凭据管理器 | 系统密钥环（freedesktop Secret Service：GNOME Keyring / KWallet / KeePassXC），D-Bus 会话加密传输；没有密钥环时明确提示，不以明文保存；密钥环锁定时由密钥环弹出解锁对话框，2 分钟无人操作则停止（保存 / 切换）并提示，界面不卡住 |
| 账号切换 | 支持（Win32 输入 + YuanShen 进程） | 切换流程在 Core，与 Windows 相同；运行环境检查为本机 XTest / uinput 输入 + 窗口进程（X 窗口 `_NET_WM_PID`，niri 原生窗口为 IPC pid）是 Wine / Proton 下的 YuanShen.exe；云原神仍不能切换（只读核验 UID） |
| Shell 任务 | cmd.exe | /bin/sh（独立任务的 Shell 命令经标准输入交给 `/bin/sh -s`）；任务计划里 Windows 上创建的 `cmd.exe [/d /s /q] /c 命令` 改用 `/bin/sh -c 命令` 运行并记日志，其他程序原样启动 |
| 多实例派发 | 支持（桌面分身子会话、云原神实例） | 与 Windows 相同的实例 IPC（WPF 项目里与平台无关的实例服务源文件由 Host.Common 直接编译）：第一个实例取根实例锁（`$TMPDIR/CoreFxPipe_BetterGI.v2.user-uid-<uid>.root.lock` 的独占 flock）并监听根管道（Unix 域套接字，仅本用户）；同一 Unix 会话里再次启动时把参数转发给它并退出（激活主窗口）；在另一 Unix 会话里启动（如 `setsid bgi-avalonia --display :2`，通常再配独立的 Wine 前缀运行另一个游戏）的是子实例（“桌面分身”），任务计划页“执行实例”可把已保存的计划整次下发给它执行并回报状态；对端进程取自 `SO_PEERCRED`，会话为 `getsid`。没有云原神子实例与桌面分身鼠标转发（每个实例有自己的显示和输入）；`--no-instances` 关闭；跨实例的账户占用 / 在线标记 / 写锁 / 脚本仓库资源锁以独占 flock 互斥（Windows 不变） |
| 按键宏（不识图） | 按键宏页：列表 + 指令编辑 / 步骤视图，键鼠监听或全局热键触发 | 同一套脚本解析、执行器与文件存储（WPF 项目里的源文件由 Host.Common 直接编译，宏文件同在 `User/BgiMacro`）；Avalonia 按键宏页只有指令编辑（带语法检查与指令参考），没有步骤视图；单次 / 按住循环 / 切换循环相同；键鼠监听用本机全局输入监听（含鼠标侧键），全局热键用 XGrabKey（Ctrl+Alt+F1–F12 为切换虚拟终端，不可用）；精确等待用睡眠加最后 1 ms 自旋代替 Windows 高精度可等待计时器；删除移到 freedesktop 回收站（`$XDG_DATA_HOME/Trash`） |
| 脚本仓库 | 仓库窗口（渠道 / 自定义地址、Git 凭据、更新 / 重置、zip 导入、打开仓库网页、一键更新订阅）；仓库网页（WebView2）订阅脚本；任务计划远端仓库同步；启动时自动更新订阅、提示未恢复的脚本数据备份 | 同一套仓库服务（WPF 项目里的 `ScriptRepoUpdater`、`RepoWebBridge`、仓库管理服务等源文件由 Host.Common 直接编译，窗口与凭据放到各平台的分部文件）；Avalonia 仓库窗口文字与 Windows 相同，入口在 JS 脚本 / 地图追踪 / 录制回放页、七圣召唤卡组旁与调度器菜单；仓库网页用 WebKitGTK，经自定义协议 `bgi-repo://bettergi.local/` 加载同一份网页，页面里的 `chrome.webview.hostObjects.repoWebBridge` 由注入脚本转成 `POST /__bridge/<方法>` 交给同一个 `RepoWebBridge`；Git 凭据存入系统密钥环（条目名与 Windows 凭据管理器相同）；外部链接与打开目录用 `xdg-open`；临时文件服务与 Windows 相同（独占 flock 租约） |
| 悬浮小窗 / 字幕浮窗 | 看攻略视频的悬浮小窗（WebView2）、字幕浮窗，跟随游戏窗口、任务期间隐藏 / 暂停、热键 | 支持：同一个 `GuideWindowService`、游戏窗口跟随、小窗与字幕 ViewModel、播放桥的页面脚本与消息解析（WPF 项目里的源文件直接编译，计时器 / 显示器 / 前台窗口 / 剪贴板 / 提示等放在各自平台的部分类，Windows 部分原样移入 `*.Wpf.cs`）；网页为 WebKitGTK 4.1（NativeWebView，页面脚本在文档创建时注入，`chrome.webview.postMessage` 由垫片转给 WebKit 消息处理器；只允许 http / https，新窗口在当前页打开，权限请求一律拒绝）；无边框置顶窗口，鼠标穿透用 X SHAPE 输入区域；设置页可打开 / 隐藏 / 关闭 / 恢复，快捷键页有“悬浮小窗”分组。与 Windows 的差别：原生网页控件之上不能叠加控件，窗口栏显示时占网页上方一行、热键反馈与进度线在网页下方；拖动与缩放由窗口管理器完成；没有 WebKitGTK 时设置页提示安装；快捷键页没有“一键填入默认快捷键” |
| 插件（上游插件系统） | 加载外部插件，有插件页、OCR 模型选择 | 只使用 Core 中的内置实现（PP-OCRv6、内置传送），不加载外部插件，没有插件页和 OCR 模型选择界面 |
| 界面 | WPF 全部页面 | Avalonia 覆盖启动、实时触发、独立任务（含专属参数编辑器）、一条龙、全自动、任务计划、设置、通知、环境等；少量窗口（例如部分名单编辑）尚未迁移，见 phase4-avalonia.md |

## 6. 已知问题

- **`MoveAvatarHorizontallyTask` 没有超时**（与平台无关，Windows 上同样存在）：自动秘境寻找石化古树时，如果角色贴住障碍物、树始终不在画面中央，
  会一直按住 A / D 不结束。在假游戏上复现过一次，卡了约 6 分钟。为保持 Windows 行为不变，本分支没有修改，建议上游另行加超时。
- **测试基线里本来就有的失败**（合并前后都一样，不是本分支引入的）：
  - Core 单测 293 项，其中 6 项失败，都是 `AutoFishingImageRecognitionTests.GetFishBarRect_ShouldMatchRectCount`。在 Linux 上与原始基线一致。
  - 主项目单测在 Linux 上原生运行（去掉 WindowsDesktop 框架）时，约 1120 项中有约 246 项失败。原因是依赖 Windows 路径分隔符、WinRT OCR、
    SAPI、Windows 字体与截图素材路径（反斜杠）等。比较方式是逐条与迁移前的基线对照，要求“没有新增失败”，而不是全部通过。
- Wayland 只针对 niri 实现并在盒子上的嵌套 niri / 模拟 portal 中验证过（见 `wayland-niri-checklist.md`），HTML 遮罩（WebKitGTK）只支持 X11；HDR 截图不支持。
- 自动秘境、一条龙等流程在 Linux 上只用假游戏和云原神网页版的本地模拟验证过，没有在真实账号上长时间运行。

## 7. 测试方法与结果

| 检查 | 方法 | 结果（最终代码） |
| --- | --- | --- |
| 编译 | `dotnet build BetterGenshinImpact.sln -p:EnableWindowsTargeting=true` | 0 错误 |
| Core 单测 | `dotnet test Test/BetterGenshinImpact.Core.UnitTest`，逐条与基线对照 | 287 通过 / 6 失败（基线已有），无回归 |
| 主项目单测 | Linux 原生运行，逐条与迁移前基线对照 | 无新增失败 |
| 平台层端到端 | 独立驱动程序 + 两个“假游戏”（Wine 版与 X11 原生版，把收到的输入写进日志、把内部状态编码进画面）：窗口查找、截图、XTest 键鼠（含相对移动）、热键、键状态、原始鼠标 | 全部通过 |
| WPF 冒烟 | Wine 下以 `BGI_WPF_SMOKE=1` 启动 WPF 主程序，打开主要页面、卡片与对话框 | 通过 |
| Avalonia 界面自测 | `bgi-avalonia --ui-test <名称>`（19 组）：任务计划、脚本设置、HTML 设置页 / 遮罩、自动配音、独立任务编辑器、快捷键、通知渠道等 | 在各自的里程碑提交上通过 |
| 业务流程 | 假游戏世界模式（小地图取自真实地图素材，并模拟大地图、传送、对话、秘境、石化古树、首领、树脂）上无人值守运行 | 一条龙 8 项（邮件、合成、秘境、首领、幽境危战、地脉花、派遣、尘歌壶）全部完成，秘境浓缩 / 原粹树脂扣减与首领树脂预检正确 |

假游戏和测试驱动属于本地测试工具，不在本分支里。

## 8. 同步上游

### 8.1 已做的同步（2026-10-31）

1. **合并 `refactor/20260930`（43eecc21，8 个提交）**。上游新增了插件系统、OCR 可插拔（只内置 PP-OCRv6）、`ITpTask` / `ITpTaskFactory`，以及 XAML 设计器主题。15 个文件冲突，解法见 8.3。
2. **合并 `main`（94c1de65）**。`refactor/20260930` 不含 `main` 上的 2 个修复，这次一并合入：
   - 8383aa58：任务启动、激活游戏窗口后释放所有按键，避免鼠标抬起事件落到 BGI 界面弹出菜单；
   - 94c1de65：扩大自动地脉花交互选项的 OCR 区域。

   两个文件都已移到 Core，修改按新位置应用。
3. **上游新功能在 Linux 上的情况**：
   - 可插拔能力（OCR 模型、传送）在 Linux 宿主中正常注册：`GameHost` 只扫描 Core 程序集，自动使用内置实现（PP-OCRv6、内置传送）。
   - 外部插件的加载器 `PluginManager`、插件页、插件设置窗口依赖 WPF，**Linux / Avalonia 上暂不加载外部插件，也没有插件页和 OCR 模型选择界面**。
     目前只有一个内置 OCR 实现，所以不影响使用。

### 8.1.1 第二次同步（2026-11-01）

1. **合并 `refactor/20260930`（43eecc21..0307ec5b，6 个提交）**：键鼠宏（KeyMacro）、角色宏配置窗口，以及 Pulonia 的“实例 / 账户 / 路线 CD 统筹设计”
   （完成记录按账户保存在 `User/Pulonia/accounts/<账户>/`，计划只指定一个账户 `account_id`，计划格式升到 4，触发器按实例，
   运行状态按实例保存在 `instances/<实例>/state.json`，账本与远程派发改为 `PuloniaRunDispatcher`，每个实例各自调度、只有根实例注册快捷键）。
   `main` 这次未合并：合并时本地的 `origin/main` 只跟踪到 94c1de65（远程跟踪只配置了 refactor 分支），之后核对 `main` 已有 2 个新提交，留待下次同步：
   - 3ba5886c：战斗策略的秒数参数按固定小数点解析（修复 pt-BR 等逗号小数点区域下 4.5 被读成 45）；
   - 07d38b31：修复国内网络下推理运行库下载时 Catalog 404。
2. **冲突解法**：59 个冲突，原因是本分支已把 WPF 的 Pulonia 代码抽到 Core，而上游同时重写了这部分。
   - WPF 一律以上游为准（取上游版本），行为与上游相同；上游新增的非界面服务（账户服务、实例键、完成记录索引、路线 CD 等）放进 Core，命名空间不变。
   - 本分支抽到 Core、WPF 不再使用的展示逻辑（运行项、运行预览行、CD 显示文字）改为按新模型只供 Avalonia 使用。
   - Core 调度器 `PuloniaTriggerScheduler` 增加 `registersHotkeys` 参数，WPF 按上游只在根实例注册快捷键。
3. **Avalonia / Linux**：`GameHost` 注册主实例的 `IPuloniaInstanceKeyProvider` 与 `IPuloniaAccountService`（Linux 只有主实例，没有多开与远程派发）；
   任务计划页的路线 CD 标记、CD 记录列表与清除改为按账户的完成记录（计划指定账户 → 本实例当前账户 → 第一个账户，与 WPF 相同）。
4. **数据格式**：上游开发期不转换旧格式，旧版计划（格式 3）、旧账户设置和根目录的 `state.json` 读不出，需要删除后重建。

### 8.1.2 第三次同步（2026-11-01）

1. **合并 `main`（94c1de65..07d38b31，2 个提交）**，无冲突（两处源文件都已在 Core，git 按改名自动应用到新位置）：
   - 3ba5886c：战斗策略的秒数参数按固定小数点解析。`CombatCommand` 在 Core，Linux / Avalonia 与 Windows 共用同一实现，修复在 Linux 上同样生效；
     新增的 `CombatCommandCultureTests` 在 Linux 上通过。
   - 07d38b31：推理运行库下载时 Catalog 404 改走 NuGet V2 元数据。`OnnxRuntimeInstaller` 在 Core，但只有 WPF 的硬件加速页使用，Linux 不下载推理运行库，不受影响。Core 项目没有 WPF 项目的 `System.Net` 隐式引用，合并时为 `HttpStatusCode` 补了 `using System.Net;`。
2. 本地 `origin` 远程跟踪补上了 `main`（`+refs/heads/main:refs/remotes/origin/main`），之后 `git fetch origin` 会同时更新两个分支。

### 8.1.3 第四次同步（2026-11-02）

1. **合并 `refactor/20260930`（0307ec5b..9e3a0b00，2 个提交）**；`main` 自 07d38b31 以来没有新提交。
   - ad10c366：移除火山翻译、Google 翻译、Windows SAPI 三个自动配音渠道。这些引擎与设置 VM 本分支已移到 Core，按上游一并删除；
     WPF 模块初始化不再注册 SAPI，Avalonia 自动配音设置窗口删除三个模板。Core 注册表保留本分支的宿主注册机制（Linux 的 espeak-ng 仍可用），
     并加入上游的 `IsRegistered`（包含宿主注册的引擎）：旧配置选中已移除引擎时回退 GPT-SoVITS。
   - 9e3a0b00：攻略小窗默认自由位置、四项常用操作默认绑定方向键（键鼠监听），删除“小窗隐藏时继续显示字幕”与其余小窗快捷键；按键宏新增绝对坐标等指令。
     小窗快捷键当时只在 WPF；Avalonia 删除“小窗隐藏时继续显示字幕”开关。按键宏后来已迁移（见第 5 节“按键宏”）：键名改用 Core 的 `GameKey`，`KeyMacroRunner` 的截图区域直接用 `PixelRect`。
2. 冲突 9 处（6 处是上游删除、本分支移到 Core 的文件，按上游删除；3 处内容冲突按上面方式合并），WPF 行为与上游相同。

### 8.2 同步步骤

```bash
git fetch origin
git log --oneline HEAD..origin/refactor/20260930     # 看新增提交
git log --oneline HEAD..origin/main                  # main 上 refactor 分支还没有的修复（远程跟踪需包含 main，见 8.1.2）
git merge --no-ff origin/refactor/20260930           # 普通合并，不 rebase
#   解决冲突（见 8.3），然后：
dotnet build BetterGenshinImpact.sln -p:EnableWindowsTargeting=true   # 0 错误
dotnet test Test/BetterGenshinImpact.Core.UnitTest                    # 与基线对照，无新增失败
#   主项目单测、平台层 e2e、WPF 冒烟（Wine）、Avalonia 界面自测、假游戏一条龙：见第 7 节
git commit                                           # 合并提交里写清冲突与适配
git merge --no-ff origin/main                        # main 有 refactor 没有的修复时再合并一次
```

### 8.3 常见冲突与解法

| 情况 | 现象 | 解法 |
| --- | --- | --- |
| 上游修改了已移入 Core 的文件 | 多数由 Git 的重命名检测自动合并到 `BetterGenshinImpact.Core/...`；检测不到时报 “modify/delete”（旧路径在本分支已删除） | 把上游的修改按新路径手工应用到 Core 中的文件，旧路径取删除（`git rm`）。合并时加 `-c merge.renameLimit=20000` 可以提高重命名检测率 |
| 上游新增文件放在 WPF 主项目，但被 Core 中的代码使用 | 编译报 Core 中找不到类型 | 把这些文件移到 Core 的相同相对路径（命名空间不变）。`App.GetService` / `App.GetLogger` / `App.ServiceProvider` 改为 `CoreServices` 的对应成员。用到 WPF 的部分留在主项目 |
| 上游在主项目 `GlobalUsing.cs` 中新增 `global using` | Core 中同名类型无法解析 | 在 Core 加对应的 `GlobalUsings.*.cs` |
| XAML 头部 | 上游改了 `UserControl` 属性，本分支在 `clr-namespace` 后加了 `;assembly=BetterGenshinImpact.Core` | 取上游一侧，再补回程序集后缀 |
| 依赖注入注册（`App.xaml.cs`） | 上游新增服务注册 | 主项目照上游注册；扫描程序集的注册表（`SoloTaskRegistry`、`PluggableRegistry`）同时扫描主程序集和 Core 程序集；Core 中的新服务还要在 Linux `GameHost` 中注册 |
| 构造函数签名变化 | Linux 宿主（`Host.Common/GameHost.cs`）或 Avalonia 编译失败 | 按上游新签名修改 Linux 宿主中的注册和调用（例如 `QuestVoiceSessionFactory`、`ITpTaskFactory`） |
| 上游改了平台相关实现（WasapiOut、WebView2、Win32 调用等） | 本分支已改为宿主注入 | 保留注入点，把上游改动放进 Windows 侧的实现 |

