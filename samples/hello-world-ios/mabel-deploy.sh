#!/bin/bash
# Mabel Deploy - Deploy iOS app without device registration
# Workaround for xtool device limit issue on free developer accounts

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${GREEN}Mabel Deploy - iOS App Deployment${NC}"
echo "===================================="
echo ""

# Check dependencies
if ! command -v ideviceinstaller &> /dev/null; then
    echo -e "${RED}Error: ideviceinstaller not found${NC}"
    echo "Install with: sudo apt-get install ideviceinstaller"
    exit 1
fi

# Find device
DEVICE_UDID=$(idevice_id -l 2>/dev/null | head -1)
if [ -z "$DEVICE_UDID" ]; then
    echo -e "${RED}Error: No iOS device connected${NC}"
    echo "Please connect your iPhone and trust this computer"
    exit 1
fi

echo "Found device: $DEVICE_UDID"

# Check if xtool build exists
APP_PATH="xtool/MabelHello.app"
if [ ! -d "$APP_PATH" ]; then
    echo -e "${RED}Error: App not found at $APP_PATH${NC}"
    echo "Run: xtool dev build"
    exit 1
fi

echo "App found: $APP_PATH"

# Create Payload directory and IPA
IPA_NAME="MabelHello.ipa"
PAYLOAD_DIR="Payload"

echo "Creating IPA package..."
rm -rf "$PAYLOAD_DIR" "$IPA_NAME"
mkdir -p "$PAYLOAD_DIR"
cp -r "$APP_PATH" "$PAYLOAD_DIR/"
zip -qr "$IPA_NAME" "$PAYLOAD_DIR"
rm -rf "$PAYLOAD_DIR"

echo -e "${GREEN}IPA created: $IPA_NAME${NC}"

# Try to install
echo ""
echo "Installing to device..."
if ideviceinstaller -u "$DEVICE_UDID" -i "$IPA_NAME" 2>&1; then
    echo -e "${GREEN}✓ App installed successfully!${NC}"
    echo "Check your iPhone - the app should appear on the home screen"
else
    echo -e "${YELLOW}⚠ Installation may have failed${NC}"
    echo "Common causes:"
    echo "  - App not signed for this device"
    echo "  - Device not provisioned"
    echo "  - Need to rebuild with xtool dev (not dev run)"
    echo ""
    echo "Try running: xtool dev (without 'run') to provision the device first"
fi

# Cleanup
rm -f "$IPA_NAME"
