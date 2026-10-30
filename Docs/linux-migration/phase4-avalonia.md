# Avalonia 宿主进展

对应 [roadmap.md](roadmap.md) 的“阶段 4：Avalonia UI”。项目说明见 `BetterGenshinImpact.Avalonia/README.md`。

## 里程碑 26：共享宿主引导 + Avalonia 宿主骨架

### 共享宿主引导 `BetterGenshinImpact.Host.Common`

无界面宿主的启动代码抽成 `GameHost`，无界面宿主与 Avalonia 宿主共用：

| 类型 | 作用 |
| --- | --- |
| `GameHost` | 创建：X11 平台服务 → `PlatformHost` / `GameSystem` → 内存配置 → 依赖注入 → `CoreServices` / 输入告警日志；`InitializeInferenceAsync`（CPU）；`StartAsync` / `StopAsync` / `TickOnce`；帧数与帧调度异常统计 |
| `GameHostOptions` | 显示、素材目录、进程名、触发间隔、拾取 / 剧情 / 自动开门初始开关；`Apply` 解析两个宿主共用的命令行参数 |
| `HostLogging` | Serilog 控制台 + 文件，宿主可追加输出（Avalonia 追加日志面板） |
| `MemoryConfigService`、`PlatformGameSystemControl`、`OpenCvSelfCheck` | 从无界面宿主移入 |

Linux 原生库（OpenCvSharpExtern、onnxruntime）改在 Host.Common 中引用，随项目引用复制到两个宿主的输出目录。
`UiHost`（UI 线程、消息框、剪贴板）由宿主在创建 `GameHost` 前设置。

### Avalonia 宿主 `BetterGenshinImpact.Avalonia`

- 主窗口：左侧导航（启动、实时触发、独立任务、任务计划、一条龙、全自动；底部环境、设置）+ 内容区 + 底部日志面板 + 轻提示。
- 启动页、实时触发页、环境页已实现，其余导航项为说明页。
- 实时触发页直接绑定 Core 的 `AllConfig`，与 WPF 页绑定同一批配置属性。
  WPF 的 `TriggerSettingsPageViewModel` 依赖 WPF 窗口和导航服务，没有直接复用；名单编辑等窗口尚未迁移。
- 主题：Avalonia 自带 Fluent（原因见项目 README）。
- 中文字体：选系统中第一个可用的中文字体并加入字体回退。

### 验证

- 无界面宿主改用 `GameHost` 后，假游戏拾取 / 剧情场景结果不变。
- Avalonia 宿主在假游戏场景中（`--auto-start`）：
  - 拾取场景：自动拾取识别“甜甜花”，假游戏收到 F 键；
  - 剧情场景：自动剧情先按空格，再点击感叹号选项，假游戏收到空格和点击。
- `--ui-selftest`：后台线程经 `UiHost.Dispatcher` 调度到 UI 线程、剪贴板写入后从后台线程读回一致、轻提示显示、
  后台线程同步调用的消息框截图后以“是”关闭并返回 `Yes`。
- 原版界面对照截图：在 Wine 中运行 WPF 主程序截取。

### 下一步（里程碑 26 时）

1. 启动页补全：截图模式、窗口选择、同时启动游戏（Linux 用 Wine / Proton 启动命令）——里程碑 27 完成。
2. 配置持久化：Linux 上的 `ConfigService`（`User/config.json`）——里程碑 27 完成。
3. 遮罩窗口（透明、置顶、点击穿透）——里程碑 27 完成窗口本身，绘制内容待迁移。
4. 按页面迁移：独立任务 → 一条龙 → 脚本 / JS → 地图追踪 → 热键 → 其它设置页。

### 文件数（按 git 跟踪的 `.cs` 统计，口径同 phase2-progress.md）

Core 972 个，主项目 763 个（本里程碑未移动 Core / 主项目文件）；Host.Common 6 个；Avalonia 宿主 `.cs` 与 `.axaml` 共 23 个（含 `.axaml` 视图 6 个）。

## 里程碑 27：配置持久化 + 启动页补全

### 配置持久化（`Host.Common/Config`）

- `FileConfigService`：Linux 上的 `IConfigService`，读写 `User/config.json`，格式与 Windows 主程序完全相同
  （同一个 `AllConfig`、同一套 `ConfigJson.Options`、读取后执行同样的旧配置迁移）。Windows 主程序生成的配置文件
  拷过来可直接读取，保存后再拷回 Windows 也能读取。
- `JsonFileStore`：通用的防抖原子写入，流程与 Windows 的 `ConfigService` 相同：任意属性改动后 500 ms 内的改动合并为一次写入；
  在 UI 线程上序列化快照（最多等 3 秒）；写临时文件后改名替换；退出时把未写的改动写完；读取失败时把原文件备份到
  `User/backup/` 再用默认配置。
- Linux 专有的设置（截图方式、同时启动游戏的命令等）放在同目录的 `User/linux-host.json`（`LinuxHostConfig`），
  不往 `AllConfig` 里加字段，`config.json` 保持与 Windows 一致。
- 两个宿主共用：Avalonia 宿主默认读写配置文件（`--no-save-config` 关闭）；无界面宿主默认只用内存中的默认配置
  （测试结果可重复），`--save-config` 或 `--config 文件` 打开。命令行开关（`--no-pick`、`--interval` 等）在读取后应用，
  之后保存时会一起写回（里程碑 28 改为只在本次运行有效，见下文）。
- 首次运行（文件不存在）立即写出默认配置，方便查看和编辑。

### 启动页补全（Avalonia）

- 截图模式：MIT-SHM 屏幕区域（默认，最快）、XGetImage 屏幕区域、XGetImage 窗口（可被遮挡，需合成器）；
  保存在 `linux-host.json`，下次启动截图器生效。命令行 `--capture` 可临时指定（两个宿主都支持）。
- 手动选择窗口：列出 X11 顶层窗口（标题、WM_CLASS、进程名和 PID、大小，按进程名找到的游戏窗口加 ★，不列宿主自己的窗口）；
  选中后启动截图器时优先用该窗口，窗口已关闭则退回按进程名查找。只在本次运行有效（窗口句柄每次启动都会变）。
- 同时启动原神：启动截图器时若找不到游戏窗口，按配置的命令启动游戏并等待窗口出现（超时可配置）。命令经 `/bin/sh -c` 执行，
  `{game}` 替换为加引号的游戏路径，`~` 展开为主目录；可设工作目录和附加环境变量（每行 `KEY=VALUE`，`#` 开头为注释）。
  预置命令：Wine、Proton（umu-run）、Steam、Lutris。另有“立即启动游戏”按钮。

### 验证

- 单元测试（Core 单测项目新增 `HostCommon/HostConfigTests`，6 个）：Windows 格式读写一致、文件缺失 / 损坏（备份）、
  防抖合并且不留临时文件、退出时写完、`linux-host.json` 往返、启动命令加引号与环境变量解析。
- 兼容性：用 Wine 中运行原版 WPF 生成的 `config.json` 启动 Avalonia 宿主，在界面上改一个设置后防抖写回；
  与原文件逐项比较，内容完全相同（只有换行符随平台为 LF，Windows 读取不受影响）。
- 手动选择窗口：假游戏改用不会被自动识别的标题和 WM_CLASS，选择对话框列出该窗口，选中后截图器绑定它并持续自动拾取。
- 截图模式 + 同时启动游戏：`linux-host.json` 设为 XGetImage 屏幕区域、启用启动命令（启动假游戏），点“启动”后
  宿主启动假游戏、约 1 秒后找到窗口、以 XGetImage(root) 截图并持续自动拾取。
- 全量回归（编译、Core / 主项目单测对照基线、Wine 与 X11 端到端）无新增失败；无界面宿主拾取 / 对话场景通过。

### 遮罩窗口（预览）

- Avalonia `MaskWindow`：无边框、透明背景、置顶（`_NET_WM_STATE_ABOVE`）、不在任务栏、显示时不抢焦点；
  截图器运行时每 500 ms 跟随游戏画面的位置和大小，停止后隐藏。
- 点击穿透：窗口打开后经 `X11Overlay.SetClickThrough` 把 X11 SHAPE 的输入区域设为空（`XShapeCombineRectangles`，
  ShapeInput + 空矩形列表），鼠标事件直接落到下面的游戏窗口。
- 目前只在左下角显示状态（已处理帧数），识别结果绘制（`MaskWindowDrawingBoard`）、日志框等后续再迁移。
- 默认关闭（`linux-host.json` 的 `maskWindowEnabled`，启动页开关，命令行 `--mask`）。原因：
  - 透明需要合成器；没有合成器时透明部分是黑色，会挡住游戏，宿主会给出警告（检测 `_NET_WM_CM_S0`）；
  - 屏幕区域截图（MIT-SHM / XGetImage 根窗口）会把遮罩上画的内容一起截进去（Windows 上截的是游戏窗口本身，没有这个问题）。
    之后考虑用 Composite 扩展直接读游戏窗口的离屏图像，或只在识别区域之外绘制。
- 验证（假游戏 + picom）：遮罩窗口与游戏画面位置大小一致、带 `_NET_WM_STATE_ABOVE`；在遮罩上点击，假游戏收到对应坐标的点击；
  游戏窗口保持为活动窗口，自动拾取持续工作（每帧发 F）。没有合成器时会出现警告，截图被遮罩挡住，拾取停止（符合预期）。

### 文件数（口径同上）

Core 972 个，主项目 763 个（本里程碑未移动文件；Core 只改了 `PlatformRuntimeProvider`，Windows 不使用）；Host.Common 10 个；
Avalonia 宿主 `.cs` 与 `.axaml` 共 29 个（含 `.axaml` 视图 8 个）；Platform.Linux 新增 `X11Overlay.cs`。

## 里程碑 28：Composite 窗口截图、遮罩绘制、运行时覆盖不写回

### Composite 窗口截图（`X11CaptureMode.CompositeWindow`）

- 首次截图时以自动模式重定向游戏窗口（`XCompositeRedirectWindow(..., CompositeRedirectAutomatic)`，屏幕显示不变），
  之后每帧 `XCompositeNameWindowPixmap` 取窗口的离屏 pixmap，用 MIT-SHM 读取（失败退回 `XGetImage`），读完立即 `XFreePixmap`。
  读到的是窗口本身（含子窗口）的内容：被其它窗口挡住、被遮罩覆盖时仍是游戏画面，也不含窗口管理器的边框。
- 窗口大小变化、最小化后恢复（取消映射再映射后旧 pixmap 不再更新）：每帧重新取 pixmap，SHM 图像按窗口大小和深度重建。
- Wine 窗口：捕获的是 Wine 的顶层 X 窗口，子窗口的内容包含在其 pixmap 中；窗口已被别的客户端（如 Wine 自己）手动重定向时
  重定向请求会失败，但仍可直接取 pixmap。
- 回退：X 服务器没有 Composite 扩展或缺少 `libXcomposite.so.1` 时直接用 MIT-SHM 屏幕区域；窗口未映射时该帧为空，
  连续失败 40 次回退 MIT-SHM 屏幕区域（`LastFallbackReason` 记录原因）。
- 启用遮罩且截图方式是默认的 MIT-SHM 屏幕区域时，自动改用 Composite 窗口图像（`GameHost.EffectiveCaptureMode`）；启动页也可直接选择。
- 验证：
  - 遮罩 + 无合成器：遮罩透明部分在屏幕上是黑色、完全盖住游戏，截图器拿到的仍是干净的游戏画面，拾取正常（里程碑 27 用屏幕区域截图时拾取停止）；
  - 遮罩 + picom：同上，截图器画面里没有遮罩上的识别框、日志框、状态文字（红色像素：屏幕 208 个，截图器画面 0 个）；
  - 无界面宿主 `--capture CompositeWindow`：另一个窗口盖在游戏上、途中把游戏窗口从 1600×900 改成 1280×720，
    截图器画面始终是游戏本身，尺寸随之变为 1280×720；
  - Wine 下的假游戏（`FakeGame.Win.exe`）被另一个窗口遮挡时，截图器画面完整。

### 遮罩绘制

- 宿主注册 Core 的 `MaskWindowDrawingBoard`（`IMaskWindowDrawingBoard` + `IMaskWindowSnapshotSource<MaskWindowDrawingSnapshot>`），
  替换原来的空实现；业务代码（`Region.DrawSelf`、`DrawingBoard.Set` 等）写入的识别框、线、文字由 Avalonia 遮罩绘制。
- `MaskDrawingLayer`（对应 WPF 的 `MaskWindowDrawingLayer`）：渲染快照，坐标从捕获像素按 层宽度 / 捕获宽度 换算；
  `Recognition` 分组受“在遮罩上显示识别结果”控制；统一样式由 `MaskWindowConfig.Recognition*` 合成（与 WPF 相同，颜色文本用 Avalonia 解析）。
  绘制板变化在任意线程触发，合并后在 UI 线程刷新。
- 日志框：最近 6 行信息日志（受 `MaskWindowConfig.ShowLogBox` 控制），状态文字在左下角。
- 调试开关“遮罩上显示全部识别结果”（`linux-host.json` 的 `maskDrawAllRecognition`，命令行 `--mask-recognition`）：
  打开 Core 的进程级开关 `RecognitionObject.ForceDrawOnWindow`，所有有名称的识别对象匹配成功时都画框（例如拾取的 F 键）。
  默认关闭，Windows 主程序不设置，行为不变。
- 脚本 HTML 遮罩（`IHtmlMaskWindowHost`）依赖 WebView，Linux 上仍是“不支持”的默认实现，留到 WebView 迁移时处理。
- 验证（假游戏 + picom + `--mask-recognition`）：拾取时 F 键周围出现红框，日志框显示最近的日志；截图器画面不含这些内容；拾取持续进行。

### 运行时覆盖不写回（`RuntimeOverrides`）

- 读取配置后应用的命令行开关（`--pick` / `--no-pick`、`--skip` / `--no-skip`、`--interval`、`--auto-enter`）和 Linux 上强制 CPU 推理，
  记录覆盖前后序列化结果中变化的节点；保存时仍等于覆盖值的节点换回文件原值，用户之后在界面上改过的照常保存。
  没有其它改动时，保存结果与原文件逐字节相同。
- `linux-host.json` 同样处理：`--capture`、`--mask` / `--no-mask`、`--mask-recognition` 只在本次运行有效。
- 验证：拿 Windows 生成的 `config.json`，以 `--no-pick --interval 30 --capture GetImageRoot --mask` 启动，日志列出两个覆盖节点；
  界面自检改动配置并防抖保存后，文件内容与原文件完全相同，`linux-host.json` 保持默认截图方式和遮罩开关。
- 新增单测 2 个（`RuntimeOverridesAreNotWrittenBack`、`LinuxHostRuntimeOverrideKeepsFileValue`）。

### 文件数（口径同上）

Core 972 个，主项目 763 个（本里程碑未移动文件；Core 只改了 `RecognitionObject`（调试开关，默认关闭）和 `ImageRegion` 的绘制判断）；
Host.Common 11 个；Avalonia 宿主 `.cs` 与 `.axaml` 共 30 个。

## 里程碑 29：独立任务页、一条龙页

### 独立任务页（对照 WPF `TaskSettingsPage`）

- 参数生成抽到 Core：`SoloTaskParamFactory.Create(taskId, config, registry, inputs)`（原 `TaskSettingsPageViewModel.CreateSoloTaskParam`），
  页面临时输入经 `ISoloTaskParamInputs`（继承已有的 `ITaskSettingsSource`）提供；七圣召唤 / 战斗策略检查移到 `TaskStrategyLookup`，
  失败原因经回调提示。WPF 的 ViewModel 改为委托：提示仍是原来的 `Toast`（战斗策略仍经 `UIDispatcherHelper.Invoke`），文案不变。
- `Host.Common` 注册 `SoloTaskRegistry`（扫描 Core；一条龙移入 Core 后也在其中）、`SoloTaskExecutionService`、`AddLocalization()`（参数基类需要 `IStringLocalizer<T>`）、
  空实现的 `ISoloTaskNotificationSuppressor`、`IScriptService`；设置 `RunnerSuspend.Provider` 与 X11 版窗口取色 `X11WindowPixelSampler`（自动音游，1x1 `XGetImage`，失败时按“无音符”处理）。
- Avalonia 独立任务页：与 WPF 相同的卡片顺序、标题与说明；每个卡片有启动/停止按钮（运行中变为红色“停止”）和关键参数
  （伐木次数 / 每日上限、战斗策略、秘境次数与队伍、首领 / 幽境策略、七圣召唤策略、钓鱼截图、音游取色方式、兑换码文本框、背包分类、分解星级），
  其余参数使用配置文件中的值。页面上没有、但注册为独立任务的（领取邮件等）按注册表分组列在后面。
- 启动 / 停止直接调用 `SoloTaskExecutionService`（单实例，与 WPF 单实例时相同：空闲则启动，运行中则停止）；执行层是 Core 的 `TaskRunner`
  （任务信号量、触发器暂停、结束时 `InputHub.ReleaseAll()`）。启动前先 `Flush` 配置，执行层从配置文件读取最新配置。
- 兑换码在卡片上的多行文本框输入（WPF 为弹出输入框）。

### 一条龙

- 依赖拆分（WPF 行为不变）：
  - `OneDragonTaskItem` 移入 Core：去掉没有赋值过的 `StatusColor`（WPF 页面上的圆点直接用 `Gray`，与原默认值相同）和未使用的 `ViewModel` 属性；
    战斗策略经 `ITaskSettingsSource` 解析（WPF 中注册的就是 `TaskSettingsPageViewModel`）。
  - `ScriptControlViewModel.GetNextProjects` / `SetProjectNextFlagFromSchedule` → Core `ScriptGroupSchedule`（ViewModel 委托，仍读写全局配置根）。
  - `IScriptService` / `ScriptService` 移入 Core（命名空间不变）。
  - 新增 `UiHost.Lifetime`（`IAppLifetime`），一条龙“关闭软件”经此调用；WPF 实现 `WpfAppLifetime` 与原来的 `Application.Current.Shutdown()` 调用相同。
  - `OneDragonTask` 移入 Core。
- Avalonia 一条龙页：选择 `User/OneDragon/*.json` 配置（没有时新建“默认配置”，内置任务全部启用）、任务开关（立即保存）、“从此执行”、完成后操作、启动 / 停止；
  参数生成与 WPF `CreateOneDragonParam` 相同（保存配置、读取并清除“从此执行”标记）。各任务的详细设置、增删排序任务与配置的界面尚未迁移。

### 验证

- 无人值守参数 `--solo-task 任务标识 [--solo-stop-after 秒]`：切到独立任务页，点击该任务的启动按钮，到时点击停止并等待结束（截图 `avalonia-solo-running.png` / `avalonia-solo-stopped.png`）。
- 自动音游（假游戏底部按键位颜色 B<220，所有键位立即按住）：6 个键 A S D J K L 按下约 4.4 秒；停止后 5 ms 内全部抬起，假游戏日志中没有仍按住的键；任务结果为 Cancelled。
- 一条龙（默认配置）：执行到“领取邮件”，停止后任务以 Cancelled 结束，没有仍按住的键。假游戏所在显示没有窗口管理器，前台检测认为游戏不在前台，子任务在等待焦点时被取消（与 Windows 上游戏失去焦点时相同的暂停逻辑）。
- 原版 WPF（Wine）与 Avalonia 独立任务页对照截图。
- 回归：Core 单测 275 通过（新增 `SoloTaskParamFactoryTests` 6 个；6 个既有失败不变），主项目单测 833 / 243 / 12 与基线相同，Wine e2e PASS 17，X11 PASS 16 SKIP 1，keystate / rawmouse FAIL 0。

### 文件数（口径同上）

Core 978 个，主项目 759 个（移入 `OneDragonTask`、`OneDragonTaskItem`、`IScriptService`、`ScriptService`；新增 `SoloTaskParamFactory`、`ScriptGroupSchedule`）；
Host.Common 12 个；Avalonia 宿主 `.cs` 与 `.axaml` 共 36 个。
