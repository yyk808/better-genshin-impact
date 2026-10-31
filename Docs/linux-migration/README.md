# BetterGI 迁移到 Avalonia / 原生 Linux

本目录记录把 BetterGI 从 WPF（Windows 专属）迁移到 Avalonia + 跨平台 .NET、最终在 Linux 上原生运行的计划与进展。

| 文档 | 内容 |
|---|---|
| [MIGRATION.md](MIGRATION.md) | **总说明（fork 自用，定期同步上游）**：架构、移入 Core 的模块、Linux 依赖与构建、与 Windows 的差异、已知问题、测试、同步上游的步骤与常见冲突 |
| [roadmap.md](roadmap.md) | 分阶段路线图、每阶段的目标与验收标准 |
| [phase1-foundation.md](phase1-foundation.md) | 第一阶段（平台抽象地基）的成果、验证结果与遗留问题 |
| [phase2-progress.md](phase2-progress.md) | 第二阶段（接入平台服务、清理 Win32 依赖）的里程碑记录与验证结果 |
| [phase4-avalonia.md](phase4-avalonia.md) | Avalonia 宿主（共享宿主引导、主窗口外壳、启动 / 实时触发 / 环境页、配置持久化）的里程碑记录与验证结果 |
| [webview-linux.md](webview-linux.md) | Linux 网页视图方案评估（HTML 设置页、HTML 遮罩）与最小实现 |
| [architecture.md](architecture.md) | 项目结构、平台抽象接口、Windows / Linux 实现的对应关系与约定 |
| [survey.md](survey.md) | 迁移前的 Windows 依赖调研（精简版） |

分支：`avalonia-linux`（从 `refactor/20260930` 切出）。

## 当前状态

- 业务代码（识别、任务、脚本、配置、触发器、任务计划等）已移入纯 `net10.0` 的 `BetterGenshinImpact.Core`，WPF 主程序改为调用 Core，Windows 行为不变。
- Linux（X11 / XWayland）上有无界面宿主 `BetterGenshinImpact.Host.Headless` 和图形界面宿主 `BetterGenshinImpact.Avalonia`，两者共用 `BetterGenshinImpact.Host.Common`。
- 总说明与同步上游的步骤见 [MIGRATION.md](MIGRATION.md)。本分支是自用 fork，不向上游提交 PR。

## 在 Linux 上编译

```bash
# 整个解决方案（Windows 目标项目需要 EnableWindowsTargeting）
dotnet build BetterGenshinImpact.sln -p:EnableWindowsTargeting=true

# 平台无关部分可以不带 Windows 目标单独编译
dotnet build BetterGenshinImpact.Core
dotnet build BetterGenshinImpact.Platform.Linux

# Core 的单元测试在 Linux 上原生运行
dotnet test Test/BetterGenshinImpact.Core.UnitTest
```
