# Linux 原生库构建

## libOpenCvSharpExtern.so

`build-opencvsharp.sh` 从源码编译 OpenCV 4.11.0（含 opencv_contrib）和 OpenCvSharpExtern（源码标签 4.11.0.20250507），生成与托管包 `OpenCvSharp4` 4.11.0.20250507 对齐的 Linux x64 原生库：

- 模块与官方 OpenCvSharp 构建一致，含 nonfree（SURF）。BGI 用到的有：
  - core / imgproc / imgcodecs；
  - dnn：OCR 预处理；
  - features2d（SIFT）/ xfeatures2d（SURF）/ flann / calib3d（FindHomography）：大地图特征匹配。
- 第三方库（zlib、libpng、libjpeg-turbo、libtiff、libwebp、OpenJPEG、OpenEXR、protobuf）用 OpenCV 自带源码静态链接，libstdc++ / libgcc 也静态链接。
- 产物只依赖 glibc 2.27 及以上，不依赖 tesseract / ffmpeg / gtk / gstreamer 等系统库。
- 没有图形界面后端：`Cv2.ImShow` 等窗口函数调用时会抛异常（BGI 只在调试 / 测试代码中使用）。

### 生成

在仓库根目录运行：

```bash
Build/Linux/build-opencvsharp.sh            # 工作目录默认 ./opencv-build（已在 .gitignore 中）
JOBS=8 Build/Linux/build-opencvsharp.sh /path/to/work
```

- 需要 `curl`、`tar`、`python3`。
- 本机没有 C++ 编译器、cmake 或 ninja 时，脚本会把 micromamba 和 conda-forge 的 gcc 13 / cmake / ninja 装到工作目录（无需 root），也可以设 `BOOTSTRAP=1` 强制使用。
- 源码与中间文件约 1.3 GB 磁盘，工具链另需约 1.3 GB。
- 产物在 `<工作目录>/out/libOpenCvSharpExtern.so`。

### 使用

`BetterGenshinImpact.Host.Common`（无界面宿主与 Avalonia 宿主都引用它）默认取仓库根目录下的 `opencv-build/out/libOpenCvSharpExtern.so`，复制到宿主输出目录的 `runtimes/linux-x64/native/`。放在其它位置时用构建属性（或同名环境变量）指定：

```bash
dotnet build BetterGenshinImpact.Host.Headless -p:OpenCvSharpExternPath=/path/to/work/out/libOpenCvSharpExtern.so
```

- 找不到这个文件时，退回 `Sdcb.OpenCvSharp4.mini.runtime.linux-x64`。它含 dnn，但没有 features2d / xfeatures2d 等模块，大地图特征匹配不可用。构建输出会提示当前使用的是哪个。
- 检查已加载的原生库：

```bash
bgi-headless --check-opencv
```

它会依次检查 SIFT / SURF 特征匹配、dnn、连通域、相位相关。
