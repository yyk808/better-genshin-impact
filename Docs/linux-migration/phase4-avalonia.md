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

### 下一步

1. 启动页补全：截图模式、窗口选择、同时启动游戏（Linux 用 Wine / Proton 启动命令）。
2. 配置持久化：Linux 上的 `ConfigService`（`User/config.json`）。
3. 遮罩窗口（透明、置顶、点击穿透）。
4. 按页面迁移：独立任务 → 一条龙 → 脚本 / JS → 地图追踪 → 热键 → 其它设置页。

### 文件数（按 git 跟踪的 `.cs` 统计，口径同 phase2-progress.md）

Core 972 个，主项目 763 个（本里程碑未移动 Core / 主项目文件）；Host.Common 6 个；Avalonia 宿主 `.cs` 与 `.axaml` 共 23 个（含 `.axaml` 视图 6 个）。
