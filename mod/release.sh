#!/bin/bash
# Build the mod and package a release zip.
# Usage: ./mod/release.sh [version]
# Example: ./mod/release.sh 0.5.0

set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
REPO_DIR="$( cd "$SCRIPT_DIR/.." && pwd )"

VERSION="${1:-$(date +%Y%m%d)}"
RELEASE_NAME="DiscoElysiumAccessibilityMod-$VERSION"
STAGING_DIR="$REPO_DIR/release/$RELEASE_NAME"
ZIP_FILE="$REPO_DIR/release/$RELEASE_NAME.zip"

# Check for game path
if [ -z "$DISCO_ELYSIUM_PATH" ]; then
    export DISCO_ELYSIUM_PATH="/mnt/c/Program Files (x86)/Steam/steamapps/common/Disco Elysium"
fi

if [ ! -d "$DISCO_ELYSIUM_PATH" ]; then
    echo "Error: Game directory not found at $DISCO_ELYSIUM_PATH"
    echo "Set DISCO_ELYSIUM_PATH to your Disco Elysium installation."
    exit 1
fi

# Build first
echo "Building mod..."
"$SCRIPT_DIR/build.sh"
echo ""

# Clean and create staging directory
rm -rf "$STAGING_DIR"
mkdir -p "$STAGING_DIR/Mods"
mkdir -p "$STAGING_DIR/UserData"

# Copy mod DLL
cp "$SCRIPT_DIR/bin/Release/net6.0/AccessibilityMod.dll" "$STAGING_DIR/Mods/"

# Copy Tolk and NVDA controller from game directory
cp "$DISCO_ELYSIUM_PATH/Tolk.dll" "$STAGING_DIR/"
cp "$DISCO_ELYSIUM_PATH/nvdaControllerClient64.dll" "$STAGING_DIR/"

# Copy data files
if [ -d "$SCRIPT_DIR/Data" ]; then
    cp "$SCRIPT_DIR/Data/"* "$STAGING_DIR/UserData/"
fi

echo "Packaging release..."
cd "$REPO_DIR/release"
zip -r "$ZIP_FILE" "$RELEASE_NAME"
cd "$REPO_DIR"

# Clean up staging directory
rm -rf "$STAGING_DIR"

echo ""
echo "Release packaged: $ZIP_FILE"
echo ""
echo "Contents:"
unzip -l "$ZIP_FILE"
