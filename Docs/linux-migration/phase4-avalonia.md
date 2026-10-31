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

## 里程碑 30：X11 前台检测与窗口激活；一条龙第一个子任务

### 前台检测与激活（`Platform.Linux`）

- 有无窗口管理器：`_NET_SUPPORTING_WM_CHECK` 指向的窗口上同一属性指向自身时认为有窗口管理器。
- 有窗口管理器：前台为 `_NET_ACTIVE_WINDOW`（与目标窗口直接相等或沿父窗口上溯到同一顶层窗口）；激活发 `_NET_ACTIVE_WINDOW` 客户端消息（来源 2），超时后再 `XSetInputFocus`。
- 没有窗口管理器：前台用 `XGetInputFocus`，沿父窗口上溯到根窗口的子窗口后比较；激活用 `XMapRaised` + `XRaiseWindow` + `XSetInputFocus(RevertToParent)`。
  枚举窗口时没有 `WM_STATE`（只有窗口管理器会设置），改为接受带 `WM_CLASS` 且 `map_state` 为可见的顶层窗口。
- `IsForeground`、`GetForegroundWindow` 都走上面的判断；`ForegroundFallbackInputChannel` 的前台判断随之生效（e2e / keystate / rawmouse 无回归）。
- `PlatformGameSystemControl.GetGenshinGameProcessNameList` 与 Win32 宿主一致：已绑定游戏窗口时返回该窗口自己的进程名，
  `IsOperable()`（`CheckAndActivateGameWindow` 使用）因此能识别 Wine / Proton 或测试窗口。Windows 宿主不变。
- 更正里程碑 29 的记录：当时一条龙暂停的原因不是“没有窗口管理器”，而是前台进程名（测试窗口的进程）不在候选进程名列表里，`IsOperable()` 判为不可操作。

### 验证

- 两个测试窗口交替激活：有窗口管理器（xfwm4）与没有窗口管理器的显示上，激活均成功（无窗口管理器时 1–2 ms），`IsForeground` 与前台窗口一致，测试窗口收到焦点进出事件。
- 一条龙（默认配置）在两种显示上：
  - 空白画面：“领取邮件”回主界面时按 Esc 多次（识别不到主界面），之后“没有邮件奖励”；进入第 2 个子任务后停止，以 Cancelled 结束，没有仍按住的键。
  - 用模板拼出的主界面 / 派蒙菜单 / 邮件页三张画面（输入后切到下一张）：Esc → 识别邮件图标并点击 → 识别“全部领取”并点击 → 回到主界面，日志“邮件：全部领取”，第 1 个子任务完整执行。
    第 2 个子任务“合成树脂”因窗口 1600x900 小于 1920x1080（地图追踪要求）失败；3、4 因配置未选秘境 / 首领跳过；5 “幽境危战”状态机在主界面重试 5 次后失败；停止后没有仍按住的键。
- 回归：Core 单测 275 / 6（无回归），主项目单测 833 / 243 / 12，X11 e2e PASS 16 SKIP 1，keystate / rawmouse FAIL 0；Wine e2e 一次 `move_click` 未收到点击（FAIL 1），单独重跑两次均 PASS 17。

### 文件数（口径同上）

Core 978 个，主项目 759 个（本里程碑前半部分未移动文件）。

## 里程碑 31：全自动（调度器）、设置页

### 导航对照

Windows 版导航中的“任务计划”现在是新的计划系统（`PuloniaTaskPlanPage`：计划、执行记录、路线 CD、触发方式），
配置组页面 `ScriptControlPage` 在“全自动 → 调度器”下。Avalonia 宿主按同样的位置组织：
“全自动”页包含调度器、JS 脚本、地图追踪、录制回放四个标签；“任务计划”暂为说明页（计划系统尚未迁移）。

### 调度器（配置组）

- Core 新增 `ScriptGroupStore`（`User/ScriptGroup/*.json` 的读取、按 Index 排序、恢复“从此执行”标记、原子保存、删除）
  与 `ScriptGroupRunner`（原 `ScriptControlViewModel.StartGroupsCore`）；`ScriptGroupSchedule` 新增 `GetNextScriptGroups`、`SetGroupNextFlag`、`SetProjectNextFlag`。
  WPF 的 `ScriptControlViewModel` 改为委托（读取失败的提示、保存失败的提示不变）。新增 4 个单元测试（`ScriptGroupScheduleTests`）。
- Avalonia 调度器：配置组列表（新增、删除、上移 / 下移、刷新、“连续任务从此开始执行”、连续执行）；组内项目（序号、名称、类型、启用状态切换、
  “从此执行”、移除、倒序、清空）；添加键鼠脚本与 Shell。增删改后与 WPF 相同地补 Index 并写文件。
  运行与 WPF 相同：`RunnerContext.Reset()` 后在 `TaskRunner` 中执行 `IScriptService.RunMulti`；停止取消本轮令牌，`TaskRunner` 结束时释放所有按键。
- JS 脚本 / 地图追踪 / 录制回放：列出 `User/JsScript`（读 manifest）、`User/AutoPathing`、`User/KeyMouseScript`，可添加到选中的配置组；键鼠脚本可直接回放。
  JS 脚本的自定义设置、项目通用设置、配置组设置、脚本仓库、录制尚未迁移。
- Linux 宿主补注册 `IServerTimeProvider` 并初始化 `ServerTimeHelper`（此前配置组执行时“月卡判断异常：ServerTimeHelper 尚未初始化”）。
- 启动时创建 `log` 目录（任务的调试截图直接写入 `log/…`，Windows 版由日志文件创建该目录）。

### 设置

- 通用：原神游戏语言、保存截图 / 截图遮盖 UID / 奖励识别截图、地图追踪匹配方式、地图缩放 / 相对拖动、就近七天神像、失去焦点自动恢复、窗口类名优先、服务器时区、详细错误日志。
- 遮罩：启用遮罩、识别结果、日志窗口、状态、帧率、UID 遮盖、准星（颜色、线宽、大小、间隔）。
- 快捷键：与 WPF 快捷键页相同的条目与分组（“内部测试”除外），点击后按下组合键绑定，写法与 WPF `HotKey.ToString` 相同。
  经平台的 `IHotkeyService`（X11 `XGrabKey`）注册；已实现的动作：启动停止 BetterGI、停止 / 暂停当前任务、自动拾取 / 自动剧情开关、一条龙，其余只保存绑定。
- 通知：常用开关与 Webhook 地址（Linux 宿主尚未接入通知发送）。
- 关于：版本、运行时、显示、素材目录、配置文件。

### 验证

- 假游戏 + 键鼠脚本配置组（按住 W 6 秒、空格、点击）：
  - 3 秒后点击停止：W 在取消时抬起，任务 Cancelled，没有仍按住的键；
  - 不停止：完整执行，点击落在客户区 (800,450)，任务 Completed；
  - 配置“停止当前脚本/独立任务”= F10，运行中用 xdotool 按 F10：日志“检测到您配置的停止快捷键F10按下”，任务以 UserRequested 中断，没有仍按住的键。
- 一条龙在 1920x1080 的假游戏场景（主界面、Esc 菜单、邮件）中：领取邮件“全部领取”完成；合成树脂进入地图追踪。
  使用 Sdcb 精简版 `libOpenCvSharpExtern.so` 时，地图追踪因缺少 `features2d_SIFT_create` 失败；改用 `Build/Linux/build-opencvsharp.sh`
  的完整构建（`OpenCvSharpExternPath`）后地图特征点加载完成，之后因假游戏没有大地图画面而“打开大地图失败”，任务按预期重试、取消，没有仍按住的键。
- 与原版 WPF（Wine）的对照截图：调度器、JS 脚本、地图追踪、录制回放、通用设置、遮罩、快捷键、通知、关于。

### 文件数（口径同上）

Core 980 个（新增 `ScriptGroupStore`、`ScriptGroupRunner`），主项目 759 个；Avalonia 宿主 `.cs` 与 `.axaml` 共 45 个。

## 里程碑 32：长按快捷键、完整 OpenCvSharpExtern 提示、其余快捷键动作、Linux 通知

### 一条龙打开地图时 M 键“按住约 74 秒”

不是按键泄漏：`TpTask.TryToOpenBigMapUi` 每次调用 `InputHub.Foreground.SimulateAction(GIActions.OpenMap)`（单次 KeyPress），
随后最多等待 63×80 ms 检查大地图界面；假游戏没有大地图画面，所以每约 5 秒按一次 M，两轮重试共 14 次，每次按住约 30～60 ms。
之前的统计脚本取“第一次按下到最后一次抬起”，误报为 74 秒。按每次按下 / 抬起配对重新统计：M 按下 14 次，最长单次 63 ms，结束时没有仍按住的键。
与 Windows 代码路径相同（同一个 Core 方法、同一个前台输入通道），不是 Linux 特有问题，代码未改。

### OpenCvSharpExtern

- 宿主仍按 `Host.Common` 的规则选择原生库：找到 `opencv-build/out/libOpenCvSharpExtern.so`（或 `OpenCvSharpExternPath`）时使用完整构建，否则退回 Sdcb 精简构建。
- 新增 `OpenCvCapabilities`：启动时探测 SIFT / SURF 能否创建。完整构建记录“OpenCV 特征匹配：SIFT / SURF 可用”；
  精简构建记录警告“OpenCV 特征匹配不可用（EntryPointNotFoundException…）……地图追踪、传送及一条龙中依赖大地图的任务无法运行”，并给出构建方法。
  “环境”页新增“特征匹配（地图追踪）”一行显示同样的结论。两个宿主（Avalonia、无界面）共用。

### 快捷键

- Core 新增 `GameTask/Common/Hotkeys/HotkeyActionCatalog`（实时任务开关、日志窗口 / 指标栏开关、快捷点击确认 / 取消按钮、独立任务快捷键与任务的对应、长按类快捷键清单）
  与 `HoldHotkeyRunner`（长按期间反复执行、按下 / 抬起回调，语义同 WPF `KeyboardHook` 的 IsHold）。WPF 快捷键页改为调用前者，行为不变。
- 平台接口新增可选的 `IHotkeyHoldService`（按下 / 抬起），X11 由 XGrabKey 的 KeyPress / KeyRelease 实现，按下与抬起回调在同一线程按顺序执行。
- Avalonia 设置页补齐动作：截图、日志与状态窗口、遮罩指标栏、7 个实时任务开关、8 个独立任务启动 / 停止、那维莱特转圈、快速强化圣遗物、快速购买、
  一键领取奖励、快速进出尘歌壶、快捷点击确认 / 取消、一键战斗宏、手动触发快速传送（按住状态经 `HotkeyHoldState` 提供给快速传送触发器）、获取大地图中心点位置。
  键鼠录制、模板素材制作、路径记录器 / 添加路径点依赖 Windows 版的录制与编辑器，尚未迁移。
- 与 Windows 的差异：长按类快捷键在 Windows 上用低级键盘钩子（不拦截按键），X11 上用 XGrabKey（游戏收不到该键）。

### 通知

- Core 新增 `NetworkNotifierFactory`：按通知配置创建 14 种网络通知器（Webhook、飞书、OneBot、企业微信、WebSocket、Bark、钉钉、Telegram、信息推送、Discord、Server 酱、MeoW、Gotify、QQ）。
  WPF `NotificationService` 改为用它创建这些通知器，Windows 通知、邮件、微信 Clawbot 仍由主程序创建（通知并发发送，注册顺序不影响结果）。
- Linux 宿主新增 `LinuxNotificationService`（实现 `INotificationHub`：事件订阅过滤、补截图、发送后释放截图，与 Windows 版相同）
  与 `FreedesktopNotifier`（notify-send，没有时 gdbus 调用 `org.freedesktop.Notifications.Notify`；都不可用或失败时静默）。
  “Windows 通知”开关在 Linux 上对应系统通知。邮件与微信 Clawbot 尚未接入。HttpClient 的请求日志只保留警告以上（地址中可能带令牌）。
- 设置 → 通知新增“测试通知”与“系统通知”两项；Avalonia 新增无人值守参数 `--notify-test`。

### 验证

- 长按：F7 绑定“那维莱特转圈”，xdotool 按住 1.5 秒：假游戏收到 149 次相对移动，全部落在按住期间，抬起后停止。
- 普通：F6 切换自动钓鱼（日志“切换自动钓鱼状态为[开启] / [关闭]”），F8 启动 / 停止自动伐木。
- 通知：本机 Webhook 接收端收到任务失败事件（task.error）与测试通知（notify.test），均带截图；用记录参数的 notify-send 替身验证系统通知的参数。
- OpenCV：精简构建与完整构建分别启动，日志分别为不可用警告与“SIFT / SURF 可用”。

## 里程碑 33：快捷键透传（不拦截按键）、Linux 键鼠录制

### 快捷键透传

Windows 的“键鼠监听”类快捷键用低级键盘钩子，按键照常送到游戏；X11 的 XGrabKey 会把按键从游戏“抢走”。几种方案的比较：

| 方案 | 游戏能否收到 | 问题 |
| --- | --- | --- |
| XGrabKey | 否 | 现有“全局热键”方式 |
| XGrabKey 后 XTest 重放 | 是 | 重放的按键又会触发抓取（需临时解除抓取），时序与修饰键状态难以还原 |
| XRecord | 是 | 需要单独的数据连接，部分环境（XWayland）不支持 |
| XInput2 原始事件（XI_RawKeyPress 等，只监听不抓取） | 是 | 与 Windows 低级钩子相同：只观察，不拦截 |

采用 XInput2 原始事件：

- `Platform.Linux` 新增 `X11RawInputHook`（`IGlobalInputHook`）：在根窗口监听原始按键、按钮、移动与滚轮，按键名与 WinForms Keys 相同；
  X 服务器没有 XInput2 时退回原来的轮询实现。
- 区分自身的 XTest 注入，避免回环：`XTestInput` 每次注入前在 `XTestInjectionLedger` 记一笔（按键或按钮、按下或抬起），
  监听收到来自 XTEST 从设备、且台账中有对应记录的事件时标记 `IsSelfInjected`（`GlobalKeyEventArgs` / `GlobalMouseEventArgs` 新增的属性，Windows 上恒为 false），
  快捷键服务忽略这些事件；其它订阅方（录制等）照常收到。用户自己用 xdotool 等发送的按键仍然能触发快捷键。
- Core 新增 `Platform/GlobalInputHotkeyService`：基于任意 `IGlobalInputHook` 实现 `IHotkeyService` 与 `IHotkeyHoldService`（修饰键精确匹配、按住时不重复触发、回调在同一线程按顺序执行）。
- 设置 → 快捷键：每个快捷键可在“全局热键（XGrabKey，抓取）”与“键鼠监听（不拦截）”之间切换，沿用配置中的 `xxxHotkeyType`（与 Windows 相同的取值）。
  键鼠监听方式与 Windows 一样只在游戏窗口在前台时触发；长按类快捷键总是用键鼠监听；带修饰键的组合键在键鼠监听方式下仍改用全局热键。

### 键鼠录制

- `GlobalKeyMouseRecord`、`KeyMouseRecordService` 与录制消息从主项目移到 Core（`Core/Recorder`），WPF 的录制页、快捷键、战斗策略选择器继续使用，行为不变。
- Core 新增 `KeyMouseRecordInputBridge`：把全局键鼠监听的事件转交给录制（WPF 由 `MouseKeyMonitor` 转交）。
- Linux 宿主注册 `IRelativeMouseInputMonitorFactory`（XInput2 原始移动，DirectInput / RawInput 都映射到它）与 `KeyMouseRecordService`，
  新增 `KeyMouseRecordController`（录制期间接上监听）。全自动 → 录制回放新增“开始录制 / 停止录制”，“启动/停止键鼠录制”快捷键可用。
  录制文件与 Windows 相同（`User/KeyMouseScript/BetterGI_GCM_*.json`），主界面（检测到好友聊天图标）记录相对移动，其它界面记录绝对坐标。

### 验证（假游戏，1920×1080）

- 透传：W 绑定“自动钓鱼开关”（键鼠监听）。BetterGI 自己回放的键鼠脚本按住 W 1.2 秒时不触发开关（自身注入）；xdotool 按 W 时开关切换，假游戏同时收到 W。
  F6（键鼠监听）游戏收到并切换自动邀约；F7（那维莱特转圈，长按）游戏收到 F7，按住 1.5 秒期间持续转动、抬起停止；F10（全局热键）游戏收不到，停止任务。结束时没有仍按住的键。
- 录制：F9 开始录制，3 秒后开始；xdotool 依次按住 W 1.2 秒、相对移动 5×80 像素、左键单击、空格，再按 F9 停止。
  脚本中为 W 按下 / 抬起（间隔 1215 ms）、5 条 MouseMoveBy (80, 0)、左键按下 / 抬起、空格按下 / 抬起。
- 回放：把录制文件加入配置组运行，假游戏收到 W 按住 1204 ms、5 次相对移动 80 像素、左键单击、空格 317 ms，与录制一致。

### 尚未完成

- Windows 版新的“任务计划”页（PuloniaTaskPlanPage）尚未迁移。

### 文件数（口径同上）

Core 988 个（新增 `GlobalInputHotkeyService`、`KeyMouseRecordInputBridge`，并移入录制的 3 个文件），主项目 756 个；Avalonia 宿主 `.cs` 与 `.axaml` 共 45 个。

## 里程碑 34：任务计划（Pulonia）

### 抽到 Core

- `Pulonia/Models`、`Pulonia/Services`、`Pulonia/Executors`、`Pulonia/Remote` 移到 Core（命名空间不变）：计划模型、存储 `PuloniaTaskStore`（User/Pulonia，两个平台共用）、
  任务构建与创建、校验、调度（Cron、触发器游标）、执行服务 `PuloniaTaskService`、游戏任务协调器、6 种执行器（独立任务、录制回放、地图追踪、JS、Shell、进程内 C#）、路线 CD、执行记录。
- 仍留在主程序（Windows 专用）：账号切换器（窗口识别与 Win32 输入）、Windows 凭据管理器、仓库管理服务（`ScriptRepoUpdater`）、热键键位校验（WPF HotKey），以及调度宿主 `PuloniaTaskTriggerHost`（WPF Dispatcher、多实例、热键注册）。
- 依赖改为 Core 已有的接口：自动更新等待用 `ScriptAutoUpdateGate`，关闭 HTML 遮罩用 `HtmlMaskWindowHost`；
  凭据保存改为 `PuloniaTaskStore.DefaultCredentialVaultFactory`，Windows 主程序加载时设为 Windows 凭据管理器（与原来相同），其它平台写入凭据时报“不支持”。
- 调度循环抽成 Core 的 `PuloniaTriggerScheduler`（每秒检查定时触发器、计划变化时重读、热键注册错误按 30 秒周期重试），热键注册经 `IPuloniaTriggerHotkeys` 由宿主提供。
  `PuloniaTaskTriggerHost` 保留主实例判断、Dispatcher 关闭守卫与热键注册，循环交给它，行为不变。
- 运行状态与任务类型的显示文本移到 Core（`PuloniaTaskRunStatusText`、`PuloniaTaskTypeText`），WPF 视图模型改为调用它们。

### Linux 宿主

- `GameHost` 注册存储、构建器、执行器、资源目录、创建服务与执行服务（没有多实例派发与账号切换），以及 `PuloniaSchedulerHost`（后台运行 `PuloniaTriggerScheduler`，定时触发器生效；计划快捷键尚未注册）。
- Avalonia 新增“任务计划”页（原来是占位页），布局对照 WPF：左侧计划列表（任务数、运行中标记、▶ 运行、新建、删除），右侧计划标题与运行 / 停止，
  “任务”页签（选择任务类型与资源后添加、添加分组、上移 / 下移、删除、启用勾选；任务树按层级缩进），“执行记录”页签（状态、提交时间与耗时、结果、来源），“触发方式”页签（只读与调度状态）。
  修改自动保存。共享设置、设置方案、路线 CD、计划引用、任务参数编辑、运行预览、触发方式编辑尚未迁移。
- 无人值守参数 `--task-plan <名称或 ID>`（停止时间沿用 `--script-stop-after`）。

### 验证（假游戏 1920×1080）

- 简单计划（1 个录制回放任务）：运行 5.9 秒后“已完成”，假游戏收到 W 按住 1211 ms、空格 319 ms，与录制一致，执行记录页显示该运行。
- 停止测试（3 个录制回放任务）：运行 3 秒后点击停止，状态“已停止”（计划已取消，当前执行器已退出并释放资源）；停止时正按住的 W 随即抬起（按住 800 ms），结束时没有仍按住的键。

### 文件数（口径同上）

Core 1105 个（移入 Pulonia 114 个，新增 `PuloniaTriggerScheduler`、`PuloniaTaskRunStatusText`、`PuloniaTaskTypeText`），主项目 642 个；Avalonia 宿主 `.cs` 与 `.axaml` 共 48 个。

## 里程碑 35：脚本设置、任务计划编辑与界面自测

### 抽到 Core（WPF 改为调用，行为不变）

- `Model/SettingItemValues`：JS 脚本 settings.json 表单的取值规则（默认值写入且不覆盖已有值、多选列表规范化与勾选顺序、级联选择按二级值反查一级）。WPF `SettingItemControls` 的默认值与多选逻辑改为调用它。
- `Core/Script/Group/ScriptGroupConfigFields`：配置组设置（原 `ScriptGroupConfigView`）的字段目录，74 项，含分区、属性路径、标题、说明、编辑方式、下拉选项（回复时机、精英掉落模式、伤害数字识别、角色编号、赶路角色、赶路模式）、
  只在任务计划节点或旧调度器中显示的范围，以及依赖其它设置的显示条件（赶路角色决定跳飞 / 切人步行 / 接近停车距离等，派蒙检测、旋转寻敌、光柱扫描、聚集材料的子项）。按路径读写并做类型转换，无法转换的输入不写入。
  WPF 视图模型的下拉选项与赶路相关显示条件改为调用它。
- `Core/Script/Group/ScriptGroupProjectSettings`：项目通用设置的规则（JS 通知在全局关闭时不可改、HTTP 权限按地址列表 Hash 保存、HTTP 提示文字与级别），WPF `ScriptGroupProjectEditorViewModel` 改为调用它。
- `Pulonia/Services/PuloniaTriggerEditing`：触发方式编辑的选项文字（服务器时区、忙碌策略）、可选目标与账号、数值校验、定时预览、带整体校验的新增 / 替换。WPF 触发器编辑器改为调用它。
- `Pulonia/Services/PuloniaParameterForm`：按能力参数 Schema 列出顶层参数（排除共享设置覆盖的参数），有效值与运行准备相同（默认值 → 共享设置 → 任务参数），输入解析（布尔、整数、数字、文本、枚举、字符串列表、JSON），合并后按 Schema 校验再写入。

### Avalonia

- 调度器页：可以添加 JS 脚本（User/JsScript）；每个项目有“设置”（执行周期、次数、状态，JS 的通知与 HTTP 权限）与“JS设置”（settings.json 表单：输入框、下拉、勾选、多选、级联选择、分隔线）；标题栏“配置组设置”按分区显示全部字段，显示条件随修改即时刷新。
  设置面板在页面内打开，“保存并关闭”时写入配置组文件。只提供 HTML 设置页的脚本提示暂不支持（WebView 未迁移）。
- 任务计划页：选中任务后右侧显示“任务设置”（名称、启用、分组执行次数、按 Schema 生成的参数），“应用”时整体校验，失败时不写入并保留输入；“触发方式”页签可新增 / 编辑定时触发（Cron、时区、忙碌策略、允许延迟、补跑、启用，实时预览接下来 5 次执行时间，无效表达式不能保存），列表中可启用 / 停用 / 删除。快捷键触发仍只能启停与删除（Linux 尚未注册计划快捷键）。
- 修复：任务移动或应用设置后选中项丢失（保存后计划对象被替换，改为按任务 ID 重新选中）。

### 界面自测

新增宿主内参数 `--ui-test script-settings | taskplan`：在真实窗口里按名称或文字找到控件，像用户一样点按钮、选下拉、勾选、输入，然后直接读取磁盘上的配置文件核对。

- script-settings（32 项）：新增配置组、添加 JS 与 Shell；JS 设置的 6 种控件默认值与修改后存盘、重新打开读回；项目通用设置（周期、次数、HTTP 权限 Hash）；配置组设置（开关、文本、下拉、无效数字不写入、赶路角色切换后显示跳飞设置、Shell 配置）。
- taskplan（19 项）：新建计划、添加 Shell / 录制回放 / 分组、下移与上移、停用、任务名称与参数（file_name、arguments）、无效执行次数被拒绝且文件不变、分组执行次数、删除任务、无效 Cron 不能保存、新增 / 停用 / 编辑 / 删除定时触发、删除计划。

### 文件数（口径同上）

Core 1110 个（新增上述 5 个），主项目 642 个；Avalonia 宿主 `.cs` 与 `.axaml` 共 51 个。

## 里程碑 36：Linux 音频播放、系统语音、剧情语音移入 Core、网页设置页

### 音频播放（PulseAudio / PipeWire）

- `Platform.Linux/Audio/PulseAudioWavePlayer`：实现 NAudio `IWavePlayer`，经 libpulse-simple（P/Invoke，运行时按 soname 加载，不分发 .so）播放；PipeWire 通过 pipewire-pulse 兼容层同样可用。
  支持 PCM 8/16/32 位与 32 位浮点；`Play` 不阻塞，后台线程约 20 ms 一块写入，播完等待 drain 后触发 `PlaybackStopped`；`Stop` 只置标志，由播放线程丢弃未播放数据（flush）后触发，与 WasapiOut 语义一致，所有 libpulse 调用都在同一线程。目标缓冲设为约 100 ms（服务器默认约 2 秒，短句会等到 drain 才出声）。
- `LinuxAudioOutput.Install()`：宿主启动时探测（打开一个播放流后立即关闭），可用则把 `QuestVoiceAudioHost.OutputFactory` 设为 PulseAudio 输出，否则保持静音输出（流程照常推进）。结果写日志并显示在环境页“音频 / 播放输出”，不可用时提示安装 libpulse0 或 pipewire-pulse。
- 验证：本机另起一个私有 pulseaudio（独立运行目录、null sink），用 parec 录制 monitor：1.5 秒 440 Hz 正弦完整录到 1.5 秒；5 秒 660 Hz 在播放 0.8 秒后 Stop，录到 0.8 秒，`PlaybackStopped` 在 Stop 后 10 ms 内触发；espeak-ng 合成的中文语音经 `WasapiAudioPlayer` + PulseAudio 输出完整播放。
  null sink 空闲时按 2 秒为单位休眠，新流开始时最多有 2 秒的启动延迟，这是 null sink 的特性，真实声卡没有。

### 系统语音与 TTS 引擎注册

- 剧情语音的会话、会话工厂、路由与全部 HTTP TTS 引擎（20 个文件）移入 Core；布局资源 `auto-scene.json` / `routing.example.json` 随 Core 程序集嵌入（资源名不变）。
- `TtsProviderRegistry` 改为宿主注册平台引擎：内置列表保留 `windows-sapi` 的描述与原有顺序，实现由 WPF 主程序模块初始化时 `WindowsSapiTtsProvider.Register()` 补上（Windows 界面与行为不变）；未注册实现的平台引擎不出现在引擎列表，按 ID 创建时报“当前平台不支持”。
  主单测 `RegisteredProviderHasImplementation("windows-sapi")` 不经主程序模块初始化，测试类构造时显式注册（该用例在 Linux 单测中保持通过）。
- `Platform.Linux/Tts/EspeakNgTtsProvider`：调用本机 espeak-ng（`--stdout`，台词经标准输入传入，按实际长度截取 PCM 后重新封装 WAV），支持声音列表（`--voices`）与语速；找不到 espeak-ng 时不注册，环境页显示“未安装”。注册后在配置的引擎独立设置中补齐缺省条目（声音 `cmn`），Windows 配置不含该条目。

### 其余遗留

- `GameFrameGrabber`（Core）：运行环境未启动时临时抓一帧，由宿主按平台设置实现。WPF 的 `Win32GameFrameGrabber` 是原 `MusicGuidedSession.OpenGameFrame` 中的代码（行为不变），Linux 宿主经 `IPlatformServices.CreateCapture` 与 `Windows.FromHandle`。`MusicGuidedSession` 不再引用 Fischless.GameCapture 与 Win32 窗口。

### 网页视图与 HTML 设置页

方案对比与建议见 [webview-linux.md](webview-linux.md)。采用 Avalonia 官方 `Avalonia.Controls.WebView`（12.1.1，MIT，Linux 使用系统 WebKitGTK 4.1）：

- `Avalonia/Services/WebKitSchemeHandler`：在 WebKitWebView 的上下文注册 `bgi-settings://` 自定义协议，页面全部请求交给 Core 的 `ScriptSettingsRequestHandler`（与 Windows 版拦截 WebView2 请求相同，不联网、不监听端口），响应头含相同的内容安全策略。
- `Avalonia/Views/HtmlSettingsWindow`：对应 WPF `JsScriptHtmlSettingsWindow`；调度器中只提供 `settings_html` 的脚本点“JS设置”时打开，保存写回配置组文件，标题显示保存时间。没有 WebKitGTK 时提示安装 `libwebkit2gtk-4.1-0`。环境页显示网页引擎版本。
- 界面自测 `--ui-test html-settings`（8 项）：新建自测专用配置组，加入演示脚本，点击“JS设置”打开网页窗口，演示页读取上下文与设置、访问外网被拦截、自动保存一次，核对配置组文件中的文本 / 数字 / 数组与窗口标题，最后删除该配置组（不动已有配置组）。
- HTML 遮罩（`IHtmlMaskWindowHost`）里程碑 37 实现。

### 文件数（口径同上）

Core 1131 个（剧情语音 20 个文件移入，新增 `GameFrameGrabber`），主项目 623 个；Avalonia 宿主 `.cs` 与 `.axaml` 共 53 个。

## 里程碑 37：HTML 遮罩窗口

- 剧情语音播放失败的提示不再写“WASAPI”：输出名由宿主设置（Linux 为 PulseAudio 或“静音输出”），Windows 文字不变。
- HTML 遮罩：`NativeWebView` 的嵌入与离屏模式在透明窗口里都画出不透明背景，改为直接用 WebKitGTK 建 RGBA 顶层窗口
  （`Avalonia/Services/GtkHtmlMaskWindowHost`），透明、置顶、SHAPE 输入形状点击穿透、跟随游戏画面；脚本目录映射到 `bgi-mask://` 虚拟主机，
  消息桥与 Windows 版相同，网络请求用 WebKit 内容过滤规则按 `http_allowed_urls` 放行。地址映射、消息解析、HTTP 权限校验移入 Core `HtmlMaskPage`，
  WPF `HtmlMaskWindow` 改为调用它。详见 [webview-linux.md](webview-linux.md)。
- 界面自测 `--ui-test html-mask`（16 项）：假游戏 + picom 上核对透明显示、消息收发与请求应答、资源访问限制、穿透 / 可交互切换时的点击去向，
  以及 Composite 窗口截图不含遮罩内容。截图方式为屏幕区域时截图器画面会含遮罩内容，打开遮罩时有提示。

- 剧情语音服务层：`QuestVoiceService`（自动启停、状态快照、试听、声音列表、连接探测）移入 Core（命名空间不变，WPF 照常注入）；
  试听播放改用宿主设置的输出（Windows 仍为 WasapiOut）。Linux 宿主按 WPF 相同方式注册会话工厂、控制器与服务，退出时停止配音。
- Avalonia 实时触发页新增“自动配音（实验性）”卡片（对应 WPF `QuestVoiceCard`）：开关、状态、六个阶段节点（空心 / 进行中 / 完成 / 异常配色与连线）、
  当前对白、最近事件、校验配置、确认后端已重启、打开日志目录。引擎与角色目录的设置窗口尚未迁移（卡片中有说明）。
- 界面自测 `--ui-test quest-voice`（7 项）：卡片显示并绑定 Core 服务、校验配置有结果、打开开关后服务随截图器启动（默认 GPT-SoVITS 未配置时报告配置错误，
  与 Windows 相同）、关闭开关后停止；开关写入的配置在自测前后由外部脚本备份恢复。与 Wine 下原版的展开卡片截图对照，布局与文字一致。

### 文件数（口径同上）

Core 1135 个（新增 `HtmlMaskPage`，`QuestVoiceService` 3 个文件移入），主项目 620 个；Avalonia 宿主 `.cs` 与 `.axaml` 共 55 个。


## 里程碑 38：自动配音设置窗口、日志文件

- Avalonia 宿主可选保存日志文件：`HostLogging` 增加延迟启用的滚动文件输出（启动阶段先缓冲，按 Linux 宿主设置 `LogFileEnabled` 启用，默认开启），
  写入程序目录 `log/better-genshin-impact<日期>.log`，格式与 Windows 版相同。设置页“其他设置”与自动配音卡片提供“打开日志 / 打开日志目录”（xdg-open）。
  界面自测 `--ui-test log-file`（6 项）。
- 自动配音设置模型迁入 Core：`QuestVoiceSettingsViewModel`、角色目录编辑 `QuestVoiceManagementViewModel`、GPT-SoVITS 与各引擎设置模型（命名空间不变）。
  UI 线程投递、防抖计时器、确认框、文件选择经 `IQuestVoiceSettingsUi` 提供，WPF 实现 `WpfQuestVoiceSettingsUi` 沿用原写法（Background 投递、DispatcherTimer、
  ThemedMessageBox、系统文件对话框）。当前平台没有实现的内置引擎不列出；宿主额外注册的引擎（Linux 的 espeak-ng）以通用设置模型按能力显示，Windows 列表不变。
- Avalonia 自动配音设置窗口（对应 WPF `QuestVoiceSettingsWindow`）：引擎与声线（★ 默认引擎、各引擎参数、刷新声音、添加为声线、本引擎声线库、试听）、
  角色（角色声线开关、默认 NPC / 旁白 / 旅行者声线、角色列表编辑）、识别（采样间隔）、高级（超时、缺失策略、角色目录文件、重新加载 / 校验 / 导入 / 导出 / 新建）。
  从实时触发页卡片的“设置…”打开，打开期间暂停配音，关闭后保存角色目录草稿再恢复。
- 界面自测 `--ui-test quest-voice-settings`（35 项）：角色目录指向输出目录中的副本（不动程序目录的 routing.json），核对引擎列表、设为默认引擎、语速与 VITS 地址、
  espeak-ng 声音添加为声线并分配给新角色、自动保存后磁盘上的 routing.json（含备份文件）、删除角色、校验与导出，以及 config.json 中的各项设置。
  与 Wine 下原版窗口截图对照，四页布局与文字一致。

### 文件数（口径同上）

Core 1154 个（自动配音设置模型 18 个文件移入，新增 `IQuestVoiceSettingsUi`、通用引擎设置模型），主项目 604 个；Avalonia 宿主 `.cs` 与 `.axaml` 共 59 个。

### 专属参数编辑器：自动秘境

- 自动秘境的专属设置模型迁入 Core：`SoloTaskSettingsViewModel`（基类）、`AutoDomainSettingsViewModel`、`SoloTaskSettingsFactory`、`ResinPlanSubTaskSpec`、
  `DomainSourceViewModel`、树脂入口 `ResinUsageEntryViewModel` / `IResinSettingsDialogs` / `ResinUsageViewModelSupport`（命名空间不变）。
  `ResinUsageViewModelSupport` 原先用 `Application.Current is App` 判断是否从服务容器取树脂弹窗，改为宿主设置的 `ResolveDialogsFromServices`（WPF 在 `App.OnStartup` 置为 true，行为不变）。
- Avalonia 任务计划的参数面板：任务有专属编辑器时优先显示（与 WPF `PuloniaTaskSettingsEditorViewModel` 相同），专属编辑器管理的字段不再出现在通用字段中，
  其余字段显示为“其他参数”；应用时合并两部分的修改。对应关系在 `DedicatedTaskSettings`（目前为自动秘境），视图 `Views/SoloTaskSettings/AutoDomainSettingsEditor`
  对照 WPF `AutoDomainSettingsView`：战斗策略、队伍、秘境来源（跟随培养计划 / 指定秘境）、秘境与周日奖励、培养计划设置、树脂使用摘要、分解圣遗物、奖励识别。
  Linux 版暂无树脂设置对话框，树脂使用只显示摘要且保持原值。
- 修正 `PuloniaParameterForm`：数组项 Schema 的 `type` 为数组（如 `["string","null"]`）时不再抛出类型转换异常（此前选中自动秘境节点会失败；仅 Avalonia 使用）。
- 界面自测 `--ui-test domain-editor`（19 项）：新建临时计划并添加自动秘境，核对专属编辑器与通用字段不重复，指定秘境 / 周日奖励 / 队伍 / 分解圣遗物 / 奖励识别存盘，
  重新选中后显示存盘值，切换为跟随培养计划后预留率、诊断、备选周日奖励存盘且未改字段保留，最后删除临时计划。
- 独立任务页的秘境卡片仍是按配置绑定的少量参数（WPF 独立任务页同样不使用专属设置模型），其余配置项待补；战斗策略、树脂等其他专属编辑器待迁移。

Core 1162 个（上述 8 个文件移入），主项目 596 个；Avalonia 宿主 `.cs` 与 `.axaml` 共 62 个。

## 里程碑 39：专属参数编辑器（续）、树脂设置对话框

- 设置模型迁入 Core（命名空间不变）：地脉花、领取每日奖励、合成浓缩树脂、尘歌壶（含商品项）的专属设置模型，
  以及树脂设置对话框的内容模型 `ResinUsageEditorViewModel`、`CraftResinEditorViewModel`、`ResinKindRowViewModel`、`ResinTierChoice`、`ResinConfirmation`、`IResinSettingsDialogContent`。
  WPF 视图只改 XAML 命名空间引用。
- Avalonia 树脂设置对话框 `ResinSettingsWindow`（对照 WPF `ResinSettingsDialog`）：使用方式（刷至耗尽 / 指定领奖次数 / 按树脂分别指定）、领奖次数、可用树脂与次数、
  原粹树脂档位；合成浓缩树脂的合成方式与保留数量；底部摘要或校验提示、确定 / 取消。`IResinSettingsDialogs` 是同步接口，
  Avalonia 实现 `AvaloniaResinSettingsDialogs` 以嵌套消息循环等待模态窗口关闭；切换模式时的确认框用宿主消息框。各专属编辑器的“树脂使用 · 设置”因此可用。
- 任务计划参数面板新增专属编辑器：自动地脉花（跳过准备流程、树脂使用）、领取每日奖励（冒险家协会地区、好感队）、合成浓缩树脂（地区、合成方式）、
  尘歌壶（进壶方式、购买日期、商品勾选）。
- 界面自测 `--ui-test dedicated-editors`（19 项）：临时计划中添加四个任务，逐个核对专属编辑器、树脂对话框（改为领奖 3 次并确定；再次打开改模式后取消不改值）、
  合成保留 20、各项存盘值，最后删除临时计划；`--ui-test domain-editor` 复测 19 项通过。

Core 1173 个（11 个文件移入），主项目 585 个；Avalonia 宿主 `.cs` 与 `.axaml` 共 73 个。

### 首领讨伐、幽境危战

- `AutoBossSettingsViewModel`、`AutoStygianOnslaughtSettingsViewModel` 迁入 Core；首领模型新增 `BossNameChoice`（忽略下拉框写回的 null，供 Avalonia 绑定，WPF 视图不变）。
- 任务计划参数面板新增两个专属编辑器（对照 WPF `AutoBossSettingsView` / `AutoStygianOnslaughtSettingsView`）：战斗策略、首领（按地区顺序展开为一个列表；WPF 为级联下拉）/ 幽境首领序号、
  队伍、树脂使用、累计领奖上限与累计次数（刷新 / 清空，范围为当前计划与任务，与 WPF 相同）、返回七天神像、奖励识别、死亡重试；幽境危战的分解圣遗物与最高星级。
  战斗策略下拉框使用 `User/AutoFight` 下的策略（WPF 为策略选择控件）。
- 界面自测 `--ui-test boss-editors`（12 项）：临时计划中添加两个任务，核对编辑器与存盘值后删除计划。
- 体力计划（`ResinPlanSettingsViewModel` 及子任务设置，依赖一条龙参数字段模型）与独立任务页秘境卡片的完整配置项待迁移。

Core 1175 个，主项目 583 个；Avalonia 宿主 `.cs` 与 `.axaml` 共 77 个。

## 里程碑 40：体力计划编辑器、独立任务页秘境配置项

- 一条龙参数字段模型移入 Core（命名空间不变）：`PuloniaTaskParameterFieldViewModel`、`PuloniaOneDragonParameterFieldViewModel`、`PuloniaOneDragonSettingChoices`，
  字段生成移至 `PuloniaParameterFieldFactory`（WPF 编辑器委托）。秘境下拉项经 `IPuloniaDomainPickerItems` 提供：Core 默认给秘境名字符串，
  WPF 以 `ModuleInitializer` 安装原来的级联项实现，WPF 绑定对象不变。培养计划秘境选项常量移入 `TrainingGuideDomainNames`，WPF 委托。
- 体力计划设置模型 `ResinPlanSettingsViewModel`、子任务模型与 `IResinPlanSettingsDialogs`、以及 `ResinUsageConfigBinding` 移入 Core；WPF 视图只改 XAML 命名空间引用。
- 任务计划参数面板新增体力计划专属编辑器（对照 WPF `ResinPlanSettingsView`）：限定体力用量与最多消耗、优先合成浓缩树脂与合成地区、活动优先与严格模式、
  首领 / 秘境 / 幽境危战 / 地脉花四个子任务（启用、摘要、设置）。子任务设置窗口 `ResinPlanSubTaskWindow` 按一条龙参数字段渲染（对照 WPF 子任务设置对话框）。
- 独立任务页自动秘境卡片补齐 WPF 独立任务页的配置项：队伍、秘境来源（跟随培养计划 / 指定秘境）、秘境、培养计划设置（自动计算次数、刷取偏好、合成收益预留率、
  按领奖结果更新库存、识别失败按固定预算继续、备选秘境与周日奖励、低冒险等阶适配、详细诊断）、树脂使用（树脂设置对话框）、分解圣遗物与最高星级、
  战斗结束等待、小步伐、步行开启秘境、左右移动次数、自动吃药、奖励识别、死亡重试。“按材料数量提前结束”（目标材料列表）尚未迁移。
- 界面自测：`--ui-test resin-plan-editor`（12 项，临时计划中修改体力上限、合成、首领开关与秘境子任务队伍并核对存盘，最后删除计划）；
  `--ui-test solo-domain`（11 项，修改卡片配置与树脂使用后核对 `config.json`，测试前后由外部脚本备份恢复配置）。
- Wine 下运行原版对照时发现：非秘境字段的空选项类型变为 `object[]`，WPF 级联选择框按 `IEnumerable<ICascadingItem>` 校验后抛异常，
  体力计划子任务设置窗口无法打开。`IPuloniaDomainPickerItems` 新增 `Empty`（WPF 返回 `ICascadingItem` 空数组）后修复，原版窗口在 Wine 下正常打开。

Core 1185 个，主项目 576 个；Avalonia 宿主 `.cs` 与 `.axaml` 共 84 个。

## 里程碑 41：WPF 界面冒烟检查

- WPF 主程序新增界面冒烟检查 `Service/WpfSmokeTest.cs`：仅在环境变量 `BGI_WPF_SMOKE=1` 时执行，默认关闭，正常启动不受影响。
  主窗口加载后依次打开启动、实时触发、独立任务、任务计划、一条龙、调度器、通用设置、快捷键、通知等页面并展开其中的卡片，
  再打开树脂设置对话框（自动秘境、合成浓缩树脂）、体力计划四个子任务设置窗口、自动配音设置窗口、调度器配置组设置（临时配置），
  窗口数秒后以关闭（取消）结束，不保存设置。期间的未处理异常记为失败，结果写入 `BGI_WPF_SMOKE_OUT` 指定的文件，退出码 0 表示通过。
- Linux 上在 Wine 下运行该模式，作为迁移的回归检查：在里程碑 40 第一个提交的代码上能捕获体力计划子任务设置窗口的 `XamlParseException`，修复后全部通过。
- Avalonia 对照修正：体力计划子任务设置窗口改为与 WPF 相同的卡片行（标题与说明在左，开关 / 下拉 / 输入框在右）；战斗策略下拉框按
  `CombatStrategyResolver` 的规范名称显示（默认的策略根目录显示为“根据队伍自动选择”，未修改时不写回）。独立任务页秘境卡片的
  分解圣遗物开关与最高星级、备选秘境与“周日或限时”与 WPF 一样放在同一行。
- 独立任务页秘境卡片补上“按材料数量提前结束”：材料目标列表模型 `TargetItemListEditorViewModel` 移入 Core（命名空间不变，WPF 不变），
  Avalonia 卡片在指定秘境时显示开关、材料目标行（材料、数量、删除）与“添加材料”，候选材料随秘境刷新。界面自测 `--ui-test solo-domain` 增至 15 项。

Core 1186 个，主项目 576 个；Avalonia 宿主 `.cs` 与 `.axaml` 共 84 个。

## 里程碑 42：任务计划快捷键触发

- Linux 宿主实现计划快捷键触发 `LinuxPuloniaTriggerHotkeys`（`IPuloniaTriggerHotkeys`，对照 WPF `PuloniaTaskTriggerHost` 的热键部分）：
  调度循环读到计划变化后重新注册已启用的快捷键触发；全局热键经平台热键服务（X11 `XGrabKey`），键鼠监听方式经共用的全局键鼠监听（不拦截按键）。
  键位规则与 WPF 相同（全局热键需组合键或功能键、F12 保留；键鼠监听只接受单键），与软件已有快捷键冲突的不注册。
  按下后与 WPF 一样调用 `FireHotkeyAsync`，经相同的准入、去抖与忙碌策略提交运行。
- Linux 额外拒绝 Ctrl + Alt + F1~F12：X 服务器（XKB 默认键位）把它当作切换虚拟终端并吞掉按键，热键永远收不到。
  WPF 新建快捷键触发的默认键位 Ctrl + Alt + F8 因此在 Avalonia 中改为 Ctrl + Shift + F8（Core 模型默认值不变）。
- Avalonia 任务计划页“触发方式”页签支持新增 / 编辑快捷键触发：快捷键输入与录制、全局热键 / 键鼠监听、启用与预览；
  页签底部显示计划快捷键的注册状态。
- 界面自测 `--ui-test plan-hotkey`（13 项）：临时计划中新增快捷键触发，核对键位校验、录制、存盘与注册，
  再用 `xdotool` 在同一显示上按下快捷键，核对计划已启动运行，最后删除计划并核对快捷键已注销。
- WPF 界面冒烟检查增加“编辑任务”窗口：在临时计划中依次插入 Shell、地图追踪、JS 脚本、录制回放、体力计划、自动秘境及几个带参数的独立任务，
  逐个打开编辑任务窗口（共 27 步）。临时计划缺少资源，自动保存失败的提示窗口也一并关闭；冒烟脚本运行前后备份恢复 `User/Pulonia`。
- Avalonia 任务计划页支持引用已有计划（与 WPF “引用已有任务计划”相同，插入来源为计划的分组节点，不复制内容）：
  直接或间接互相引用时拒绝；仍被其他计划引用的计划不可删除。界面自测 `--ui-test plan-reference`（9 项）。
- Avalonia 任务计划页新增“路线 CD”页签：列出账本中地图追踪路线的完成记录（作用域、完成时间、CD 截止或一次性已完成），
  可逐条清除（与 WPF 相同调用 `ClearCollectionCooldownAsync`）。界面自测 `--ui-test plan-cooldown` 核对列表与账本一致；
  本机账本没有路线记录，清除按钮只在有记录时验证（预置到 `state.json` 的测试记录会在启动时被状态校验丢弃，未能用于自测）。
- 尚未迁移：任务计划共享设置与设置方案管理、任务树上的路线 CD 标记与按计划筛选的 CD 总览。
