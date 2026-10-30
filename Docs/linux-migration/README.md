# BetterGI 迁移到 Avalonia / 原生 Linux

本目录记录把 BetterGI 从 WPF（Windows 专属）迁移到 Avalonia + 跨平台 .NET、最终在 Linux 上原生运行的计划与进展。

| 文档 | 内容 |
|---|---|
| [roadmap.md](roadmap.md) | 分阶段路线图、每阶段的目标与验收标准 |
| [phase1-foundation.md](phase1-foundation.md) | 第一阶段（平台抽象地基）的成果、验证结果与遗留问题 |
| [phase2-progress.md](phase2-progress.md) | 第二阶段（接入平台服务、清理 Win32 依赖）的里程碑记录与验证结果 |
| [architecture.md](architecture.md) | 项目结构、平台抽象接口、Windows / Linux 实现的对应关系与约定 |
| [survey.md](survey.md) | 迁移前的 Windows 依赖调研（精简版） |

分支：`avalonia-linux`（从 `refactor/20260930` 切出）。

## 当前状态（第一阶段完成后）

- 新增纯 `net10.0` 的 `BetterGenshinImpact.Core`（平台抽象层 + 输入抽象），`BetterGenshinImpact.Platform.Windows`（封装现有 Win32 实现）、
  `BetterGenshinImpact.Platform.Linux`（X11 / XWayland 实现）。
- 输入 / 截图 / 窗口接口签名中的 Win32 类型（`User32.VK`、`RECT`、`nint hWnd`）已替换为 `GameKey`、`PixelRect`、`IPlatformWindow`，Windows 行为不变。
- 主程序和 UI 仍是 WPF（`net10.0-windows`），尚未在 Linux 上原生运行；UI 迁移见路线图第 4 阶段。

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
