# BetterGenshinImpact.Avalonia

Linux 上的图形界面宿主（预览）。纯 `net10.0`，Avalonia 12 + Avalonia 自带的 Fluent 主题（深色）。

启动引导（平台服务、配置、依赖注入、CPU 推理、截图器启动 / 停止）与无界面宿主共用
`BetterGenshinImpact.Host.Common` 的 `GameHost`；本项目只负责界面和 `UiHost` 的 Avalonia 实现。

## 当前内容

页面与窗口对照 WPF 原版，业务都调用 Core（与 Windows 版同一套代码）：

| 部分 | 说明 |
| --- | --- |
| 主窗口外壳 | 左侧导航 + 内容区 + 底部日志面板，配色和卡片样式参照原版（WPF-UI 深色） |
| 启动 | 启动 / 停止截图器、截图方式、手动选择捕获窗口、同时启动原神（Wine / Proton 命令可配置）、云原神（网页版） |
| 实时触发 | 自动拾取、自动剧情、自动邀约、自动开门、半自动钓鱼、自动吃药、快速传送、自动配音（实验性） |
| 独立任务 | 任务卡片与专属参数编辑器（秘境、首领、地脉花、幽境危战、体力计划等）、树脂设置对话框 |
| 一条龙 | 选择配置、开关任务、启动 / 停止 |
| 全自动 | 调度器（配置组、JS 脚本设置、HTML 设置页）、JS 脚本、地图追踪、键鼠录制回放 |
| 任务计划 | 计划编辑、触发方式与快捷键触发、共享设置与设置方案、路线 CD 记录、计划引用 |
| 设置 | 通用设置、快捷键（含开发者快捷键：模板素材制作、路径记录器）、通知（系统通知、Webhook、企业微信、邮件，可单独测试） |
| 环境 | 平台探测（显示服务器、X 扩展）、推理设备、OpenCV 原生库与模块自检、音频输出、网页引擎、界面字体、素材目录 |
| 遮罩 | 识别框、日志框、HTML 遮罩（透明、置顶、点击穿透，需要合成器） |
| `UiHost` | `AvaloniaUiDispatcher`、`AvaloniaDialogService`（模态消息框 + 窗口内轻提示）、`AvaloniaClipboard` |

尚未迁移的窗口和与 Windows 的差异见 `Docs/linux-migration/MIGRATION.md`。

实时触发页直接绑定 Core 的 `AllConfig`，与 WPF 的 `TriggerSettingsPageViewModel` 绑定的是同一批配置属性；
触发器每帧读取 `IsEnabledByConfig`，开关立即生效。

## 依赖

除 .NET 10 外，运行需要 X11 / XWayland 会话（libX11、XTest、XInput2、Composite 等）和中文字体；网页设置页、HTML 遮罩、云原神需要
`libwebkit2gtk-4.1-0`；遮罩透明需要合成器（如 picom）；剧情语音播放需要 libpulse；完整的 OpenCV 功能需要自行编译的
OpenCvSharpExtern（见 `Build/Linux/README.md`）。完整列表见 `Docs/linux-migration/MIGRATION.md`。

## 配置文件

- `User/config.json`：与 Windows 主程序格式相同，两边可以互相拷贝；改动后 500 ms 防抖写入（临时文件 + 改名），退出时写完。
- `User/linux-host.json`：Linux 专有设置（截图方式、同时启动游戏的命令、游戏路径、工作目录、环境变量、等待超时、遮罩窗口开关）。

命令行开关和 Linux 上强制 CPU 推理只在本次运行有效，保存时写回文件原值（界面上改过的设置照常保存）。

`User` 目录在素材目录下（`--assets`），也可以用 `--config 文件` 指定，`linux-host.json` 放在同一目录。

## 主题选择

用 Avalonia 自带的 `FluentTheme`，没有用 FluentAvalonia：

- 自带主题与 Avalonia 同版本发布（12.1.4），已在本地包缓存中，不增加依赖；
- FluentAvalonia 的发布跟随 Avalonia 11，尚无与 12.x 对应的版本；
- 原版的 NavigationView / CardControl / CardExpander 外观用 `ListBox`、`Border`、`Expander` 加样式即可还原（`Styles.axaml`）。

## 中文字体

主窗口按优先级在系统字体中选第一个可用的中文字体（Noto Sans CJK SC、思源黑体、文泉驿等，见 `Services/FontInfo.cs`），
同时把这些字体加入 Avalonia 的字体回退。一个都没有时界面会显示方块，环境页会提示安装，例如：

```bash
sudo apt install fonts-noto-cjk      # Debian / Ubuntu
sudo pacman -S noto-fonts-cjk        # Arch
```

## 运行

```bash
dotnet build BetterGenshinImpact.Avalonia
dotnet BetterGenshinImpact.Avalonia/bin/Debug/net10.0/bgi-avalonia.dll --assets <Windows 主程序输出目录>
```

素材、模型从 Windows 主程序的输出目录读取（与无界面宿主相同）。OpenCvSharpExtern 原生库见 `Build/Linux/README.md`。

| 参数 | 说明 |
| --- | --- |
| `--display :0` | X 显示，默认取 `DISPLAY` |
| `--assets 目录` | 素材 / 模型 / User 目录的根目录 |
| `--process 名称,名称` | 游戏进程名，默认 `YuanShen,GenshinImpact` |
| `--interval 毫秒` | 触发器间隔，默认 50 |
| `--pick` / `--no-pick`、`--skip` / `--no-skip` | 打开 / 关闭自动拾取、自动剧情（只在本次运行有效，不写回配置文件，下同） |
| `--config 文件` | 配置文件路径，默认 `<素材目录>/User/config.json` |
| `--no-save-config` | 不读写配置文件，只用内存中的默认配置 |
| `--capture 方式` | 截图方式：`ShmRoot`（默认）、`GetImageRoot`、`GetImageWindow`、`CompositeWindow`（Composite 窗口图像，不受遮挡和遮罩影响） |
| `--auto-enter` / `--no-auto-enter` | 自动开门初始开关 |
| `--log 文件` | 同时写日志文件 |
| `--page 页面名` | 启动后切换到指定页面（如 `实时触发`） |
| `--auto-start` | 窗口打开后自动启动截图器 |
| `--screenshots 目录` | 无人值守：运行一段时间后依次切换页面保存界面截图 |
| `--exit-after 秒` | 无人值守：到时退出 |
| `--ui-selftest` | `UiHost` 自检：后台线程调度、剪贴板读回、配置防抖保存、轻提示、消息框 |
| `--mask` / `--no-mask` | 打开 / 关闭遮罩窗口（透明、置顶、点击穿透，显示识别框和日志框；需要合成器；启用时截图默认改用 `CompositeWindow`） |
| `--mask-recognition` | 遮罩上显示全部识别结果（调试） |
| `--cloud 地址` | 本次运行使用云原神（网页版）：启动截图器时打开 WebKitGTK 云原神窗口，进入游戏后截取该窗口，键鼠经页面 SDK（`ys-input-inject.js`，与 Windows 网页版实例相同）发送；不写回设置。界面上在“启动”页的“云原神（网页版）”中开启 |
| `--select-window 标题` | 无人值守：打开窗口选择对话框，选中标题匹配的窗口（并截图） |
| `--solo-task 任务标识` / `--solo-stop-after 秒` | 无人值守：在独立任务页启动该任务，到时停止并等待结束（并截图） |
| `--ui-test 名称` | 界面自测（开发用）：按名称运行一组界面操作并核对结果，例如 `taskplan`、`script-settings`、`html-settings`、`solo-domain` |
