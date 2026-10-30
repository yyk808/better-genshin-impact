#!/usr/bin/env bash
# 在 Linux x64 上从源码编译 OpenCV + opencv_contrib + OpenCvSharpExtern，生成 libOpenCvSharpExtern.so。
#
# - 版本与托管包 OpenCvSharp4 4.11.0.20250507 对齐（OpenCV 4.11.0，OpenCvSharp 源码标签 4.11.0.20250507）。
# - 模块与官方 OpenCvSharp 构建一致（含 contrib 与 nonfree：SURF），因为 OpenCvSharpExtern 的源码引用了全部模块。
# - OpenCV 静态编译，第三方库（zlib/png/jpeg/tiff/webp/openjpeg/protobuf 等）用 OpenCV 自带源码静态链接，
#   libstdc++ / libgcc 也静态链接；不依赖 tesseract / ffmpeg / gtk / gstreamer 等系统库，只依赖 glibc。
# - 没有图形界面后端：Cv2.ImShow 等 highgui 窗口函数调用时会抛异常（BGI 只在调试 / 测试代码中使用）。
#
# 用法：
#   Build/Linux/build-opencvsharp.sh [工作目录]        # 默认 ./opencv-build
# 环境变量：
#   JOBS=N               并行编译数（默认 CPU 核数）
#   BOOTSTRAP=1          用 micromamba 在工作目录中安装 conda-forge 的 gcc 13 / cmake / ninja（无需 root 和 apt）；
#                        本机没有 C++ 编译器、cmake 或 ninja 时自动启用
# 产物：<工作目录>/out/libOpenCvSharpExtern.so
# 使用：把产物路径传给构建属性 OpenCvSharpExternPath，例如
#   dotnet build BetterGenshinImpact.Host.Headless -p:OpenCvSharpExternPath=<工作目录>/out/libOpenCvSharpExtern.so
set -euo pipefail

OPENCV_VERSION=4.11.0
OPENCVSHARP_TAG=4.11.0.20250507
WORK=$(mkdir -p "${1:-./opencv-build}" && cd "${1:-./opencv-build}" && pwd)
JOBS=${JOBS:-$(nproc)}
SRC=$WORK/src
mkdir -p "$SRC" "$WORK/out"

log() { echo "[$(date +%T)] $*"; }

# 1. 工具链
if [[ "${BOOTSTRAP:-}" == 1 ]] || ! command -v c++ >/dev/null || ! command -v cmake >/dev/null || ! command -v ninja >/dev/null; then
  TOOLS=$WORK/tools
  if [[ ! -x $TOOLS/env/bin/cmake ]]; then
    log "安装工具链（micromamba + conda-forge gcc 13 / cmake / ninja）到 $TOOLS"
    mkdir -p "$TOOLS/bin"
    if [[ ! -x $TOOLS/bin/micromamba ]]; then
      curl -fsSL https://micro.mamba.pm/api/micromamba/linux-64/latest -o "$TOOLS/micromamba.tar.bz2"
      python3 -c "import tarfile,sys; tarfile.open(sys.argv[1],'r:bz2').extract('bin/micromamba', sys.argv[2])" \
        "$TOOLS/micromamba.tar.bz2" "$TOOLS"
    fi
    MAMBA_ROOT_PREFIX=$TOOLS/mamba "$TOOLS/bin/micromamba" create -y -p "$TOOLS/env" -c conda-forge \
      gxx_linux-64=13 gcc_linux-64=13 sysroot_linux-64=2.28 cmake ninja
  fi
  export PATH=$TOOLS/env/bin:$PATH
  export CC=$TOOLS/env/bin/x86_64-conda-linux-gnu-gcc CXX=$TOOLS/env/bin/x86_64-conda-linux-gnu-g++
  # 不让 conda 环境中的其它库被 find_package 找到
  export CMAKE_IGNORE_PREFIX_PATH=$TOOLS/env
fi
log "编译器：$(${CXX:-c++} --version | head -1)；cmake $(cmake --version | head -1 | awk '{print $3}')；并行 $JOBS"

# 2. 源码
fetch() { # url dir
  if [[ ! -d $SRC/$2 ]]; then
    log "下载 $1"
    curl -fsSL "$1" | tar -xz -C "$SRC"
  fi
}
fetch https://github.com/opencv/opencv/archive/refs/tags/$OPENCV_VERSION.tar.gz opencv-$OPENCV_VERSION
fetch https://github.com/opencv/opencv_contrib/archive/refs/tags/$OPENCV_VERSION.tar.gz opencv_contrib-$OPENCV_VERSION
fetch https://github.com/shimat/opencvsharp/archive/refs/tags/$OPENCVSHARP_TAG.tar.gz opencvsharp-$OPENCVSHARP_TAG

# 3. OpenCV（静态库，-fPIC）
OCV_BUILD=$WORK/build-opencv
OCV_INSTALL=$WORK/opencv-install
if [[ ! -f $OCV_INSTALL/.done ]]; then
  log "配置 OpenCV $OPENCV_VERSION"
  cmake -S "$SRC/opencv-$OPENCV_VERSION" -B "$OCV_BUILD" -G Ninja \
    -D CMAKE_BUILD_TYPE=Release \
    -D CMAKE_INSTALL_PREFIX="$OCV_INSTALL" \
    -D CMAKE_POSITION_INDEPENDENT_CODE=ON \
    -D OPENCV_EXTRA_MODULES_PATH="$SRC/opencv_contrib-$OPENCV_VERSION/modules" \
    -D BUILD_SHARED_LIBS=OFF \
    -D OPENCV_ENABLE_NONFREE=ON \
    -D BUILD_EXAMPLES=OFF -D BUILD_DOCS=OFF -D BUILD_PERF_TESTS=OFF -D BUILD_TESTS=OFF \
    -D BUILD_JAVA=OFF -D BUILD_opencv_apps=OFF -D BUILD_opencv_ts=OFF \
    -D BUILD_opencv_python2=OFF -D BUILD_opencv_python3=OFF \
    -D BUILD_opencv_java_bindings_generator=OFF -D BUILD_opencv_python_bindings_generator=OFF \
    -D BUILD_opencv_js=OFF -D BUILD_opencv_js_bindings_generator=OFF -D BUILD_opencv_objc_bindings_generator=OFF \
    -D BUILD_opencv_barcode=OFF -D BUILD_opencv_bioinspired=OFF -D BUILD_opencv_ccalib=OFF \
    -D BUILD_opencv_datasets=OFF -D BUILD_opencv_dnn_objdetect=OFF -D BUILD_opencv_dpm=OFF \
    -D BUILD_opencv_fuzzy=OFF -D BUILD_opencv_gapi=OFF -D BUILD_opencv_intensity_transform=OFF \
    -D BUILD_opencv_mcc=OFF -D BUILD_opencv_rapid=OFF -D BUILD_opencv_reg=OFF -D BUILD_opencv_stereo=OFF \
    -D BUILD_opencv_structured_light=OFF -D BUILD_opencv_surface_matching=OFF -D BUILD_opencv_videostab=OFF \
    -D BUILD_opencv_cvv=OFF -D BUILD_opencv_freetype=OFF -D BUILD_opencv_hdf=OFF -D BUILD_opencv_sfm=OFF \
    -D BUILD_opencv_viz=OFF -D BUILD_opencv_julia=OFF -D BUILD_opencv_ovis=OFF -D BUILD_opencv_matlab=OFF \
    -D BUILD_opencv_wechat_qrcode=ON \
    -D BUILD_ZLIB=ON -D BUILD_PNG=ON -D BUILD_JPEG=ON -D BUILD_TIFF=ON -D BUILD_WEBP=ON \
    -D BUILD_OPENJPEG=ON -D BUILD_OPENEXR=ON -D BUILD_PROTOBUF=ON -D PROTOBUF_UPDATE_FILES=OFF \
    -D WITH_GTK=OFF -D WITH_QT=OFF -D WITH_OPENGL=OFF -D WITH_VTK=OFF \
    -D WITH_FFMPEG=OFF -D WITH_GSTREAMER=OFF -D WITH_V4L=OFF -D WITH_1394=OFF -D WITH_AVIF=OFF \
    -D WITH_TESSERACT=OFF -D WITH_ADE=OFF -D WITH_EIGEN=OFF -D WITH_LAPACK=OFF -D WITH_OPENCL=OFF \
    -D WITH_VA=OFF -D WITH_VA_INTEL=OFF -D WITH_GPHOTO2=OFF -D WITH_IMGCODEC_HDR=ON \
    -D WITH_ITT=OFF -D WITH_IPP=ON -D BUILD_IPP_IW=ON -D WITH_TBB=OFF -D WITH_OPENMP=OFF \
    -D WITH_HDF5=OFF -D WITH_FREETYPE=OFF -D WITH_HARFBUZZ=OFF -D WITH_GDAL=OFF -D WITH_GDCM=OFF \
    -D WITH_JASPER=OFF -D WITH_OPENJPEG=ON -D WITH_SPNG=OFF -D WITH_ONNX=OFF -D WITH_CUDA=OFF \
    -D WITH_WAYLAND=OFF -D WITH_OBSENSOR=OFF -D WITH_CAROTENE=OFF \
    -D OPENCV_GENERATE_PKGCONFIG=OFF
  log "编译 OpenCV（耗时较长）"
  cmake --build "$OCV_BUILD" -j "$JOBS"
  cmake --install "$OCV_BUILD" >/dev/null
  touch "$OCV_INSTALL/.done"
fi

# 4. OpenCvSharpExtern（共享库，静态链接 OpenCV 与 libstdc++ / libgcc）
SHARP_BUILD=$WORK/build-opencvsharp
log "配置 OpenCvSharpExtern $OPENCVSHARP_TAG"
cmake -S "$SRC/opencvsharp-$OPENCVSHARP_TAG/src" -B "$SHARP_BUILD" -G Ninja \
  -D CMAKE_BUILD_TYPE=Release \
  -D CMAKE_PREFIX_PATH="$OCV_INSTALL" \
  -D OpenCV_DIR="$OCV_INSTALL/lib/cmake/opencv4" \
  -D CMAKE_POLICY_VERSION_MINIMUM=3.5 \
  -D CMAKE_CXX_FLAGS="-std=c++17" \
  -D CMAKE_SHARED_LINKER_FLAGS="-static-libstdc++ -static-libgcc -Wl,--exclude-libs,ALL -Wl,--as-needed"
log "编译 OpenCvSharpExtern"
cmake --build "$SHARP_BUILD" -j "$JOBS"
cp "$SHARP_BUILD/OpenCvSharpExtern/libOpenCvSharpExtern.so" "$WORK/out/"
strip --strip-unneeded "$WORK/out/libOpenCvSharpExtern.so" 2>/dev/null || true

log "完成：$WORK/out/libOpenCvSharpExtern.so"
echo "动态依赖："; ldd "$WORK/out/libOpenCvSharpExtern.so" || true
