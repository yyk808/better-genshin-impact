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
