# Linux ROCm（AMD 显卡）推理：实机检查清单

BetterGI Linux 版的推理引擎可选 **CPU**（默认，随程序发布的 onnxruntime）或 **ROCm**（AMD 显卡）。
ROCm 不自带运行库，也不下载：直接用系统里已安装、带 **MIGraphX** 的 onnxruntime 共享库
（onnxruntime 1.23 起删除了旧的 ROCm provider，AMD 显卡改用 MIGraphX provider）。

工作方式（代码见 `Host.Common/LinuxOnnxRuntime.cs`、`Core/Recognition/ONNX/BgiOnnxFactory.cs`）：

1. 设置页“硬件加速（推理引擎）”选 ROCm →“应用”，重启 BetterGI 后生效。
2. 启动时：有 `/dev/kfd`，并在下列位置找到 `libonnxruntime.so(.1)`、且同目录有 `libonnxruntime_providers_migraphx.so`（或旧的 `_rocm.so`）：
   环境变量 `BGI_ONNXRUNTIME_LIB` 指定的文件 → `/usr/lib` → `/usr/lib64` → `/usr/lib/x86_64-linux-gnu` → `/usr/local/lib` → `/opt/rocm/lib`。
   找到后用 `NativeLibrary.SetDllImportResolver` 让托管 onnxruntime 加载这个系统库。
3. 会话依次尝试 MIGraphX provider（`OrtSessionOptionsAppendExecutionProvider_MIGraphX`）、旧 ROCm provider，CPU 兜底不支持的算子。
4. 首次使用会用 OCR / 古树模型实际跑一次验证；任何一步失败都自动回退 CPU，原因写在设置页“硬件加速”和日志里（与 Windows 版运行库失败的处理相同）。

## Arch / Manjaro / EndeavourOS（推荐）

```bash
sudo pacman -S onnxruntime-rocm      # extra 仓库，1.29.0；会替换 onnxruntime（它 provides onnxruntime），依赖 migraphx、rocm-hip-sdk、rccl
sudo usermod -aG render,video "$USER" # 重新登录后生效
ls -l /dev/kfd /dev/dri/renderD*      # 应存在且当前用户可读写
ls /usr/lib/libonnxruntime.so.1 /usr/lib/libonnxruntime_providers_migraphx.so
```

可选核对：`rocminfo | grep -m3 gfx`（`rocminfo` 在 `rocminfo` 包里）能列出显卡的 gfx 型号。

## 其他发行版

需要两样东西：ROCm 运行时（含 MIGraphX），和**带 MIGraphX 编译的 onnxruntime 共享库**（`libonnxruntime.so` + `libonnxruntime_providers_migraphx.so` + `libonnxruntime_providers_shared.so`）。

- **Ubuntu / Debian**：按 AMD 文档装 ROCm（`amdgpu-install --usecase=rocm`，并 `sudo apt install migraphx`）。
  AMD 的 apt 源目前不提供 onnxruntime 的 C 共享库（PyPI 的 `onnxruntime-rocm` / `onnxruntime-migraphx` 轮子把 onnxruntime 静态链进 Python 模块，
  里面没有可用的 `libonnxruntime.so`）。可从 AMD ROCm 的 onnxruntime 容器镜像或自己的构建里取出上述三个 .so，放到一个目录，
  启动前 `export BGI_ONNXRUNTIME_LIB=/那个目录/libonnxruntime.so.1`（或 `.so`）。
- **Fedora**：`sudo dnf install rocm-hip rocm-runtime migraphx`（视版本而定）；onnxruntime 的 MIGraphX 版同样需要自备共享库并用 `BGI_ONNXRUNTIME_LIB` 指定。
- **openSUSE / 其它**：同上——装 ROCm + MIGraphX，提供带 MIGraphX 的 libonnxruntime 并用 `BGI_ONNXRUNTIME_LIB` 指定。
- 共享库版本 ≥ 1.21 即可（BetterGI 编译时用的托管 onnxruntime 是 1.21，C API 向后兼容）。

## 实机核对步骤

1. 先用 CPU 正常启动一次，确认截图、OCR 正常。
2. 设置 → 通用 →“硬件加速（推理引擎）”：“ROCm 检测”一行应显示“检测到 /dev/kfd 与系统 onnxruntime：/usr/lib/libonnxruntime.so.1”。
3. 选“ROCm（AMD 显卡，MIGraphX）”→ 应用 → 重启 BetterGI → 启动截图器。
4. 日志应有：`推理：ROCm 使用系统 onnxruntime /usr/lib/libonnxruntime.so.1`、`[ONNX]当前引擎 ROCm，设备 AMD 显卡（ROCm 设备 0）`，
   没有“ROCm 加速验证失败”。设置页“当前：ROCm · AMD 显卡（ROCm 设备 0）”。
5. 首次加载模型时 MIGraphX 要编译，可能需要几十秒到几分钟，属正常；之后同一会话内推理应明显快于 CPU（可用自动秘境 / 古树识别对比帧率或耗时）。
6. 若回退了 CPU，按设置页里的原因处理：
   - “没有 /dev/kfd”：没装 amdgpu / ROCm 内核驱动，或用户不在 render、video 组。
   - “没有找到带 MIGraphX 的系统 onnxruntime”：装 `onnxruntime-rocm`，或设置 `BGI_ONNXRUNTIME_LIB`。
   - “onnxruntime 不带 MIGraphX / ROCm provider”：找到的是 CPU 版 onnxruntime（如 Arch 的普通 `onnxruntime` 包）。
   - 验证阶段报 hipErrorNoBinaryForGpu / 不支持的 gfx：消费级显卡可能不在 ROCm 官方支持列表，
     可试 `export HSA_OVERRIDE_GFX_VERSION=11.0.0`（RDNA3）或 `10.3.0`（RDNA2）后再启动。
7. 想换回 CPU：设置页选 CPU → 应用 → 重启。

## 已在盒子上验证的部分（无 AMD 显卡）

- 设置页只列 CPU 与 ROCm，选 ROCm 写入配置（`rocm.deviceId = ROCm:0`）、提示重启后生效（界面自测 `inference-select`）。
- 没有 `/dev/kfd`、找不到系统库、库里没有 MIGraphX/ROCm provider 三种情况都回退 CPU、记录失败引擎并显示原因，OCR 照常（`inference-fallback`）。
- 真正在 AMD 显卡上用 MIGraphX 推理需要实机按上面步骤核对。
