# 第二阶段进展：接入平台服务、继续清理 Win32 依赖

第二阶段按里程碑推进，每个里程碑单独提交，要求：全解决方案可编译、Windows 行为不变、单元测试不退化、
Platform.Linux 在两个假游戏（Wine / X11 原生）上的端到端回归通过。

## 验证方式

| 项目 | 做法 |
|---|---|
| 编译 | `dotnet build BetterGenshinImpact.sln -p:EnableWindowsTargeting=true`（Linux 上），0 错误 |
| Core 单元测试 | `dotnet test Test/BetterGenshinImpact.Core.UnitTest`，纯 `net10.0`，Linux 原生运行 |
| 主项目单元测试 | `BetterGenshinImpact.UnitTest` 在 Linux 上去掉 `Microsoft.WindowsDesktop.App` 框架后用 `dotnet vstest` 运行，与第一阶段基线逐条比较 |
| 端到端 | 本机驱动程序（不入库）用 Platform.Linux 操作 Wine 中的假游戏与 X11 原生假游戏，检查找窗、截图、键鼠、热键 |

> 注意：在 Linux 上构建时不要设置 `DOTNET_SYSTEM_GLOBALIZATION_PREDEFINED_CULTURES_ONLY=0`（Wine 中运行 BetterGI 时会用到）。
> 设置后任意字符串都被当作合法文化名，MSBuild 会把 `Resources/QuestVoice/routing.example.json` 当成 `example` 文化的附属资源，
> 内嵌资源丢失，QuestVoice 相关测试失败。构建服务器会继承该变量，需要 `dotnet build-server shutdown` 后重新构建。

## 里程碑 1：DI 注册平台服务，运行时经平台找窗；拆分 SystemControl

- Core
  - `IPlatformWindow.ProcessName`（默认实现返回空字符串）。
  - `GameWindowSearch`（候选进程名 + 是否窗口类名优先）、`IGameWindowFinder`。
  - `WindowManagerGameWindowFinder`：基于 `IWindowManager` 枚举结果的通用查找，按进程名（忽略 `.exe`）
    或 WM_CLASS 中的 `进程名.exe`（Wine 窗口）匹配，多个命中取面积最大者。Linux 使用它。
  - `IPlatformServices.GameWindows`；`CreateCapture(string? method)` 可按名称选截图方式。
  - `GameTask/GenshinGameProcess`：候选进程名、子会话启动参数（从 SystemControl 拆出，平台无关，支持 Linux 路径）。
- Platform.Windows
  - `Win32GameWindowFinder`：原 `SystemControl.FindGenshinImpactHandle` 的三级查找原样迁入（日志文本、分类不变）。
  - `Win32WindowOps`：原 SystemControl 中按 HWND 操作窗口的函数（窗口矩形、激活、聚焦、还原、前台进程…）。
  - `Win32PlatformServices(captureMode, logger)`：`GameWindows`、按名称创建截图器。
- Platform.Linux：`X11PlatformServices.GameWindows`，`CreateCapture("GetImageWindow")` 等按名称选 X11 截图方式。
- 主项目
  - DI 注册单例 `IPlatformServices`（Windows 为 `Win32PlatformServices`）。
  - `Win32RuntimeProvider` 经 `IPlatformServices.GameWindows` 找窗，用 `CreateCapture` / `CreateInput` 创建截图器与输入后端。
  - `SystemControl` 只保留游戏启动 / 关闭、重启程序等应用逻辑，窗口函数改为转发到 `Win32WindowOps`，外部调用方不变；
    `SystemControl.Platform` 提供静态入口；新增 `FindGenshinImpactWindow()`（返回 `IPlatformWindow`）。
- 新增 `Test/BetterGenshinImpact.Core.UnitTest`（纯 `net10.0`）：窗口查找、`WindowQuery`、启动参数等 20 个用例，Linux 原生通过。

验证：编译 0 错误；主项目单测 785 通过 / 118 失败 / 5 跳过（第一阶段基线 776 / 127 / 5，无退化；
多出的 9 个通过来自修正了上面的构建环境变量问题，失败的均为依赖 Windows 环境的用例）；
端到端 Wine 17 PASS、X11 16 PASS / 1 SKIP，且 `WindowManagerGameWindowFinder` 在两个假游戏上都找到了同一窗口。

## 里程碑 2：系统键状态与全局键鼠监听抽象

- Core
  - `IKeyStateProvider`（系统 / 物理键鼠状态：`IsKeyDown`、`GetPressedKeys`、`GetCursorPosition`），相当于 GetAsyncKeyState。
  - `IGlobalInputHook`（全局键鼠监听，不拦截）与平台无关的事件参数 `GlobalKeyEventArgs` / `GlobalMouseEventArgs`。
  - `PollingGlobalInputHook`：轮询 `IKeyStateProvider` 产生按下 / 抬起 / 移动事件（左右修饰键优先、修饰键先于普通键），
    没有系统钩子的平台使用；观察不到滚轮。
  - `KeyModifierHelper.MouseButtonName`：与 WinForms `MouseButtons.ToString()` 相同的按钮名，录制脚本格式不变。
  - `IPlatformServices.KeyState`、`CreateGlobalInputHook()`。
- Platform.Windows：`Win32KeyStateProvider`（GetAsyncKeyState）、`Win32GlobalInputHook`（包装 MouseKeyHook 的低级钩子，
  事件线程与原来相同）、`Win32KeyConvert`（Keys / MouseButtons ↔ GameKey）。MouseKeyHook 包引用从主项目移到 Platform.Windows。
- Platform.Linux：`X11KeyStateProvider`（XQueryKeymap + XQueryPointer，按当前键盘映射建立 keycode → GameKey 反查表）；
  `CreateGlobalInputHook()` 返回基于它的轮询监听。
- 主项目
  - 直接调用 `GetAsyncKeyState` 的 5 处（技能 CD、自动钓鱼输入检查、引导窗口、音游覆盖层、相对鼠标消息处理）改为 `IPlatformServices.KeyState`。
  - `MouseKeyMonitor.GlobalHook` 改为 `IGlobalInputHook`（由平台服务创建）；键鼠录制（`GlobalKeyMouseRecord` / `KeyMouseRecorder`）、
    鼠标热键（`MouseHook`）、脚本 `KeyMouseHook` 改用平台无关事件。热键与脚本回调拿到的键名仍是 WinForms `Keys` 名称，行为不变。
  - Wine 专用的轮询（`WinePlatformAddon`）与相对鼠标 Raw Input（`RawInputMonitor`）保持原样，留待后续。

验证：编译 0 错误；Core 单测 33 个通过；主项目单测 785 / 118 / 5，无退化；端到端 Wine 17 PASS、X11 16 PASS / 1 SKIP；
新增驱动检查 `keystate`：在 Xvfb 上用 XTest 注入 Shift+F、右键、移动，`X11KeyStateProvider` 与轮询监听全部检测正确。

## 里程碑 3：热键走平台热键服务；快捷键配置文本的平台无关解析

- Core
  - `IHotkeyService.Register(..., noRepeat)`：按住不放不重复触发（Windows `MOD_NOREPEAT`；X11 开启可检测的自动重复，按 KeyRelease 判断）。
  - `WpfKeyNames`：WPF `Key` 名称（含别名）↔ VK 对照表。表由 `KeyInterop` 在 Wine 中实测生成（`WpfKeyNames.g.cs`），不依赖 WPF。
  - `HotkeyText`：快捷键配置文本（`"Ctrl + Shift + F11"`、`"XButton1"`、`"< None >"`）的解析与格式化，
    规则与主程序 `Model.HotKey` 相同；`ToGesture()` 得到可注册的 `HotkeyGesture`。Linux 版用它读取现有配置。
- 主项目：`HotKeySettingModel` 的“全局热键”改为经 `IPlatformServices.Hotkeys` 注册（`GlobalRegisterHook` 变为注册句柄）。
  键名仍先按原来的 WinForms `Keys` 规则解析，注册的 VK、修饰键、`MOD_NOREPEAT`、失败时的异常与提示都与原来相同。
  “键鼠监听”类型的热键已在里程碑 2 改走 `IGlobalInputHook`。
- 发现的现有问题（未改变，保持 Windows 行为）：WPF 键名中有 37 个无法按 WinForms `Keys` 解析（如 `OemPlus`、`OemComma`、
  `LeftShift`），这些键设为“全局热键”时注册会失败。`HotkeyText` 能正确解析它们，Avalonia 版不受影响。

验证：编译 0 错误；Core 单测 56 个通过（含全部 WPF 键名的往返校验）；主项目单测 785 / 118 / 5，无退化；
端到端 Wine 17 PASS、X11 16 PASS / 1 SKIP；驱动 `keystate` 检查中，X11 普通热键按住 1.5 秒触发 22 次，noRepeat 热键只触发 1 次。

## 里程碑 4：注册表 / 游戏设置服务（Linux 读 Wine 前缀 user.reg）

- Core
  - `IUserRegistry` / `IRegistryKey`：当前用户注册表的只读访问，值类型与 `Microsoft.Win32.RegistryKey.GetValue` 一致。
  - `WineRegistryFile`：Wine 注册表文件解析器（键名 / 字符串转义、`\x539f` 形式的非 ASCII 字符、`dword:`、`hex:` 多行续行、
    `hex(b):` QWORD、`str(2)`/`str(7)` 扩展字符串与多字符串、`@` 默认值）。
  - `WinePrefixUserRegistry`：读取 `<WINEPREFIX>/user.reg`，文件修改后自动重新解析。
  - `Genshin.Settings.GenshinSettingsStore` / `IGameSettingsStore`：原主程序 `GenshinRegistry` 的区服判断与开键逻辑迁入
    （运行中进程 → 配置的游戏路径 → 注册表是否存在，规则不变；游戏路径也接受 Linux 路径）。`GenshinRegistryType` 一并迁入（命名空间不变）。
  - `IPlatformServices.UserRegistry`：Windows 为 `Win32UserRegistry`（HKCU），X11 为 Wine 前缀（构造参数或 `$WINEPREFIX`，默认 `~/.wine`）。
- 主项目：`GenshinRegistry.GetRegistryKey` 改为经平台注册表 + `GenshinSettingsStore`，返回 `IRegistryKey`；
  `SettingsContainer`（GENERAL_DATA / 键位映射）、`ResolutionSettings`、`GenshinGameSettings` 随之改用 `IRegistryKey`，读取结果不变。
- 未迁移：写注册表的部分（原神 HDR 自动关闭、URL 协议注册、通知抑制等）仍直接用 Win32 注册表；
  Linux 上写 Wine 注册表应通过 `wine reg`（wineserver 运行时直接改文件会被覆盖），留待 Linux 宿主实现。

验证：编译 0 错误；Core 单测 75 个通过，其中注册表测试数据由 Wine 11 的 `reg add` 实际写出
（中文键名、二进制多行 GENERAL_DATA、DWORD/QWORD/扩展字符串/多字符串/默认值）；主项目单测 785 / 118 / 5，无退化；
端到端 X11 16 PASS / 1 SKIP，Wine 第一次运行 16 PASS / 1 WARN（锁定视角相对移动少计 1 条原始输入，与本次改动无关），
重跑两次均为 17 PASS。

## 里程碑 5：UI 线程 / 消息框 / 剪贴板服务

- Core（`Platform/UiServices.cs`）
  - `IUiDispatcher`（`CheckAccess` / `Invoke` / `Post` / `InvokeAsync`）、`IDialogService`（消息框 `Show` / `ShowAsync` 与轻提示 `Toast`，
    `Error` / `Warning` / `Information` 等扩展方法的默认标题与 `ThemedMessageBox` 相同）、`IClipboardService`（文本剪贴板）。
  - `UiHost`：供不经依赖注入创建的任务代码使用的静态入口。未设置时为无界面实现（`InlineUiDispatcher` 直接在当前线程执行，
    `HeadlessDialogService` 只发出事件并返回 `None`，`InMemoryClipboard` 为进程内存），Core 单测和命令行宿主可直接使用。
- Platform.Linux：`CommandLineClipboard`，按会话选择 `wl-copy` / `wl-paste`（Wayland）或 `xclip` / `xsel`（X11），找不到工具时退回进程内剪贴板。
  Avalonia 宿主应改用 `TopLevel.Clipboard`。
- 主项目：`Service/Ui` 中的 `WpfUiDispatcher`、`WpfDialogService`（包装 `ThemedMessageBox` 与 Wpf.Ui.Violeta 的 `Toast`）、
  `WpfClipboardService`（在 UI 线程上访问剪贴板）。`App.OnStartup` 最先设置 `UiHost`，同时注册到 DI。
- 迁移的调用方：`GameRuntimeService`（UI 线程判断与切换）、`Win32RuntimeProvider`、`SystemControl.StartFromLocalAsync`、`TaskRunner`、
  `AutoSkipTrigger`、`AutoPickTrigger`、`ScriptParser`（七圣召唤）、`ComboCombatTaskFactory`、`AutoDomainTask.TrainingGuide`、`OneDragonTask`、
  `LogParse`、`TravelsDiaryDetailManager`、`KeyMouseRecordService`、`GlobalKeyMouseRecord`、兑换码（`UseRedemptionCodeTask`、`RedeemCodeManager`）、
  脚本引擎的 `GlobalMethod.InputText` 与 `ScriptRepoUpdater`。消息文本、标题、是否切到 UI 线程都与原来相同。
- 未迁移（与 WPF 窗口直接相关，随 Avalonia 宿主处理）：`WebPageRuntimeProvider`、`PathRecorder`、`AutoComboTreeWindowService`、
  `RepoWebBridge`、`ScriptRepoUpdater` 中打开窗口的部分、`OneDragonTask` / `SystemControl` 中的退出程序、`ImageRegionReferenceSearchHelper` /
  `MatchTemplateHelper` 中的 `Application.Current is App` 判断（用于取日志），以及 View / ViewModel 中的用法。

验证：编译 0 错误；Core 单测 82 个通过（新增 UI 服务与命令行剪贴板测试，后者用脚本模拟剪贴板工具）；主项目单测 785 / 118 / 5，无退化；
端到端 X11 16 PASS / 1 SKIP，Wine 第一次 16 PASS / 1 WARN（同里程碑 4 的锁定视角相对移动偶发），重跑 17 PASS；`keystate` 0 FAIL。

## 里程碑 6：清理业务代码中的 System.Drawing（GDI+）

区分两部分：`Color` / `Point` / `Size` / `Rectangle` 属于跨平台的 System.Drawing.Primitives（.NET 共享框架自带，Linux 可用），保留；
`Pen` / `Bitmap` / `Graphics` / `Font` / `Brush` 属于只支持 Windows 的 System.Drawing.Common（GDI+），需要从业务代码中去掉。

- Core：`Drawing/DrawPen`（颜色 + 线宽，不持有非托管资源）与 `DrawPens`（对应 `System.Drawing.Pens`，线宽 1，每次返回新实例）。
  为兼容原有代码与脚本，构造函数（`(Color)`、`(Color, float)`）、`Color` / `Width` 属性、`Dispose()` 与 `System.Drawing.Pen` 一致。
- 主项目：遮罩绘制相关 API（`Region.DrawSelf` / `DrawRect` / `DrawLine` / `ToMaskWindowDrawingRect`、`RecognitionObject.DrawOnWindowPen`、
  `CameraOrientation.DrawDirection`、`MaskWindowDrawingStroke.FromPen` 等）及 20 个调用文件改用 `DrawPen`，颜色与线宽不变。
- 脚本引擎：JS 中的 `Pen` 宿主类型改为 `DrawPen`。已在 Wine 中用 ClearScript V8 实测 `new Pen(Color.Lime, 3)`、`new Pen(Color.Red)`、
  给 `RecognitionObject.DrawOnWindowPen` 赋值、修改 `Width`、调用 `Dispose()` 均与原来一致。
- `GetGridIconsTask` 保存图标改用 OpenCV 编码 PNG（像素与原先经 GDI+ `Bitmap` 保存的相同）。
- 剩余（未改，均为 Windows 专用或仅调试输出）：`RewardResultRecognizer` 与 `InventoryCountComparisonTask` 的调试图用 GDI+ 写中文标注
  （OpenCV 的 `PutText` 不支持中文，需换 SkiaSharp，输出会有细微差异，留待移入 Core 时处理）；`Helpers/Extensions/BitmapExtension`（WPF 图像转换）；
  View 中的用法随 Avalonia 宿主替换。

验证：编译 0 错误；Core 单测 85 个通过；主项目单测 785 / 118 / 5，无退化；端到端 Wine 17 PASS、X11 16 PASS / 1 SKIP；`keystate` 0 FAIL。

## 里程碑 7a：识别基础代码移入 Core（第一批）

做法：文件按原相对路径 `git mv` 到 `BetterGenshinImpact.Core`，命名空间不变，调用方无需修改；
逐个编译 Core，依赖主项目类型的文件退回主项目，直到 Core 独立编译通过（迁移脚本留在本机，不提交）。

- 移入 37 个文件：OpenCV 辅助（模板匹配 `TemplateMatchHelper` / `FastSqDiffMatcher` / `SubPixMatch` / `TemplateMatchNormalizer`、
  裁剪、轮廓、图像差异、特征点分块与 `Feature2DFactory` 等）、OCR 数据类型与 `IOcrService` / `OcrResult`、ONNX 的设备类型 / 运行时包下载 /
  基准协调等、`SearchOptions`、`RecognitionTypes`、`PaddleOcrModelConfig`。
- `CommonExtension` 拆分：平台无关的几何 / OpenCV 扩展移入 Core；涉及 WPF `Point`/`Rect`/`Color` 与 Win32 `RECT` 的 5 个方法
  留在主项目的 `CommonWindowsExtension`（均为扩展方法调用，调用方不变）。
- Core 增加 `InternalsVisibleTo`（`BetterGI`、两个单测项目），搬入代码的 `internal` 成员可见性与原来一致。
- `HardwareAccelerationView.xaml` 中引用 `InferenceDeviceType` 的 xmlns 补上 `assembly=BetterGenshinImpact.Core`（命名空间跨程序集后 XAML 需要指明）。
- Core.UnitTest 引用 OpenCV 原生运行时（Windows：`OpenCvSharp4.runtime.win`；Linux：不依赖 GTK / FFmpeg 的 `Sdcb.OpenCvSharp4.mini.runtime.ubuntu.22.04-x64`），
  新增模板匹配 / 裁剪 / 图像差异测试，并把 `RectClampTests` 从主单测移入。识别代码的测试现在在 Linux 上原生运行。
- 仍在主项目的识别代码及主要阻碍：`RecognitionObject` 与 JSON 加载（依赖 `Region` / 配置）、Paddle OCR 与 YOLO（依赖配置 `AllConfig`、
  `HardwareAccelerationConfig`、ONNX Runtime 包、`Global` 路径）、`MatchTemplateHelper`（取日志用 `App`）、特征存储（依赖 `Global`）。
  下一批需要先把这些包（Newtonsoft.Json、Microsoft.ML.OnnxRuntime.Managed、YoloSharp、Clipper2 等，均为跨平台包）与配置模型移入 Core。

验证：编译 0 错误；Core 单测 99 个通过（含 OpenCV 原生调用）；主项目单测 785 通过 / 111 失败 / 5 跳过，通过集合与之前完全相同，无退化
（失败数少 7 是因为 Linux 上缺 WindowsBase 的“灾难性失败”理论用例在报告中少记了 7 行，本来就失败）；端到端 X11 16 PASS / 1 SKIP，
Wine 第一次 16 PASS / 1 WARN（锁定视角相对移动偶发），重跑 17 PASS；keystate 0 FAIL。

## 里程碑 7b：配置模型、任务模型与脚本基础代码移入 Core（第二批）

- Core 增加跨平台托管包（版本与主项目一致）：CommunityToolkit.Mvvm、Newtonsoft.Json、Clipper2、YamlDotNet、NCalc、Microsoft.ML.OnnxRuntime.Managed。
- 移入 274 个文件（命名空间不变）。主要有：配置模型 `Core/Config` 17 个（各功能的 `*Config`，`AllConfig`、`Global` 仍在主项目）、
  `GameTask/Common`（地图坐标、树脂、异常类型、奖励、部分 Job 模型）36 个、剧情语音 `QuestVoice` 的核心 / 特征 35 个、
  `SoloTask` 的契约与特性 17 个、秘境 / 战斗 / 钓鱼 / 七圣召唤 / 寻路 / 树脂规划等任务的模型与纯算法、`Core/Script` 的仓库模型与工具 13 个、
  `Helpers` 中的通用工具与扩展、`Model` 中的数据模型。
- 不搬的规则（脚本自动排除）：用 `typeof(X).Assembly` 取嵌入资源或程序集信息的文件；作为 `IStringLocalizer<T>` 资源定位类型的任务类
  （资源按 T 所在程序集查找，搬走会找不到翻译）；使用 Win32 / 注册表 / P/Invoke 的文件（`ConsoleHelper`、`UrlProtocolHelper`、
  `GenshinHdrRegistryHelper` 编译能过，但属于 Windows 专用，已退回主项目）。已确认没有被拆到两个程序集的 partial 类，也没有同名类型冲突。
  `SoloTaskRegistry` 扫描主程序集中带 `[SoloTaskInfo]` 的任务，这些任务都依赖 `GameRuntime`，仍在主项目，注册结果不变。
- XAML：6 个页面引用了已移入 Core 的枚举 / 类型（`CrosshairType`、`AutoPickMode`、`DamageNumberRecognitionMode`、`QuestVoiceStageState`、
  `SkillCdRule`、`AutoComboLlmProvider` 等），其 xmlns 补上 `assembly=BetterGenshinImpact.Core`。
- 测试：10 个只依赖 Core 的测试文件从主单测移入 Core.UnitTest（钓鱼图像识别、RodNet、OCR 后处理、树脂规划 / 执行 / 旧配置映射、
  剧情语音文本比较与控制器、`GameTaskWait` 等）；`FakeLogger`、`FakeInputChannel` 两个测试替身以源码链接方式共用。

验证：编译 0 错误；Core 单测 233 个，227 通过、6 失败——这 6 个是钓鱼截图测试，测试素材在未初始化的 Assets 子模块中，搬迁前在主单测中同样失败。
其中 62 个用例搬迁前就有 Linux 结果，结果一一相同（56 通过 / 6 失败）；另外约 70 个用例搬迁前在 Linux 上因为加载 WindowsBase 失败而根本没有报告结果，
现在可以在 Linux 上原生运行并通过。
主单测：所有搬迁前通过的用例现在仍然通过（在主单测或 Core 单测中），无退化。主单测的报告总数从 908 变为 1044，原因是部分测试类不再因类型加载失败而整类不报告；
新出现的用例中 96 个通过，95 个失败，失败原因都是 Linux 测试环境本身：缺 WPF 程序集（WindowsBase / PresentationCore）、缺 onnxruntime 原生库、
测试素材缺失，以及 `CombatStrategyResolverTests` 期望 Windows 路径分隔符。代码未改，Windows 上不受影响。
端到端 Wine 17 PASS、X11 16 PASS / 1 SKIP；keystate 0 FAIL。


## 里程碑 7c：遮罩绘制模型去 WPF 并移入 Core（第三批）

- 新增平台无关的 `MaskColor` / `MaskPoint` / `MaskRect`（`Core/Mask/MaskGeometry.cs`）。`MaskRect` 与 WPF `Rect` 一样拒绝负宽高并抛出 `ArgumentException`。
- 遮罩绘制板及相关接口、绘制模型、样式与 OpenCV 转换（共 7 个文件）移入 Core。原来的 WPF `Rect` / `Point` / `Color` 字段改为上面的新类型。
- WPF 端新增 `MaskWpfConversions`，只在渲染层（`MaskWindowDrawingLayer`、`MaskWindowViewModel`、`SkillCdOverlayRenderer`）转换回 WPF 类型。
  画笔缓存改用 `MaskColor` 作键；`MaskRect` 不会是 WPF 的 Empty 矩形，所以去掉了相应判断。绘制结果与原来相同。
- 留在主项目：`IMaskWindowHost`、`IMaskWindowMapState` 与 `MaskWindowMapState`（仍使用 WPF `Rect`，属于宿主侧，移植到 Avalonia 时再处理）。
- 再次运行批量搬迁后，没有新的文件可以移入 Core。阻碍集中在 `Region` 及其子类：
  `Region.Click` / `Move` 直接调用 `User32.GetCursorPos` 与 `DesktopRegion`（Win32 输入），`ImageRegion` 用 ImageSharp 缓存图像。
  它们被识别、`Bv` 和几乎所有任务引用，下一步需要先把 `Region` 的输入改为经 `IInputChannel`。
- 测试：新增 `MaskDrawingModelTests`（`MaskRect` 校验、`FromPen` 转换、绘制板分组与版本），在 Linux 上原生运行。

验证：编译 0 错误；Core 单测 236 个，230 通过，6 个失败，与之前相同（都是缺素材的钓鱼截图测试）；
主单测 825 通过 / 207 失败 / 12 跳过，与 7b 基线逐个用例比较，无退化、无缺失；端到端 Wine 17 PASS、X11 16 PASS / 1 SKIP；keystate 0 FAIL。

## 里程碑 8a：屏幕信息、InputHub 与服务入口移入 Core

- 新增 `IScreenInfo` 与静态入口 `PlatformHost`（键状态、主屏分辨率、Mica 支持判断）。Windows 主程序在模块初始化时设置为原有 Win32 实现，行为不变。
- `InputHub` 移入 Core。默认后端由宿主经 `InputHub.DefaultBackendFactory` 提供；未设置时为 `UnsupportedInputBackend`。
- **Region 卡点已解决**：`Region.Click` / `Move` 与 `DesktopRegion` 取光标和屏幕大小改走 `PlatformHost`，点击 / 移动经 `InputHub` 的 `IInputChannel`，不再直接调用 Vanara / `PrimaryScreen`。
- 新增 `CoreServices`（服务容器入口，委托给 App 的容器），140 个非界面文件的 `App.GetLogger` / `GetService` / `ServiceProvider` 改为调用它。
- `SoloTaskRegistry` 同时扫描主程序集与 Core 程序集（按 Order、TaskId 排序，结果不变）。

## 里程碑 8b：配置根与服务基础设施移入 Core

- 配置中使用的 WPF 枚举（`Stretch`、`WindowState`、背景材质）改为成员名和数值都相同的平台无关枚举。配置按数值序列化，配置文件不变；界面绑定经 `EnumValueConverter` 转换。
- `ConfigHost.Config` 作为配置根入口（`ConfigService.Config` 委托给它）。通知数据经 `INotificationHub` 访问通知服务。
- `AllConfig` 及全部配置模型、`Global`、`I18nService`、通知器等 42 个文件移入 Core；随后又移入可编译的服务模型、存储 / 网络基础设施、账号记录、攻略库等 158 个文件。
- 拆分 `IInferenceDeviceDiscovery`（DXGI 实现留在主程序）与 `ColorScalarExtension`。
- 配置 JSON 选项独立为 `ConfigJson.Options`，`ConfigService.JsonOptions` 仍返回同一实例。
- 新增 `IGameSystemControl` / `GameSystem`：任务代码经它调用 `SystemControl` 的窗口与进程操作；Windows 由 `SystemControlAdapter` 原样转发。

## 里程碑 8c：解除识别 / 任务代码对界面与 Windows 实现的直接引用

- `SettingItem.ToControl`、`ScriptProject.LoadSettingUi` 拆为主程序的扩展方法（WPF 控件生成留在主程序），数据模型移入 Core。
- 新增 `ITriggerDispatcher` / `TriggerDispatcher`。任务与脚本经接口启停触发器，调度器本身（画中画、遮罩宿主、性能指标）留在主程序。
- 新增以下入口，原位置保留同名转发，行为不变：
  - `GameKeyMapping.MappingKey`（原 `KeyBindingsSettingsPageViewModel.MappingKey`）
  - `PathingPaths.PathJsonPath`（原 `MapPathingViewModel.PathJsonPath`）
  - `GameTaskAssets.LoadAssetImage`（原 `GameTaskManager.LoadAssetImage`）
  - `PickTextHelper`（原 `AutoPickTrigger.GetWhiteTextBoundingRect`）
- E 技能分类结果改经 `ESkillClassifyMonitor.Publish` 发布。主程序把它接到 `ESkillClassifyViewModel`；结果坐标改用 `MaskRect` / `MaskPoint`。
- 剧情语音检测的进程音频捕获抽象为 `IProcessAudioCapture`，Windows 由 `ProcessAudioCapture.Factory` 提供 WASAPI 回环实现。
- `GameRuntimeExtensions` 的暂停逻辑改经 `IRunnerSuspendState`（`RunnerSuspend.Provider`，Windows 为 `RunnerContext.Instance`）。
- Core 引用 CsTrees 1.0.5、LibGit2Sharp 0.31.0、Microsoft.ClearScript.V8 7.4.5（只引用托管部分，原生库由宿主选择）。
  - 已确认 NuGet 上有 `Microsoft.ClearScript.V8.Native.linux-x64` 7.4.5。
  - 在 Linux 上用独立的小程序实测：`V8ScriptEngine` 可以执行脚本（数组方法、Promise）。

## 里程碑 9：识别核心移入 Core

移入 Core 的文件共 35 个：

- `Region` / `ImageRegion` / `DesktopRegion` / `GameCaptureRegion` / `ReferenceRect` / `ConvertRes`
- `OcrFactory`、PaddleOCR（`Det` / `Rec` / `PaddleOcrService`）、`BgiOnnxFactory` / `BgiYoloPredictor` / `InferenceRuntimeService` / `InferenceModelDiagnostics`
- 全部 `Bv` 视觉操作
- `GameRuntime` / `GameRuntimeExtensions` / `ISystemInfo` / `CaptureContent`
- 素材缓存与 AutoFight / AutoPick / Element 素材
- `NewRetry`、`RecognitionAssets`

此前已移入的 `RecognitionObject` 与 Bv 的翻译资源（`BvResxHelper*.resx`）配合使用。翻译资源生成 `BetterGenshinImpact.Core.resources.dll` 卫星程序集，复制到主程序输出目录。

验证（8a–9 每个推送点）：

- 编译 0 错误。
- Core 单测 236 个：230 通过，6 失败。失败的是原有的缺素材钓鱼截图测试，与基线相比无退化。
- 主单测 825 通过 / 249 失败 / 12 跳过，与 7b 基线逐个用例比较无退化、无缺失。失败比 7b 多出的 42 个都是新发现的理论用例数据行：
  - 21 行属于 `PuloniaTaskHotkeyTests`，原先因类型加载失败，一行都没有被发现；
  - 另外 21 行属于 `PaddleOcrService_VariousLangWords`，数据行比原先展开得更多。
  - 它们失败都是因为 Linux 上没有 `WindowsBase` / `System.Drawing.Common`，与基线中同名用例的失败原因相同。
- 端到端：Wine 17 PASS，X11 16 PASS / 1 SKIP；keystate 0 FAIL。

### 剩余阻碍（Core 外的任务层）

从识别核心向外扩展到任务层（约 250 个文件）时，剩下的外部引用如下：

- 4 个独立任务（AutoBoss / AutoDomain / AutoLeyLineOutcrop / AutoStygianOnslaught）的 `[SoloTaskSettingsView(typeof(View), typeof(ViewModel))]` 特性引用界面类型。需要把界面映射挪到主程序的注册表；`AutoDomainTask` 还直接使用 `AutoDomainSettingsViewModel`。
- `QuickTeleportTrigger` 使用 `HotKey` / `KeyboardHook` / `MouseHook`（WinForms 键鼠钩子）。
- `SkillCdOverlayRenderer` 使用 WPF `Color`。
- `ScriptRepoUpdater` / `PathRecorder` 打开 `WebpageWindow`。
- `AutoFishing/Behaviours` 使用 `MOUSEEVENTF`。
- `PathExecutor` / `AssertUtils` 调用 `SystemControl.GetGameScreenRect`（返回 Win32 `RECT`）。
- `TaskTriggerDispatcher` 与 `GameTaskManager.CreateTriggers` 属于宿主编排，计划留在宿主侧。
- 尚未开始：音游输入（`KeyInputTransports` 直接调用 SendInput / PostMessage）与 `RawInputMonitor` / `RelativeMouseInputMonitorFactory` 的抽象。

## 里程碑 10：修复 8b 引入的递归，独立任务的界面映射移出任务类

- **缺陷修复**：8b 的提交 06af6dcb 把 `SystemControl.` 批量改成 `GameSystem.` 时误改了 `SystemControlAdapter` 本身，使适配器转发回 `GameSystem`，造成无限递归（运行到获取窗口 / 截图区域时栈溢出）。8b–9 的验证没有覆盖到这条调用路径。
  - 1648020f 恢复适配器转发到 `SystemControl`。
  - 新增单测 `SystemControlAdapterTests`：扫描适配器方法的 IL，断言不调用 `GameSystem` / `IGameSystemControl`，并且至少调用一次 `SystemControl`。已确认重新注入该缺陷时测试失败。
  - 复查了其他批量改写（`ConfigJson`、`TriggerDispatcher`、`GameTaskAssets` 等），没有类似的自我转发。
- `IGameSystemControl` 增加 `GetGameScreenSize`，返回 `PixelSize`。`PathExecutor`、`AssertUtils`、AutoBoss / Duel / AutoDomain / AutoStygianOnslaught 不再使用 Win32 `RECT`。
- 新增程序集级 `SoloTaskSettingsMapAttribute(taskType, viewType, viewModelType)`：
  - 8 个独立任务的界面映射集中写在主程序的 `View/Pages/SoloTaskSettings/SoloTaskSettingsMappings.cs`，任务类不再引用界面类型。
  - `SoloTaskRegistry` 合并各程序集的映射，重复的映射直接报错。

## 里程碑 11：键鼠钩子、WPF 颜色、网页窗口、Win32 鼠标事件与任务解耦

- 快捷传送等用到的“热键是否按住”改经 `HotkeyHoldState.Query`（Core）查询。WinForms 键鼠钩子的逻辑原样移到主程序的 `HoldHotkeyQuery`。
- 新增 `MaskColorParser.ParseRgbaHex`（Core，有单测）。`OverlayStyleHelper` 委托给它，`SkillCdOverlayRenderer` 改用 `MaskColor`。
- 脚本仓库渠道列表抽为 `ScriptRepoChannels.All`，路径 JSON 选项抽为 `PathingJson.Options`，两者都与原静态成员是同一实例。`ScriptRepoUpdater` / `PathRecorder` 打开网页窗口的部分留在主程序。
- 钓鱼行为用布尔量 `_leftButtonDown` 记录左键状态，不再使用 `MOUSEEVENTF` 枚举。

## 里程碑 12：任务设置、全局输入钩子、连招颜色与 E 技能视图模型

- 新增 `ITaskSettingsSource`（伐木轮数 / 每日上限、按键截图开关、七圣召唤与战斗策略路径），由 `TaskSettingsPageViewModel` 实现。脚本的 `Dispatcher` / `Genshin` 改经服务获取，不再直接引用页面视图模型。
- 新增 `SharedGlobalInputHook`。`MouseKeyMonitor` 与 `KeyMouseHook` 共享同一个钩子实例，释放路径不变。`KeyMouseHook` 改用 `GameSystem.GetCaptureRect`（新增，返回 `PixelRect`）。
- `MaskColorParser` 增加 `ColorTextConverter`：Windows 由 WPF `ColorConverter` 提供，未设置时按十六进制解析。`AutoComboRunTask` 改用 `MaskColor`。
- `ESkillClassifyViewModel` 与 `ESkillClassifyResult` 移入 Core。
- Core 引用 CsTrees.MEAI 1.0.2、Microsoft.Extensions.AI 10.6.0、Microsoft.Extensions.AI.OpenAI 10.6.0、Anthropic 12.53.0。`Microsoft.Extensions.Logging.Abstractions` 升到 10.0.12，与主程序已解析的版本一致。

## 里程碑 13：任务层第一批 106 个文件移入 Core

- 自动连招行为树浮窗改经 `IAutoComboTreeView`（`AutoComboTreeView.Current`，默认是空实现）显示和隐藏，`AutoComboTreeWindowService` 实现该接口。
- 移入的主要文件组如下，翻译资源（`.resx`）与对应的类一起移动：
  - 通用 Job 任务（领取纪行 / 历练点奖励等）
  - 钓鱼行为树与模型
  - 脚本仓库存储与脚本依赖（`Core/Script/Repositories`、`Core/Script/Dependence`）
  - 剧情语音检测（`AutoSkip/Audio`）、自动拾取 / 剧情触发器
  - 连招建树（`AutoCombo/ComboBuild`）、`ITaskTrigger` / `ITriggerDispatcher` / `ITaskSettingsSource` / `HotkeyHoldState`
  - 技能 CD 遮罩、路径模型、`AssertUtils`、`GameScreenshotService` 等

验证（10–13 每个推送点）：

- 编译 0 错误。
- Core 单测 244 个：238 通过，6 失败，与 7b 基线相比无退化。失败的仍是原有的缺素材钓鱼截图测试；新增了 `MaskColorParser` 的测试。
- 主单测 1088 个：827 通过 / 249 失败 / 12 跳过，与里程碑 9 逐个用例比较无退化、无缺失。新增了 `SystemControlAdapterTests`。
- 端到端：Wine 17 PASS，X11 16 PASS / 1 SKIP；keystate 0 FAIL。

### 剩余阻碍（主程序中约 134 个任务层文件）

里程碑 9 列出的阻碍已全部解开。剩余文件的外部引用如下：

- `HtmlMaskWindow`（约 15 处）
- WinForms `MouseButtons` / `Keys`（约 14 处）
- `ClickExtension` / `Point.Click`（约 16 处）
- 直接调用 `Kernel32`（约 10 处）、`User32`（4 处），以及 `Win32KeyConvert`
- `InventoryCountComparisonTask` 使用 GDI+ 绘图（`Font` / `Brush` / `StringFormat` / `ToBitmap`）
- `CaptureModes`、`DefaultTcgConfig`
- `TargetItemCatalog` 通过 `typeof().Assembly` 读取嵌入资源
- `HungarianAlgorithm`、`TxtCombatTaskFactory` / `KeyMouseCombatTaskFactory`
- `FileFormatException`、`WhereNotNull`

以下部分计划留在宿主侧：

- 宿主编排：`TaskTriggerDispatcher`
- `GameRuntimeService`、`InstanceService`（Pulonia 远程）、`OverlayMetricsService`、`IMaskWindowHost`

尚未开始：音游输入（`KeyInputTransports`）与 `RawInputMonitor` / `RelativeMouseInputMonitorFactory` 的抽象。
