# BetterGI 跨平台 / Linux 迁移说明（fork 自用，定期同步上游）

`avalonia-linux` 是自用 fork（不向上游提交 PR），定期把上游 `refactor/20260930`（以及 `main` 上的修复）合并进来。
本文说明这个分支相对上游做了什么、Windows 版为什么行为不变、Linux 版怎么构建和测试、还有哪些已知问题，
以及以后怎样同步上游（第 8 节）。各阶段的详细记录见同目录的 [phase1-foundation.md](phase1-foundation.md)、
[phase2-progress.md](phase2-progress.md)、[phase4-avalonia.md](phase4-avalonia.md)。

## 1. 概览

- 分支：`avalonia-linux`，从 `refactor/20260930` 切出。自有提交是线性的普通提交，同步上游时使用普通合并提交（不 rebase、不改写已推送的历史）。
- 已同步到：`refactor/20260930` 的 `0307ec5b`（2026-11-01）和 `main` 的 `94c1de65`。
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
| 插件（上游插件系统） | 加载外部插件，有插件页、OCR 模型选择 | 只使用 Core 中的内置实现（PP-OCRv6、内置传送），不加载外部插件，没有插件页和 OCR 模型选择界面 |
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
   `main` 没有新提交（仍为 94c1de65），不需要合并。
2. **冲突解法**：59 个冲突，原因是本分支已把 WPF 的 Pulonia 代码抽到 Core，而上游同时重写了这部分。
   - WPF 一律以上游为准（取上游版本），行为与上游相同；上游新增的非界面服务（账户服务、实例键、完成记录索引、路线 CD 等）放进 Core，命名空间不变。
   - 本分支抽到 Core、WPF 不再使用的展示逻辑（运行项、运行预览行、CD 显示文字）改为按新模型只供 Avalonia 使用。
   - Core 调度器 `PuloniaTriggerScheduler` 增加 `registersHotkeys` 参数，WPF 按上游只在根实例注册快捷键。
3. **Avalonia / Linux**：`GameHost` 注册主实例的 `IPuloniaInstanceKeyProvider` 与 `IPuloniaAccountService`（Linux 只有主实例，没有多开与远程派发）；
   任务计划页的路线 CD 标记、CD 记录列表与清除改为按账户的完成记录（计划指定账户 → 本实例当前账户 → 第一个账户，与 WPF 相同）。
4. **数据格式**：上游开发期不转换旧格式，旧版计划（格式 3）、旧账户设置和根目录的 `state.json` 读不出，需要删除后重建。

### 8.2 同步步骤

```bash
git fetch origin
git log --oneline HEAD..origin/refactor/20260930     # 看新增提交
git log --oneline HEAD..origin/main                  # main 上 refactor 分支还没有的修复
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

