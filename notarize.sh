#!/bin/bash
# notarize.sh —— 只做"提交苹果公证 + staple"。
# 适用于:DMG 已经构建并签名好,只是因为网络/VPN 等问题公证失败需要重试。
#
# 用法:
#   ./notarize.sh                       # 默认对 OKXMonitor-0.1.0.dmg
#   ./notarize.sh OKXMonitor-0.2.0.dmg  # 指定 DMG
#
# 前置:
#   * 该 DMG 已经用 Developer ID 签名(release.sh 已经做了)
#   * 已经配过 keychain profile: xcrun notarytool store-credentials notarytool-okxmonitor ...
#
# 提示:
#   如果一直卡在 "initiating connection to the Apple notary service"
#   超过 2 分钟没有 submission id,八成是 VPN/代理拦了 Apple 公证 S3 上传节点。
#   关掉 VPN 重跑本脚本是最快的解决方式。

set -euo pipefail
cd "$(dirname "$0")"

DMG="${1:-OKXMonitor-0.1.0.dmg}"
NOTARY_PROFILE="${OKX_NOTARY_PROFILE:-notarytool-okxmonitor}"

if [[ ! -f "$DMG" ]]; then
    echo "ERR: 找不到 $DMG。先跑 ./release.sh 生成。" >&2
    exit 1
fi

echo "==> 公证 $DMG (profile: $NOTARY_PROFILE)"
echo "==> 通常 1-5 分钟,会实时打印进度"
echo ""

# verbose 直接打到终端(不走 pipe,避免缓冲)
xcrun notarytool submit "$DMG" \
    --keychain-profile "$NOTARY_PROFILE" \
    --verbose --wait

echo ""
echo "==> Staple 公证票据到 DMG…"
xcrun stapler staple "$DMG"

echo ""
echo "==> 校验最终产物…"
xcrun stapler validate "$DMG"
spctl --assess --type open --context context:primary-signature --verbose "$DMG" 2>&1 | tail -3

echo ""
echo "✅ Done: $(pwd)/$DMG"
echo "   已通过 Apple 公证并 staple,可发布。"
