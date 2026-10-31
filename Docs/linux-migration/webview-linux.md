# Linux 网页视图方案评估（HTML 遮罩窗口与 JS HTML 设置页）

Windows 版用到网页的地方有两处：

| 用途 | Windows 实现 | 需要的能力 |
| --- | --- | --- |
| JS 脚本 HTML 设置页（`JsScriptHtmlSettingsWindow`） | WebView2，`WebResourceRequested` 拦截全部请求交给 Core 的 `ScriptSettingsRequestHandler` | 显示网页；**自定义响应**（不联网、不监听端口）；读取 POST 请求体；阻止跳转到其他地址 |
| 脚本 HTML 遮罩（`IHtmlMaskWindowHost` / `HtmlMaskWindow`） | 置顶、透明、可切换鼠标穿透的 WPF 窗口，内嵌 WebView2，页面通过宿主对象与脚本通信 | 透明背景；整窗鼠标穿透；跟随游戏窗口；脚本 ↔ 页面双向消息；频繁刷新 |

## 候选方案

| 方案 | 引擎 / 体积 | Linux 支持情况（2026 年 10 月） | 自定义协议 / 请求拦截 | 透明 + 穿透 | 结论 |
| --- | --- | --- | --- | --- | --- |
| **Avalonia.Controls.WebView**（官方 `NativeWebView` / `NativeWebDialog`，12.1.1，MIT） | 系统 WebKitGTK 4.1（或 WPE WebKit）；托管程序集约 1.4 MB，不随包分发浏览器 | Avalonia 12 起 `NativeWebView` 在 Linux 可内嵌：无 WPE 时用 WebKitGTK（X11 子窗口，或 `ExperimentalOffscreen` 离屏合成）；有 WPE 时离屏渲染。Debian/Ubuntu 只需 `libwebkit2gtk-4.1-0` | 公开 API 只有只读的 `WebResourceRequested`（不能返回自定义内容）；但 `TryGetPlatformHandle()` 给出 `WebKitWebView*`，可直接调用 WebKitGTK 的 `webkit_web_context_register_uri_scheme` 注册自定义协议（WebKitGTK 2.40 起可读 POST 请求体） | WebKitGTK 子窗口不能与 Avalonia 透明窗口合成；离屏模式能否保留透明度未验证；穿透需要整窗 X11 输入形状，与网页无关，可沿用现有遮罩的做法 | **推荐**。体积小、维护方是 Avalonia 官方，与现有界面同一框架 |
| 直接 P/Invoke WebKitGTK（自建 GTK 窗口） | 同上 | 可行，但需要自己维护 GTK 主循环与窗口嵌入 | 完全可控 | GTK 窗口可设 RGBA 视觉 + `webkit_web_view_set_background_color` 透明，再用 X11 输入形状穿透 | 作为遮罩窗口的后备方案：透明 + 穿透比 NativeWebView 更好控制，但工作量大 |
| CefGlue（`CefGlue.Avalonia`，Chromium 120 系列） | 随包分发 CEF，约 150–250 MB | 支持 Linux（离屏渲染 OSR） | `CefSchemeHandlerFactory` 完整支持自定义协议和请求拦截 | OSR 支持透明背景，可合成进 Avalonia | 能力最全，但体积和更新负担过重（Chromium 安全更新需跟随），不推荐作为默认依赖 |
| WebViewControl-Avalonia（基于 CefGlue） | 同 CefGlue | 同上 | 同上 | 同上 | 同上，额外依赖第三方封装 |
| 本机 HTTP 服务 + 系统浏览器 | 无额外依赖 | 可行 | 由服务端处理 | 不适用（无法做遮罩） | 违背 Windows 版“不监听端口”的设计，仅在完全没有 WebKitGTK 时作为设置页的退路考虑 |

## 本机验证（Debian 13，WebKitGTK 2.52.6，Xvfb）

- `WebViewAdapterInfo.GetAdapterInfo(WebKitGtk)`：已安装，场景 `NativeDialog`；`ExperimentalOffscreen = true` 时为 `OffscreenRenderer`。WPE 未安装。
- 两种模式下 `NativeWebView` 都能正常内嵌在 Avalonia 窗口中显示（含中文字体），`InvokeScript` 返回值、页面 `invokeCSharpAction` → `WebMessageReceived` 均正常。
- 通过平台句柄注册 `bgi-settings://` 自定义协议：入口 HTML、CSS、`GET /__bgi/settings`、`POST /__bgi/settings`（请求体完整读到）都由 C# 返回；响应头（内容安全策略）用 libsoup 设置后，页面访问外网被拦截。
- 截图见本地测试目录（不提交）。

## 建议

1. **JS HTML 设置页**：采用 `Avalonia.Controls.WebView` + WebKitGTK 自定义协议，已做最小实现（见下）。没有 WebKitGTK 时提示安装 `libwebkit2gtk-4.1-0`，不影响其他功能。
2. **HTML 遮罩窗口**：建议分两步：
   - 先用 `NativeWebView` 的离屏模式（`ExperimentalOffscreen`）在透明 Avalonia 遮罩窗口中验证透明度与刷新性能；鼠标穿透沿用现有 Avalonia 遮罩窗口的 X11 输入形状；脚本 → 页面用 `InvokeScript`，页面 → 脚本用 `invokeCSharpAction`；
   - 如果离屏模式不能保留透明背景，再改为直接 P/Invoke 创建 RGBA 的 GTK 窗口承载 WebKitWebView。
   - 不建议为遮罩引入 CefGlue。
   - 结论（里程碑 37）：离屏模式不能保留透明背景，已改用 RGBA 的 GTK 窗口，见下文“HTML 遮罩窗口”。
3. Wayland：WebKitGTK 路径依赖 XWayland（`ForceX11GdkBackend` 默认开启），与 BGI 当前只支持 X11/XWayland 一致；WPE 路径以后可用于纯 Wayland。

## 最小实现（里程碑 36）

- `BetterGenshinImpact.Avalonia/Services/WebKitSchemeHandler.cs`：在 WebKitWebView 的上下文注册 `bgi-settings://`（注册为安全、允许 CORS 的协议），回调中读方法、路径、请求体，交给 Core 的 `ScriptSettingsRequestHandler`，按其响应写回状态码、类型、内容安全策略等响应头。各设置页按 `ScriptSettingsHtmlPage.HostName` 区分。
- `BetterGenshinImpact.Avalonia/Views/HtmlSettingsWindow.cs`：对应 WPF `JsScriptHtmlSettingsWindow`，不保存网站数据；跳转到本页主机以外的地址一律取消，新窗口请求忽略；保存成功后标题显示保存时间；页面 `POST /__bgi/close` 关闭窗口。
- 配置组中“JS设置”：脚本只提供 `settings_html` 时打开该窗口（与 Windows 版相同，设置值保存到配置组文件）。
- 环境页显示网页引擎（WebKitGTK 版本或未安装原因）。
- 与 Windows 版的差异：`window.close()` 不会关闭窗口（NativeWebView 没有对应事件，页面需调用关闭接口）；开发者工具未开放。

## HTML 遮罩窗口（里程碑 37）

### NativeWebView 透明度验证

在透明、置顶的 Avalonia 窗口（`TransparencyLevelHint = Transparent`，实际透明级别 `Transparent`）中放一个 `NativeWebView`，页面背景透明，
画一个红色方块和一个半透明蓝色方块，在假游戏 + picom 上截屏比较（Xvfb，WebKitGTK 2.52.6）：

| 模式 | 页面空白处 | 半透明蓝块 |
| --- | --- | --- |
| 嵌入窗口（默认） | 白色（不透明） | 与白色混合 |
| 离屏 `ExperimentalOffscreen` | 白色（不透明） | 与白色混合 |
| 上面两种 + `webkit_web_view_set_background_color(透明)` | GTK 主题背景色（不透明） | 与背景色混合 |
| **GTK RGBA 顶层窗口 + WebKitWebView（透明背景）** | **游戏画面** | **与游戏画面正确混合** |

即 `NativeWebView` 两种模式都会画出不透明的背景，不能用于遮罩。

### 实现

- `Avalonia/Services/GtkHtmlMaskWindowHost`（`IHtmlMaskWindowHost` 的 Linux 实现，宿主启动时设为 `HtmlMaskWindowHost.Current`）：
  - 每个遮罩是一个 GTK 顶层窗口：RGBA 视觉、`app-paintable`、无边框、置顶、不在任务栏；WebKitWebView 背景色设为透明。
    GTK 调用都在 Avalonia 的 GTK 线程上执行（公开 API `Avalonia.X11.Interop.GtkInteropHelper.RunOnGlibThread`，与 `NativeWebView` 共用一个 GTK 主循环）。
  - 点击穿透：GTK 输入形状置空（与现有遮罩相同的 X11 SHAPE 输入区域），同时不接受焦点；关闭穿透时恢复输入形状并激活遮罩，重新穿透时把焦点还给游戏窗口。
  - 位置大小每 500 毫秒跟随游戏画面（截图器未启动时不移动）；最多 5 个窗口，指定 ID 时先关闭同 ID 的窗口；任务结束时由 Core 关闭全部。
  - 脚本目录映射到 `bgi-mask://hm-xxxx.bettergi.local/`（与 Windows 版的 https 虚拟主机同名，由 `WebKitSchemeHandler` 提供文件；路径或链接越出脚本目录时 403）。
  - 消息桥：文档创建时注入 `window.chrome.webview`（`postMessage` / `addEventListener('message')`，经 WebKit 脚本消息处理器转给宿主）和与 Windows 版相同的 `window.htmlMask`；脚本 → 页面用 `webkit_web_view_evaluate_javascript` 投递。
  - 网络请求：WebKit 内容过滤规则先拦截全部请求，再放行本页面虚拟主机、`data:` / `blob:`、页面自身地址，以及启用 JS HTTP 权限时 `http_allowed_urls` 中的地址（`*` 通配）。不允许打开新窗口和下载，`alert` 等对话框不弹出。
  - 网站数据（localStorage 等）保存在 `Data/WebKit/HtmlMask`，与其他网页隔离（对应 Windows 版的 HtmlMask 配置文件）。
- Core `HtmlMaskPage`：虚拟主机名、页面地址映射、页面消息解析、HTTP 权限校验与消息桥脚本，Windows 版 `HtmlMaskWindow` 改为调用它（行为不变）。

### 与 Windows 版的差异

- 需要合成器（如 picom），否则透明部分为黑色（与现有遮罩相同，启动时有警告）。
- 截图方式为“MIT-SHM 屏幕区域”等屏幕截图时，截图器画面会包含遮罩页面的内容；打开 HTML 遮罩时会提示改用“Composite 窗口图像”（或启用遮罩窗口，启用后自动改用）。Windows 截取的是游戏窗口本身，没有这个问题。
- 被拦截的网络请求不在日志中逐条记录（WebKit 内容过滤在网页进程中执行），页面中表现为请求失败；页面来源是 `bgi-mask://` 而不是 `https://`。

### 验证（假游戏 1920×1080 + picom，界面自测 `--ui-test html-mask`，16 项全部通过）

- 打开遮罩、默认穿透；页面 `htmlMask.request` → 脚本 `Respond` → 页面 Promise 完成；脚本 `Send` → 页面回复；脚本 `Request` → 页面返回值；
  页面读取脚本目录内文件成功，读取目录外文件失败，访问外网被拦截，`window.open` 返回空；关闭、切换穿透，关闭窗口。
- 屏幕截图：红块处为红色、半透明蓝块与游戏画面混合、其余为游戏画面；关闭后恢复游戏画面。
- 穿透时在红块上点击，假游戏收到点击、页面收不到；关闭穿透后页面收到点击。
- 遮罩打开时用截图方式 Composite 窗口图像抓帧，红块位置是游戏画面（截图器画面不含遮罩内容）；
  对照：截图方式为 MIT-SHM 屏幕区域时抓到红色（该项不通过，其余 15 项通过），并输出改用 Composite 的提示。

