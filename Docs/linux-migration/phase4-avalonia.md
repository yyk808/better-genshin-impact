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
3. 遮罩窗口（透明、置顶、点击穿透）。
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
  之后保存时会一起写回。
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

### 文件数（口径同上）

Core 972 个，主项目 763 个（本里程碑未移动文件；Core 只改了 `PlatformRuntimeProvider`，Windows 不使用）；Host.Common 10 个；
Avalonia 宿主 `.cs` 与 `.axaml` 共 26 个（含 `.axaml` 视图 7 个）。
