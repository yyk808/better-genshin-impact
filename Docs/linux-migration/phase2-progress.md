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
