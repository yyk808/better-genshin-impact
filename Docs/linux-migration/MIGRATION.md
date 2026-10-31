# BetterGI 跨平台 / Linux 迁移说明（面向上游维护者）

本文说明 `avalonia-linux` 分支相对 `refactor/20260930` 做了什么、Windows 版为什么行为不变、Linux 版怎么构建和测试、
还有哪些已知问题，以及建议的合并方式。各阶段的详细记录见同目录的 [phase1-foundation.md](phase1-foundation.md)、
[phase2-progress.md](phase2-progress.md)、[phase4-avalonia.md](phase4-avalonia.md)。

## 1. 概览

- 分支：`avalonia-linux`，从 `refactor/20260930` 切出。共 139 个提交，都是线性的普通提交，没有合并提交。
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
   - `TtsProviderRegistry` 的平台引擎（Windows 仍注册 SAPI）；
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
| 游戏本体 | Wine / Proton（窗口须为 X11 或 XWayland 窗口），或者云原神网页版 | — |

纯 Wayland（portal 截图 / 输入）目前不支持。

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
| 推理设备 | DirectML / CUDA 等 | 强制 CPU（只在本次运行生效，不写回配置） |
| 鼠标绝对移动 | — | Wine 会把绝对移动转换成原始输入并转动视角，上层按 `AbsoluteMoveAffectsCamera` 处理 |
| TTS | Windows SAPI | espeak-ng（以及与平台无关的 HTTP 引擎） |
| 网页视图 | WebView2 | WebKitGTK 4.1（自定义协议 `bgi-settings://`、`bgi-mask://`，内容安全策略与 Windows 相同） |
| 通知 | Windows 通知 | 系统通知（notify-send / D-Bus）；Webhook、企业微信、邮件相同；微信 Clawbot 未接入 |
| 凭据（任务计划账号） | 凭据管理器 | 不支持保存凭据（会提示） |
| 多实例派发、账号切换 | 支持 | 未接入 |
| 界面 | WPF 全部页面 | Avalonia 覆盖启动、实时触发、独立任务（含专属参数编辑器）、一条龙、全自动、任务计划、设置、通知、环境等；少量窗口（例如脚本仓库、部分名单编辑）尚未迁移，见 phase4-avalonia.md |

## 6. 已知问题

- **`MoveAvatarHorizontallyTask` 没有超时**（与平台无关，Windows 上同样存在）：自动秘境寻找石化古树时，如果角色贴住障碍物、树始终不在画面中央，
  会一直按住 A / D 不结束。在假游戏上复现过一次，卡了约 6 分钟。为保持 Windows 行为不变，本分支没有修改，建议上游另行加超时。
- **测试基线里本来就有的失败**（合并前后都一样，不是本分支引入的）：
  - Core 单测 293 项，其中 6 项失败，都是 `AutoFishingImageRecognitionTests.GetFishBarRect_ShouldMatchRectCount`。在 Linux 上与原始基线一致。
  - 主项目单测在 Linux 上原生运行（去掉 WindowsDesktop 框架）时，约 1120 项中有约 246 项失败。原因是依赖 Windows 路径分隔符、WinRT OCR、
    SAPI、Windows 字体与截图素材路径（反斜杠）等。比较方式是逐条与迁移前的基线对照，要求“没有新增失败”，而不是全部通过。
- 纯 Wayland 不支持；HDR 截图不支持。
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

## 8. 与上游最新代码的合并情况

用 `refactor/20260930` 最新提交（`43eecc21`）做了合并演练，分支本身没有改动。冲突共 15 个文件，可以分成三类：

1. **9 个 XAML 头部**（设计器主题提交给 `UserControl` 加了 `ui:Design.Background/Foreground`，本分支给 `clr-namespace` 加了 `;assembly=BetterGenshinImpact.Core`）：
   保留上游加的属性，再补回本分支的程序集后缀即可。
2. **OCR 改为可插拔能力**（`OcrFactory`、`PpOcrV6Service`、删除 `PaddleOcrModelConfig`、`CommonSettingsPageViewModel`、`QuestVoiceSessionFactory`）：
   以上游实现为准，`QuestVoiceSessionFactory` 的音频输出保留 `QuestVoiceAudioHost.OutputFactory`。
3. **插件系统**（`App.xaml.cs`）：上游把 `PluggableRegistry` 等放在 WPF 主项目，但 OCR、传送这些可插拔能力的使用方已经在 Core。处理方法：
   - 把 `Core/Plugins`（Hosting 除外）、`PluginConfig`、`ITpTask` / `ITpTaskFactory` / `BuiltinTpTaskFactory` 移到 Core；
   - `App.GetService` 改为 `CoreServices.GetService`，Core 增加 `global using BetterGenshinImpact.Core.Plugins`；
   - `PluginManager` 等插件加载部分（用到 WPF `Application`）留在主项目；
   - `App.xaml.cs` 扫描主程序集与 Core 程序集；
   - Linux 的 `GameHost` 注册 `PluggableRegistry` 及其代理（只扫描 Core，暂不加载外部插件）；
   - Avalonia 中的 `new TpTask(...)` 改为 `Pluggable.Get<ITpTaskFactory>().Create(...)`。

演练结果：编译 0 错误，Core 单测和主项目单测与基线相比没有回归，WPF 冒烟测试通过。如果上游先合并本分支，插件系统可以照上面的做法直接放进 Core。

## 9. 建议的合并方式

整个分支一次性合并也可以（开发时每个里程碑都验证过编译与回归），但 diff 太大不好审阅。建议拆成按顺序叠加的几个 PR，
每个 PR 对应一段**连续的提交**，不需要改写历史：

| 顺序 | 内容 | 提交范围（按 `git log --reverse`） | 说明 |
| --- | --- | --- | --- |
| 1 | 平台抽象地基：Core、Platform.Windows、Platform.Linux，接口去掉 Win32 类型 | 第 1–3 个提交 | Windows 行为不变，Linux 部分在这一步还不会被用到 |
| 2 | 业务移入 Core（识别核心、任务层、脚本、配置、触发器、运行环境） | 第 4–82 个提交 | 以文件移动为主（审阅时建议用 `git diff -M`）；WPF 改为调用 Core |
| 3 | Linux 宿主：Host.Headless、Host.Common、OpenCvSharpExtern 构建脚本 | 第 83–86 个提交 | 只新增项目，不影响 Windows |
| 4 | Avalonia 图形界面以及之后的各里程碑（其中仍有少量 Core 抽取，WPF 改为调用，行为不变） | 第 87–139 个提交 | 可以按里程碑继续细分；每个里程碑的说明见 phase4-avalonia.md |

如果上游更希望历史精简，可以在合并时选 GitHub 的 “Squash and merge”，或者由维护者从本分支的上述节点另建分支，这样不需要改写本分支已推送的历史。
