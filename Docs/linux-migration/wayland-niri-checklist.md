# niri（Wayland）实机测试清单

盒子上没有 GPU（无 `/dev/dri`）、没有 `/dev/input` 和 `/dev/uinput`，所以以下几项只能在实机上确认。
盒子上已验证的部分：嵌套 niri（winit 后端，跑在 Xvfb 上）+ xwayland-satellite 中的找窗 / 焦点 / 屏幕位置 / 截图 / XTest 输入（假游戏端到端 16 项通过）；
真实 xdg-desktop-portal 前端 + 模拟 ScreenCast 后端 + 真实 PipeWire 节点中的授权、restore_token 恢复和取帧。

每一步请记下结果（通过 / 失败 + 日志里的相关行），日志在 `User/log/`，启动时会打印一行“平台：…，探测：…”。

## 0. 准备

- [ ] `niri --version`（目标 v25.x 以上），`xwayland-satellite --version`；`echo $WAYLAND_DISPLAY $DISPLAY $NIRI_SOCKET` 三个都非空
- [ ] 安装：`xdg-desktop-portal xdg-desktop-portal-gnome gstreamer1.0-pipewire gstreamer1.0-tools gstreamer1.0-plugins-base`，
      `pipewire` 和 `wireplumber` 在运行（`systemctl --user status pipewire wireplumber`）
- [ ] `~/.config/xdg-desktop-portal/niri-portals.conf` 存在或使用发行版默认；`busctl --user introspect org.freedesktop.portal.Desktop /org/freedesktop/portal/desktop | grep ScreenCast` 有输出
- [ ] 权限：`id` 中有 `input` 组；`ls -l /dev/uinput` 为 `crw-rw---- root input`（命令见 MIGRATION.md「Wayland（niri）」）
- [ ] niri 配置里给游戏加 `open-floating true` 的 window-rule（同上），或者游戏用全屏

## 1. 后端选择

- [ ] 直接启动 `bgi-avalonia`：日志“平台”行为 `LinuxWayland`，探测项含 `compositor=niri`、`niri.outputs=…`、`x11.DISPLAY=:0`、`evdev=…`、`uinput=…`、`portal.screencast=…`
- [ ] `BGI_LINUX_BACKEND=x11 bgi-avalonia`：回到 X11 后端（`X11`），行为与以前一样
- [ ] 环境页显示同样的探测项；缺 `input` 组或 `/dev/uinput` 时提示里给出 usermod / udev 命令

## 2. 找窗口与焦点

- [ ] 游戏启动后 BGI 能找到窗口（日志“找到游戏窗口”），标题为“原神”
- [ ] 游戏不在前台时点“启动”，BGI 通过 niri 把游戏切到前台（`niri msg --json focused-window` 为游戏）
- [ ] 游戏切到别的工作区时，BGI 判断为“不在前台”，不发送输入

## 3. 截图

- [ ] 默认截图方式（日志显示 `GetImageWindow`；设置里选 ShmRoot / GetImageRoot 也会自动改成它）：实时触发能识别画面（例如自动拾取、F 对话）
- [ ] 截图里没有遮挡物 / 其它窗口；游戏被别的窗口盖住时是否仍正确（XWayland 下通常仍正确，请记录）
- [ ] 截图方式改为 `Portal`（设置页的下拉列表里没有这一项，避免 X11 下误选；请在 `User/linux-host.json` 中把 `CaptureMode` 改为 `"Portal"`，或用 `bgi-headless --capture Portal`）：第一次弹出 GNOME 的选择对话框，选游戏窗口 → 能截到图
- [ ] 重启后再次用 `Portal`：不再弹对话框（`~/.config/bettergi/screencast-restore-token` 存在）；删掉该文件后会重新弹
- [ ] Portal 选“整个显示器”时坐标是否对得上（目前按窗口来源设计，显示器来源需裁剪，请记录现象）
- [ ] 帧率 / CPU：Portal 方式下 `top` 中 `gst-launch-1.0` 的 CPU 占用（1080p）

## 4. 输入

- [ ] 游戏前台时 BGI 的按键（W/A/S/D、F、Space）和点击能被游戏收到（XTest 经 satellite）
- [ ] 视角转动（相对鼠标移动）正常；“鼠标绝对移动影响视角”的设置与 X11 下表现一致
- [ ] 原生 Wayland 窗口（例如云原神网页版在原生 Wayland 浏览器里）：uinput 输入能到达；点击位置与画面一致（多显示器、缩放 ≠ 1 时分别记录）

## 5. 全局热键

- [ ] 在 `input` 组里：游戏、浏览器、终端分别在前台时，BGI 热键都能触发
- [ ] 不在 `input` 组：日志 / 环境页有明确提示，热键退回 X11（只在 X 窗口有焦点时有效）
- [ ] BGI 自己发出的按键不会触发热键（虚拟设备名 `BetterGI virtual input` 被忽略）

## 6. 遮罩

- [ ] 已知：layer-shell 覆盖层（`LayerShellOverlay`）已实现但尚未接入遮罩，Wayland 下遮罩仍不显示。请确认打开遮罩时不会出现多余的平铺窗口干扰游戏；如有，记录现象
- [ ] 窗口化游戏顶部是否有 xwayland-satellite 加的标题栏（白条 + ×）；有的话截图发回（屏幕位置换算已按此处理）

## 7. 一条龙（可选）

- [ ] 浮动窗口模式下跑一条龙中 2–3 项（如邮件、合成），确认截图、输入、焦点全程正常

请把每项结果和对应日志片段发回，特别是失败项的“平台：…，探测：…”整行。
