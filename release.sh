#!/bin/bash
# release.sh —— 生产发布构建。
# 用 Developer ID 证书签名 + hardened runtime + Apple 公证 + staple,
# 产出可以直接双击运行、零 Gatekeeper 警告的 DMG。
#
# 用法:
#   ./release.sh              # 用默认版本号 0.1.0
#   ./release.sh 0.2.0        # 指定版本号
#
# 环境变量:
#   OKX_SIGN_ID         代码签名身份(默认自动选择 Developer ID Application)
#   OKX_NOTARY_PROFILE  notarytool keychain profile 名(默认 notarytool-okxmonitor)
#
# 前置:
#   1. 钥匙串里有 "Developer ID Application: ..." 证书
#   2. 已运行过: xcrun notarytool store-credentials notarytool-okxmonitor ...

set -euo pipefail
cd "$(dirname "$0")"

VERSION="${1:-0.1.0}"
APP="OKXMonitor.app"
BIN_NAME="OKXMonitor"
DMG="OKXMonitor-${VERSION}.dmg"
VOL="OKX Monitor"

SIGN_ID="${OKX_SIGN_ID:-$(security find-identity -v -p codesigning 2>/dev/null \
    | awk -F'"' '/Developer ID Application/{print $2; exit}')}"
NOTARY_PROFILE="${OKX_NOTARY_PROFILE:-notarytool-okxmonitor}"

if [[ -z "$SIGN_ID" ]]; then
    echo "ERR: 钥匙串里没有 'Developer ID Application' 证书。先去 Apple Developer 网站下载并装好。" >&2
    exit 1
fi

echo "==> 签名身份: $SIGN_ID"
echo "==> 公证 profile: $NOTARY_PROFILE"
echo "==> 版本: $VERSION"
echo ""

# 1. Release 编译
echo "==> 编译 (release)…"
swift build -c release
BIN_PATH="$(swift build -c release --show-bin-path)/$BIN_NAME"

# 2. 组装 .app(版本号写入 Info.plist)
echo "==> 组装 ${APP}…"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN_PATH" "$APP/Contents/MacOS/$BIN_NAME"

ICON_LINE=""
if [[ -f AppIcon.icns ]]; then
    cp AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"
    ICON_LINE='    <key>CFBundleIconFile</key>        <string>AppIcon</string>'
fi

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>           <string>OKX Monitor</string>
    <key>CFBundleDisplayName</key>    <string>OKX Monitor</string>
    <key>CFBundleIdentifier</key>     <string>com.okxmonitor.app</string>
    <key>CFBundleExecutable</key>     <string>$BIN_NAME</string>
    <key>CFBundleVersion</key>        <string>$VERSION</string>
    <key>CFBundleShortVersionString</key> <string>$VERSION</string>
    <key>CFBundlePackageType</key>    <string>APPL</string>
    <key>LSMinimumSystemVersion</key> <string>13.0</string>
    <key>LSUIElement</key>            <true/>
    <key>NSHighResolutionCapable</key><true/>
$ICON_LINE
</dict>
</plist>
PLIST

# 3. 用 Developer ID 签名 .app —— hardened runtime + timestamp,公证必需
echo "==> 签名 $APP (hardened runtime, secure timestamp)…"
codesign --force --options runtime --timestamp \
    --sign "$SIGN_ID" "$APP"

echo "==> 验证签名…"
codesign --verify --deep --strict --verbose=2 "$APP" 2>&1 | tail -3

# 4. 打 DMG
echo "==> 打包 ${DMG}…"
STAGE="$(mktemp -d)/dmg"
mkdir -p "$STAGE"
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"

# 公证后的版本不再需要"右键打开"绕 Gatekeeper 的说明
cat > "$STAGE/安装说明.txt" <<'TXT'
OKX Monitor 安装方法
=====================

1. 把 OKXMonitor.app 拖到右边的「Applications」文件夹。
2. 在 应用程序 里双击打开即可 —— 已通过 Apple 公证,无 Gatekeeper 警告。
3. App 没有 Dock 图标,以悬浮小窗 + 菜单栏图标的形式运行。
4. 点小窗右上角齿轮 ⚙️,填入【只读权限】的 OKX API Key / Secret / Passphrase。
5. 凭据安全存入 macOS Keychain,可在「钥匙串访问」搜 okxmonitor 查看/删除。

如遇 Keychain 授权弹窗,请点「始终允许」。
TXT

rm -f "$DMG"
hdiutil create -volname "$VOL" -srcfolder "$STAGE" -ov -format UDZO "$DMG" >/dev/null
rm -rf "$(dirname "$STAGE")"

# 剥扩展属性 —— hdiutil 偶尔会写入怪异的 com.apple.FinderInfo,
# 会让 notarytool 的 open() 系统调用挂死(EAGAIN)。
xattr -c "$DMG"

# 5. 签名 DMG
echo "==> 签名 ${DMG}…"
codesign --force --sign "$SIGN_ID" --timestamp "$DMG"

# 6. 提交苹果公证(等待结果,一般 1-5 分钟)
echo ""
echo "==> 提交苹果公证服务,等待中…(通常 1-5 分钟,Apple 服务器忙时更久)"
xcrun notarytool submit "$DMG" \
    --keychain-profile "$NOTARY_PROFILE" \
    --wait

# 7. 把公证票据 staple 进 DMG —— 离线也能识别
echo "==> Staple 公证票据…"
xcrun stapler staple "$DMG"

# 8. 最终校验
echo ""
echo "==> 校验最终产物…"
xcrun stapler validate "$DMG"
spctl --assess --type open --context context:primary-signature --verbose "$DMG" 2>&1 | tail -3

SIZE="$(du -h "$DMG" | cut -f1)"
echo ""
echo "✅ Done: $(pwd)/$DMG  ($SIZE)"
echo "   这个 DMG 已经过 Apple 公证并 staple,用户双击即可安装,无 Gatekeeper 警告。"
