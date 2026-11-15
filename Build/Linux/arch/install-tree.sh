# 由 PKGBUILD package() 与 verify-layout.sh 共用：把 publish 目录装到目标根。
# 调用前需设置：pkgname、startdir、_repo
# 用法：_install_tree <destdir> <publish-dir>

_install_tree() {
  local dest="$1"
  local publish="$2"
  install -d "${dest}/usr/lib/${pkgname}"
  cp -a "${publish}/." "${dest}/usr/lib/${pkgname}/"

  # 不打包本机用户数据、凭据或测试工具
  rm -rf \
    "${dest}/usr/lib/${pkgname}/User/config.json" \
    "${dest}/usr/lib/${pkgname}/User/linux-host.json" \
    "${dest}/usr/lib/${pkgname}/User/Account" \
    "${dest}/usr/lib/${pkgname}/User/AccountRecords" \
    "${dest}/usr/lib/${pkgname}/log" \
    "${dest}/usr/lib/${pkgname}/Data" \
    "${dest}/usr/lib/${pkgname}/Repos"
  find "${dest}/usr/lib/${pkgname}" -iname '*UnitTest*' -prune -exec rm -rf {} +
  find "${dest}/usr/lib/${pkgname}" \( -iname '*fakegame*' -o -iname '*wpftmp*' \) -prune -exec rm -rf {} +
  find "${dest}/usr/lib/${pkgname}" -name '*.pdb' -delete
  # 不要把构建机的密钥、cookie、token 带进包
  find "${dest}/usr/lib/${pkgname}" \( \
      -iname '*credentials*' -o -iname '*secrets.json' -o -iname '*.cookie' \
      -o -iname 'user.reg' -o -iname '*.kdbx' \
    \) -delete

  if [[ ! -x "${dest}/usr/lib/${pkgname}/bgi-avalonia" && ! -f "${dest}/usr/lib/${pkgname}/bgi-avalonia.dll" ]]; then
    echo "发布结果缺少 bgi-avalonia，构建失败" >&2
    return 1
  fi
  chmod 755 "${dest}/usr/lib/${pkgname}/bgi-avalonia" 2>/dev/null || true
  find "${dest}/usr/lib/${pkgname}" -type f -name '*.so' -exec chmod 755 {} +
  find "${dest}/usr/lib/${pkgname}" -type d -exec chmod 755 {} +
  # 发布目录里的文件保持只读，运行时写入走 --data-dir
  find "${dest}/usr/lib/${pkgname}" -type f -exec chmod a-w {} +
  chmod 755 "${dest}/usr/lib/${pkgname}/bgi-avalonia" 2>/dev/null || true

  install -Dm755 "${startdir}/better-genshin-impact.sh" "${dest}/usr/bin/better-genshin-impact"
  ln -sf better-genshin-impact "${dest}/usr/bin/bgi-avalonia"
  install -Dm644 "${startdir}/better-genshin-impact.desktop" "${dest}/usr/share/applications/${pkgname}.desktop"
  install -Dm644 "${_repo}/BetterGenshinImpact/Resources/Images/logo.png" \
    "${dest}/usr/share/icons/hicolor/400x400/apps/${pkgname}.png"
  install -Dm644 "${_repo}/BetterGenshinImpact/Resources/Images/logo.png" \
    "${dest}/usr/share/pixmaps/${pkgname}.png"
  install -Dm644 "${_repo}/LICENSE" "${dest}/usr/share/licenses/${pkgname}/LICENSE"
  install -Dm644 "${startdir}/README.md" "${dest}/usr/share/doc/${pkgname}/README.md"
  install -Dm644 "${_repo}/Build/Linux/README.md" "${dest}/usr/share/licenses/${pkgname}/OpenCvSharpExtern-README.md"
}

