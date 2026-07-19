#!/usr/bin/env bash
# =============================================================================
# Mabel Host macOS — no-Mac build + package + sign pipeline (runs on Linux/WSL)
#
#   1. Cross-compiles the AppKit host with the xtool Darwin Swift SDK.
#   2. Assembles a MabelHost.app bundle (Info.plist + Mach-O).
#   3. Ad-hoc signs the bundle with rcodesign (Apple codesign, in Rust).
#
# Requirements (see docs/macos-host.md):
#   - Swift 6.1 toolchain          (default: $HOME/swift/usr/bin/swift)
#   - Darwin Swift SDK registered  (swift sdk list  ->  "darwin")
#   - rcodesign on PATH            (cargo install apple-codesign)
#
# NOTE: This BUILDS and SIGNS without a Mac. RUNNING the .app still needs
#       macOS — that step is deferred (documented limitation).
# =============================================================================
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SWIFT="${SWIFT:-$HOME/swift/usr/bin/swift}"
SDK="${MABEL_MACOS_SDK:-arm64-apple-macosx}"
ARCH_DIR="${SDK}"                 # .build subdir matches the triple
CONFIG="${CONFIG:-debug}"
APP_NAME="MabelHost"
BUNDLE_ID="com.mabel.host.macos"
OUT="${HERE}/build"
APP="${OUT}/${APP_NAME}.app"

echo "==> Mabel Host macOS build (no-Mac)"
echo "    swift : ${SWIFT}"
echo "    sdk   : ${SDK}"

# ---------------------------------------------------------------------------
# 1. Cross-compile.
#
# SwiftPM auto-applies debug entitlements to executables via `codesign`, which
# does not exist on Linux. The Mach-O is fully linked BEFORE that step, so we
# shim `codesign` with a no-op for the build and do the real signing with
# rcodesign afterwards.
# ---------------------------------------------------------------------------
SHIM="$(mktemp -d)"
printf '#!/bin/sh\nexit 0\n' > "${SHIM}/codesign"
chmod +x "${SHIM}/codesign"

echo "==> Building (swift build --swift-sdk ${SDK})"
PATH="${SHIM}:${PATH}" "${SWIFT}" build \
    --swift-sdk "${SDK}" \
    -c "${CONFIG}" \
    --package-path "${HERE}"
rm -rf "${SHIM}"

BIN="${HERE}/.build/${ARCH_DIR}/${CONFIG}/MabelHostApp"
if [ ! -f "${BIN}" ]; then
    echo "!! build failed: ${BIN} not found" >&2
    exit 1
fi
echo "==> Linked Mach-O: $(file -b "${BIN}")"

# ---------------------------------------------------------------------------
# 2. Assemble the .app bundle.
# ---------------------------------------------------------------------------
echo "==> Packaging ${APP_NAME}.app"
rm -rf "${APP}"
mkdir -p "${APP}/Contents/MacOS" "${APP}/Contents/Resources"
cp "${BIN}" "${APP}/Contents/MacOS/${APP_NAME}"
printf 'APPL????' > "${APP}/Contents/PkgInfo"

cat > "${APP}/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>${APP_NAME}</string>
    <key>CFBundleDisplayName</key><string>Mabel Host</string>
    <key>CFBundleExecutable</key><string>${APP_NAME}</string>
    <key>CFBundleIdentifier</key><string>${BUNDLE_ID}</string>
    <key>CFBundleVersion</key><string>0.1.0</string>
    <key>CFBundleShortVersionString</key><string>0.1.0</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>LSMinimumSystemVersion</key><string>13.0</string>
    <key>NSHighResolutionCapable</key><true/>
    <key>NSPrincipalClass</key><string>NSApplication</string>
</dict>
</plist>
PLIST

# ---------------------------------------------------------------------------
# 3. Ad-hoc sign with rcodesign (no Apple certificate needed).
#    rcodesign performs ad-hoc signing when no signing key is supplied.
# ---------------------------------------------------------------------------
echo "==> Signing with rcodesign (ad-hoc)"
rcodesign sign "${APP}"

echo "==> Verifying signature (embedded in the Mach-O)"
rcodesign print-signature-info "${APP}/Contents/MacOS/${APP_NAME}" 2>/dev/null | head -30 || true

echo ""
echo "==> DONE: ${APP}"
echo "    Ad-hoc signed, ready to copy to a Mac to run."
echo "    (Running on Linux is not possible — AppKit needs macOS.)"
