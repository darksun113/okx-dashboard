#!/bin/bash
# Builds OKXMonitor.app — a self-contained, always-on-top desktop widget.
set -euo pipefail
cd "$(dirname "$0")"

APP="OKXMonitor.app"
BIN_NAME="OKXMonitor"

echo "==> Compiling (release)…"
swift build -c release

BIN_PATH="$(swift build -c release --show-bin-path)/$BIN_NAME"

echo "==> Assembling $APP …"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS"
mkdir -p "$APP/Contents/Resources"
cp "$BIN_PATH" "$APP/Contents/MacOS/$BIN_NAME"

# Bundle the app icon if present (generate it with ./make-icon.sh).
ICON_LINE=""
if [[ -f AppIcon.icns ]]; then
    cp AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"
    ICON_LINE="    <key>CFBundleIconFile</key>        <string>AppIcon</string>"
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
    <key>CFBundleVersion</key>        <string>1.0</string>
    <key>CFBundleShortVersionString</key> <string>1.0</string>
    <key>CFBundlePackageType</key>    <string>APPL</string>
    <key>LSMinimumSystemVersion</key> <string>13.0</string>
    <key>LSUIElement</key>            <true/>
    <key>NSHighResolutionCapable</key><true/>
$ICON_LINE
</dict>
</plist>
PLIST

# Sign with a STABLE self-signed identity so the app keeps the same code identity
# across rebuilds. That way macOS Keychain's "Always Allow" sticks permanently and
# you are never re-prompted after the first time. Falls back to ad-hoc if creation fails.
IDENTITY="OKXMonitor Self-Signed"

have_identity() {
    # List includes untrusted self-signed certs (no -v), which is what we have.
    security find-identity -p codesigning 2>/dev/null | grep -qF "$IDENTITY"
}

ensure_identity() {
    have_identity && return 0
    echo "==> Creating one-time self-signed signing certificate '$IDENTITY' …"
    local tmp pw; tmp="$(mktemp -d)"; pw="okxmonitor"
    cat > "$tmp/cfg" <<CFG
[req]
distinguished_name = dn
x509_extensions = v3
prompt = no
[dn]
CN = $IDENTITY
[v3]
keyUsage = critical, digitalSignature
extendedKeyUsage = critical, codeSigning
basicConstraints = critical, CA:false
CFG
    openssl req -x509 -newkey rsa:2048 -days 3650 -nodes \
        -keyout "$tmp/key.pem" -out "$tmp/cert.pem" -config "$tmp/cfg" >/dev/null 2>&1
    # macOS `security` needs a non-empty password and the legacy SHA1 MAC.
    openssl pkcs12 -export -legacy -macalg sha1 \
        -inkey "$tmp/key.pem" -in "$tmp/cert.pem" \
        -out "$tmp/id.p12" -passout "pass:$pw" >/dev/null 2>&1
    # -A lets codesign use the private key without prompting on every build.
    security import "$tmp/id.p12" -k "$HOME/Library/Keychains/login.keychain-db" \
        -P "$pw" -A -T /usr/bin/codesign >/dev/null 2>&1
    rm -rf "$tmp"
    have_identity
}

if ensure_identity; then
    echo "==> Signing with '$IDENTITY' (stable identity — Keychain 'Always Allow' will persist)"
    codesign --force --deep --sign "$IDENTITY" "$APP"
else
    echo "==> Self-signed identity unavailable; falling back to ad-hoc signing."
    codesign --force --deep --sign - "$APP" 2>/dev/null || true
fi

echo "==> Done: $(pwd)/$APP"
echo "    运行: open \"$(pwd)/$APP\"   (或双击)"
