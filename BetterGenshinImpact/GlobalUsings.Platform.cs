// 平台抽象层（BetterGenshinImpact.Core 程序集）中的跨平台类型：GameKey、PixelRect、IPlatformWindow、IGameCapture 等。
// 第一阶段迁移把接口签名中的 Win32 类型（User32.VK、RECT、nint hWnd）换成了这些类型，这里全局导入以减少逐文件改动。
global using BetterGenshinImpact.Platform;
