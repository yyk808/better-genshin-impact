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
3. Wayland：WebKitGTK 路径依赖 XWayland（`ForceX11GdkBackend` 默认开启），与 BGI 当前只支持 X11/XWayland 一致；WPE 路径以后可用于纯 Wayland。

## 最小实现（里程碑 36）

- `BetterGenshinImpact.Avalonia/Services/WebKitSchemeHandler.cs`：在 WebKitWebView 的上下文注册 `bgi-settings://`（注册为安全、允许 CORS 的协议），回调中读方法、路径、请求体，交给 Core 的 `ScriptSettingsRequestHandler`，按其响应写回状态码、类型、内容安全策略等响应头。各设置页按 `ScriptSettingsHtmlPage.HostName` 区分。
- `BetterGenshinImpact.Avalonia/Views/HtmlSettingsWindow.cs`：对应 WPF `JsScriptHtmlSettingsWindow`，不保存网站数据；跳转到本页主机以外的地址一律取消，新窗口请求忽略；保存成功后标题显示保存时间；页面 `POST /__bgi/close` 关闭窗口。
- 配置组中“JS设置”：脚本只提供 `settings_html` 时打开该窗口（与 Windows 版相同，设置值保存到配置组文件）。
- 环境页显示网页引擎（WebKitGTK 版本或未安装原因）。
- 与 Windows 版的差异：`window.close()` 不会关闭窗口（NativeWebView 没有对应事件，页面需调用关闭接口）；开发者工具未开放。
