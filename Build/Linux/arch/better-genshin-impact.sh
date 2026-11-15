#!/bin/bash
# BetterGI Linux 启动入口：程序目录只读；未指定时用户数据写到 XDG 数据目录。
# 用户在命令行给出的 --assets / --data-dir / --data-directory 优先，本脚本不会覆盖。
set -euo pipefail

install_dir="/usr/lib/better-genshin-impact"

has_assets=0
has_data=0
for arg in "$@"; do
  case "$arg" in
    --assets) has_assets=1 ;;
    --data-dir|--data-directory) has_data=1 ;;
  esac
done

extra=()
if [[ "$has_assets" -eq 0 ]]; then
  extra+=(--assets "$install_dir")
fi
if [[ "$has_data" -eq 0 ]]; then
  if [[ -n "${BGI_DATA_DIR:-}" ]]; then
    data_dir="$BGI_DATA_DIR"
  else
    data_dir="${XDG_DATA_HOME:-$HOME/.local/share}/better-genshin-impact"
  fi
  mkdir -p "$data_dir"
  extra+=(--data-dir "$data_dir")
fi

exec "$install_dir/bgi-avalonia" "${extra[@]}" "$@"
