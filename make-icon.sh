#!/bin/bash
# Generates AppIcon.icns from make-icon.swift. Run once (or to refresh the icon).
set -euo pipefail
cd "$(dirname "$0")"

MASTER="icon-master.png"
ICONSET="AppIcon.iconset"

echo "==> Rendering master PNG…"
swift make-icon.swift "$MASTER"

echo "==> Building iconset…"
rm -rf "$ICONSET"; mkdir "$ICONSET"
sips -z 16 16     "$MASTER" --out "$ICONSET/icon_16x16.png"      >/dev/null
sips -z 32 32     "$MASTER" --out "$ICONSET/icon_16x16@2x.png"   >/dev/null
sips -z 32 32     "$MASTER" --out "$ICONSET/icon_32x32.png"      >/dev/null
sips -z 64 64     "$MASTER" --out "$ICONSET/icon_32x32@2x.png"   >/dev/null
sips -z 128 128   "$MASTER" --out "$ICONSET/icon_128x128.png"    >/dev/null
sips -z 256 256   "$MASTER" --out "$ICONSET/icon_128x128@2x.png" >/dev/null
sips -z 256 256   "$MASTER" --out "$ICONSET/icon_256x256.png"    >/dev/null
sips -z 512 512   "$MASTER" --out "$ICONSET/icon_256x256@2x.png" >/dev/null
sips -z 512 512   "$MASTER" --out "$ICONSET/icon_512x512.png"    >/dev/null
cp "$MASTER" "$ICONSET/icon_512x512@2x.png"

echo "==> Packing AppIcon.icns…"
iconutil -c icns "$ICONSET" -o AppIcon.icns
rm -rf "$ICONSET" "$MASTER"
echo "==> Done: $(pwd)/AppIcon.icns"
