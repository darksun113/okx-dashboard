#!/bin/bash
# Builds OKXMonitor.app and packages it into a distributable DMG with a
# drag-to-Applications shortcut.
set -euo pipefail
cd "$(dirname "$0")"

APP="OKXMonitor.app"
VOL="OKX Monitor"
DMG="OKXMonitor.dmg"

# 1. Build a fresh, signed .app.
./build.sh

# 2. Stage the DMG contents: the app + an /Applications symlink.
STAGE="$(mktemp -d)/dmg"
mkdir -p "$STAGE"
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"

# A short readme so recipients know how to get past Gatekeeper.
cat > "$STAGE/安装说明.txt" <<'TXT'
OKX Monitor 安装方法
=====================

1. 把 OKXMonitor.app 拖到右边的「Applications」文件夹。
2. 第一次打开：在 应用程序 里【右键点 OKXMonitor → 打开】，
   在弹窗里再点一次「打开」。
   （因为是自签名 App，直接双击会被 macOS 拦截，右键打开可绕过。）
3. App 没有 Dock 图标，它会以悬浮小窗 + 菜单栏图标的形式运行。
4. 点小窗右上角齿轮 ⚙️，填入【只读权限】的 OKX API Key / Secret / Passphrase。

如遇 Keychain 授权弹窗，请点「始终允许」。
TXT

# 3. Create a compressed DMG.
rm -f "$DMG"
hdiutil create -volname "$VOL" -srcfolder "$STAGE" -ov -format UDZO "$DMG" >/dev/null

# 4. Sign the DMG with the same stable identity (best-effort).
codesign --force --sign "OKXMonitor Self-Signed" "$DMG" 2>/dev/null || true

rm -rf "$(dirname "$STAGE")"
SIZE="$(du -h "$DMG" | cut -f1)"
echo "==> Done: $(pwd)/$DMG  ($SIZE)"
