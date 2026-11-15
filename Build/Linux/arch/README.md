# Arch Linux 打包

在本目录用 `makepkg` 生成可 `pacman -U` 安装的包，方便在自己的 Arch 系统上升级。这不是官方仓库或 AUR 包，也不需要为此改系统包管理器配置。

## 生成并安装

在 Arch（或能运行官方 `makepkg` 的环境）上：

```bash
cd Build/Linux/arch
makepkg -f
sudo pacman -U better-genshin-impact-0.66.3alpha1-1-x86_64.pkg.tar.zst
```

构建依赖是官方 extra 中的 `dotnet-sdk-10.0`；运行依赖是 `dotnet-runtime-10.0` 与 `aspnet-runtime-10.0`（runtimeconfig 需要 Microsoft.NETCore.App 与 Microsoft.AspNetCore.App 10，不能只依赖前者）。版本号 `0.66.3alpha1` 对应程序的 `0.66.3-alpha.1`（pacman 的 pkgver 不能含连字符）。以后只提高 `pkgver` / `pkgrel`，不要回退。

包装**必须**带上与托管 OpenCvSharp 匹配的完整 `libOpenCvSharpExtern.so`（见上级 [README.md](../README.md)）。系统 `opencv` 包不提供该桥接库；缺少完整库时 `makepkg` 会按仓库相对路径运行 `Build/Linux/build-opencvsharp.sh`（工作目录 `opencv-build/`）。脚本失败则拒绝出包，不会产出缺库或 SIFT 不可用的包。不要依赖仅本机存在的绝对路径，也不要把该 `.so` 提交进 git。构建时按以下顺序获取：

1. 环境变量 `OpenCvSharpExternPath`（若已指向一份完整库）
2. 仓库内 `opencv-build/out/libOpenCvSharpExtern.so`
3. `Build/Linux/libOpenCvSharpExtern.so`
4. 以上皆无时，调用官方脚本从源码构建到 `opencv-build/out/`（需要 `curl`、`tar`、`python3`；本机没有 gcc/cmake/ninja 时脚本会在工作目录自举工具链）

该原生库复制到程序根目录（`/usr/lib/better-genshin-impact/libOpenCvSharpExtern.so`），以便 OpenCvSharp 的 DllImport 从 `AppContext.BaseDirectory` 加载；不要只放在 `runtimes/linux-x64/native`（deps.json 不会为这项 None 资源登记 native）。不要把 `.so` 提交进 git。构建会实际运行 `bgi-avalonia --check-opencv`（加载原生库并做 SIFT/SURF/dnn），不能只检查文件存在。库在包内分发，并附带本目录安装的许可证说明；第三方源码许可见 OpenCV / OpenCvSharp 上游。

包装脚本还会构建 Windows 主项目（仅用于复制 CopyToOutputDirectory 的内容），把 Assets/（含 BetterGI.Assets.Map / Model / Other）、GameTask/ 识别图和仓库内默认的 User/I18n、示例脚本放入 /usr/lib/better-genshin-impact。首次启动会把其中尚不存在的模板复制到用户数据目录，已有配置和脚本不会被覆盖。包不把本机用户数据、凭据或测试工具打进去。

## 启动与数据目录

- 命令行入口：`better-genshin-impact`（并提供 `bgi-avalonia` 符号链接）
- 桌面入口：`better-genshin-impact.desktop`，图标来自程序自带 logo
- 程序与只读资源：`/usr/lib/better-genshin-impact`（由包装脚本以 `--assets` 传入）
- 用户可写数据：`~/.local/share/better-genshin-impact`（或 `$XDG_DATA_HOME/better-genshin-impact`，可用 `BGI_DATA_DIR` / `--data-dir` 覆盖），包括 `User/`（配置、脚本、账号）、`Data/`（含 WebKit）、`log/`、`Repos/`

`pacman -U` 升级只替换 `/usr` 下的程序文件，不会覆盖上述用户目录。卸载同样保留用户数据。

开发或便携布局（程序目录可写、且未指定 `--data-dir` / `BGI_DATA_DIR`）仍把数据写在程序目录，行为与原先一致。Windows 不受影响。

## 可选功能（均为官方 extra 包，不是硬依赖）

- **CPU 推理**：开箱可用，无 CUDA 依赖
- **ROCm**：`onnxruntime-rocm`（系统库；版本可能与托管包不完全相同）
- **niri / 录屏**：`niri`、`xdg-desktop-portal`、`xdg-desktop-portal-gnome`、`pipewire`、`xwayland-satellite`
- **通知**：`libnotify`、`mako`
- **账号保管**：`libsecret`
- **图文攻略 / 云原神页**：`webkit2gtk-4.1`
- **剧情语音**：`espeak-ng`

## 核对

`verify-layout.sh` 检查安装清单、权限、启动路径，以及用户数据不会被打进包。需要一份 `dotnet publish` 输出：

```bash
Build/Linux/arch/verify-layout.sh /path/to/publish
```

若当前环境没有 `makepkg` / `fakeroot` / `pacman`，请不要用普通 tar 冒充 `.pkg.tar.zst`；把本目录带到 Arch 上再构建。
