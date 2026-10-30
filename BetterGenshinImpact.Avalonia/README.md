# BetterGenshinImpact.Avalonia

Linux 上的图形界面宿主（预览）。纯 `net10.0`，Avalonia 12 + Avalonia 自带的 Fluent 主题（深色）。

启动引导（平台服务、配置、依赖注入、CPU 推理、截图器启动 / 停止）与无界面宿主共用
`BetterGenshinImpact.Host.Common` 的 `GameHost`；本项目只负责界面和 `UiHost` 的 Avalonia 实现。

## 当前内容

| 部分 | 说明 |
| --- | --- |
| 主窗口外壳 | 左侧导航 + 内容区 + 底部日志面板，配色和卡片样式参照原版（WPF-UI 深色） |
| 启动 | 启动 / 停止截图器；截图模式、触发器间隔、手动选择捕获窗口（列出 X11 窗口）；同时启动原神（Wine / Proton 等命令可配置）；游戏窗口、已处理帧数 |
| 实时触发 | 自动拾取（极速模式、黑白名单模式、拾取按键）、自动剧情（快速跳过、选项优先级等）、自动邀约、自动开门、半自动钓鱼、自动吃药、快速传送 |
| 环境 | 平台探测结果（显示服务器、X 扩展）、推理设备、OpenCV 版本与原生库、OpenCV 模块自检、界面字体、素材目录 |
| 日志面板 | Serilog 日志实时显示，可切换调试日志、复制（经 `UiHost.Clipboard`）、清空、收起 |
| `UiHost` | `AvaloniaUiDispatcher`（`Dispatcher.UIThread`）、`AvaloniaDialogService`（模态消息框 + 窗口内轻提示）、`AvaloniaClipboard`（`TopLevel.Clipboard`） |

“独立任务”页列出独立任务卡片（关键参数、启动/停止），“一条龙”页可选择配置、开关任务并启动/停止（详细设置界面尚未迁移）。
其余导航项（任务计划、全自动、设置）目前是说明页。

实时触发页直接绑定 Core 的 `AllConfig`，与 WPF 的 `TriggerSettingsPageViewModel` 绑定的是同一批配置属性；
触发器每帧读取 `IsEnabledByConfig`，开关立即生效。

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
| `--select-window 标题` | 无人值守：打开窗口选择对话框，选中标题匹配的窗口（并截图） |
| `--solo-task 任务标识` / `--solo-stop-after 秒` | 无人值守：在独立任务页启动该任务，到时停止并等待结束（并截图） |
