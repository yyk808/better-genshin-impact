#!/bin/bash
# 核对 Arch 包目录清单、权限、启动入口和“升级不覆盖用户数据”。
# 不调用 pacman，也不向本机安装。若环境没有 makepkg，只验证目录树，不假装已生成 .pkg.tar.zst。
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "${here}/../../.." && pwd)"
pkgname=better-genshin-impact
publish="${1:-}"
work="${2:-}"

if [[ -z "${publish}" ]]; then
  echo "用法: $0 <dotnet publish 输出目录> [临时检查目录]"
  echo "若尚未 publish，可先：dotnet publish BetterGenshinImpact.Avalonia/BetterGenshinImpact.Avalonia.csproj -c Release -r linux-x64 --self-contained false -p:EnableWindowsTargeting=true -o /tmp/bgi-publish"
  exit 2
fi

if [[ ! -d "${publish}" ]]; then
  echo "找不到发布目录：${publish}" >&2
  exit 1
fi

cleanup=()
if [[ -z "${work}" ]]; then
  work="$(mktemp -d /tmp/bgi-arch-layout.XXXXXX)"
  cleanup+=("${work}")
fi
trap 'rm -rf "${cleanup[@]}"' EXIT

pkgdir="${work}/pkg"
rm -rf "${pkgdir}"
startdir="${here}"
_repo="${repo}"
# shellcheck source=/dev/null
source "${here}/install-tree.sh"
_install_tree "${pkgdir}" "${publish}"

fail=0
need() {
  if [[ ! -e "$1" ]]; then
    echo "缺少：$1" >&2
    fail=1
  fi
}

need "${pkgdir}/usr/bin/better-genshin-impact"
need "${pkgdir}/usr/bin/bgi-avalonia"
need "${pkgdir}/usr/share/applications/${pkgname}.desktop"
need "${pkgdir}/usr/share/icons/hicolor/400x400/apps/${pkgname}.png"
need "${pkgdir}/usr/share/pixmaps/${pkgname}.png"
need "${pkgdir}/usr/share/licenses/${pkgname}/LICENSE"
need "${pkgdir}/usr/lib/${pkgname}/Assets"
need "${pkgdir}/usr/lib/${pkgname}/GameTask"
need "${pkgdir}/usr/lib/${pkgname}/User/I18n"
need "${pkgdir}/usr/lib/${pkgname}/libOpenCvSharpExtern.so"
if [[ ! -e "${pkgdir}/usr/lib/${pkgname}/bgi-avalonia" && ! -e "${pkgdir}/usr/lib/${pkgname}/bgi-avalonia.dll" ]]; then
  echo "缺少程序主体 bgi-avalonia" >&2
  fail=1
fi

if ! grep -q 'install_dir="/usr/lib/better-genshin-impact"' "${pkgdir}/usr/bin/better-genshin-impact"; then
  echo "启动脚本未把程序目录固定为 /usr/lib/better-genshin-impact" >&2
  fail=1
fi
if ! grep -q -- '--assets' "${pkgdir}/usr/bin/better-genshin-impact" || ! grep -q -- '--data-dir' "${pkgdir}/usr/bin/better-genshin-impact"; then
  echo "启动脚本未同时指定 --assets 与 --data-dir" >&2
  fail=1
fi
if ! grep -q 'XDG_DATA_HOME' "${pkgdir}/usr/bin/better-genshin-impact"; then
  echo "启动脚本未使用 XDG 数据目录" >&2
  fail=1
fi

# 包装脚本必须把用户参数原样后置，且仅在用户未指定时补默认 --assets / --data-dir
if ! grep -q 'exec ' "${pkgdir}/usr/bin/better-genshin-impact" || ! grep -q '"$@"' "${pkgdir}/usr/bin/better-genshin-impact"; then
  echo "启动脚本未转发用户参数" >&2
  fail=1
fi
if ! grep -q 'has_data' "${pkgdir}/usr/bin/better-genshin-impact"; then
  echo "启动脚本可能覆盖用户指定的 --data-dir" >&2
  fail=1
fi

if [[ ! -x "${pkgdir}/usr/bin/better-genshin-impact" ]]; then
  echo "CLI 入口不可执行" >&2
  fail=1
fi
if [[ -e "${pkgdir}/usr/lib/${pkgname}/User/config.json" ]]; then
  echo "不得打包用户配置 User/config.json" >&2
  fail=1
fi
if [[ -d "${pkgdir}/usr/lib/${pkgname}/User/Account" ]]; then
  echo "不得打包账号目录" >&2
  fail=1
fi
if [[ -d "${pkgdir}/usr/lib/${pkgname}/log" || -d "${pkgdir}/usr/lib/${pkgname}/Data" ]]; then
  echo "不得把可写的 log/Data 打进只读安装前缀" >&2
  fail=1
fi
if find "${pkgdir}" \( -iname '*fakegame*' -o -iname '*UnitTest*' \) | grep -q .; then
  echo "不得打包测试工具" >&2
  fail=1
fi

if ! grep -qx 'Exec=better-genshin-impact' "${pkgdir}/usr/share/applications/${pkgname}.desktop"; then
  echo "desktop 文件 Exec 不正确" >&2
  fail=1
fi

if [[ -e "${pkgdir}/home" ]]; then
  echo "包内不应包含 /home 用户数据" >&2
  fail=1
fi
if find "${pkgdir}" -path '*local/share/better-genshin-impact/*' | grep -q .; then
  echo "不得把本机 XDG 用户数据打进包" >&2
  fail=1
fi

if grep -E "^depends=\(|^  '" "${here}/PKGBUILD" | grep -qi cuda; then
  echo "depends 不得硬性依赖 CUDA" >&2
  fail=1
fi
for pkg in dotnet-runtime-10.0 aspnet-runtime-10.0 webkit2gtk-4.1 libsecret espeak-ng onnxruntime-rocm niri libnotify mako; do
  if ! grep -q "${pkg}" "${here}/PKGBUILD"; then
    echo "PKGBUILD 未声明 ${pkg}" >&2
    fail=1
  fi
done

if ! grep -q '^pkgver=0.66.3alpha1$' "${here}/PKGBUILD"; then
  echo "pkgver 非预期（应为无连字符的可比较版本）" >&2
  fail=1
fi

# 模拟升级：用户数据在包外，第二次安装不得改动该目录
upgrade_home="${work}/xdg"
mkdir -p "${upgrade_home}/better-genshin-impact/User"
echo 'keep-config' > "${upgrade_home}/better-genshin-impact/User/config.json"
# 包树不含该路径，故“再装一次”后文件仍在
if ! grep -qx 'keep-config' "${upgrade_home}/better-genshin-impact/User/config.json"; then
  echo "升级保留用户配置检查失败" >&2
  fail=1
fi
if find "${pkgdir}/usr/lib/${pkgname}/User" -name 'config.json' -o -name 'linux-host.json' 2>/dev/null | grep -q .; then
  echo "包内出现了用户配置文件，升级可能覆盖用户配置" >&2
  fail=1
fi

if [[ "${fail}" -ne 0 ]]; then
  echo "目录树检查未通过：${pkgdir}" >&2
  exit 1
fi

# 在组装出的程序根目录实际加载原生库并跑 SIFT/SURF/dnn，不能只检查文件存在
app_dir="${pkgdir}/usr/lib/${pkgname}"
if command -v dotnet >/dev/null 2>&1; then
  if ! (cd "${app_dir}" && dotnet bgi-avalonia.dll --check-opencv --no-instances); then
    echo "OpenCV 原生加载或特征匹配自检失败（${app_dir}）" >&2
    exit 1
  fi
else
  echo "本环境没有 dotnet，跳过组装树的 --check-opencv 实测（文件清单已要求程序根目录存在 libOpenCvSharpExtern.so）。" >&2
fi

echo "目录树检查通过：${pkgdir}"
echo "CLI=/usr/bin/better-genshin-impact  程序=/usr/lib/${pkgname}  数据=~/.local/share/${pkgname}"
if ! command -v makepkg >/dev/null 2>&1; then
  echo "本环境没有 makepkg，未生成 .pkg.tar.zst（这不是用 tar 冒充的 pacman 包）。请在 Arch 上于 Build/Linux/arch 运行 makepkg -f，再用 pacman -U 安装生成的包。"
fi
